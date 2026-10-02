using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

[assembly: InternalsVisibleTo("MinePack.Smoke")]

namespace MinePack.Core;

public static class InstanceUseGuard
{
    private const int ProcessCommandLineInformation = 60;
    private const uint ProcessQueryInformation = 0x0400;
    private const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);
    private const int MaximumCommandLineBuffer = 128 * 1024;
    private static readonly AsyncLocal<Lease?> CurrentLease = new();
    private static ProcessInspectionForTesting? _processInspectionForTesting;

    public static Scope Acquire(string gameDirectory, IEnumerable<string>? managedPaths = null)
    {
        var processInspection = Volatile.Read(ref _processInspectionForTesting);
        return AcquireCore(gameDirectory, managedPaths,
            processInspection?.Candidates, processInspection?.CommandLineReader);
    }

    internal static IDisposable UseProcessInspectionForTesting(
        IEnumerable<(int ProcessId, string ProcessName)> processCandidates, Func<int, string> commandLineReader)
    {
        ArgumentNullException.ThrowIfNull(processCandidates);
        ArgumentNullException.ThrowIfNull(commandLineReader);
        var installed = new ProcessInspectionForTesting(processCandidates.ToArray(), commandLineReader);
        var previous = Interlocked.Exchange(ref _processInspectionForTesting, installed);
        return new ProcessInspectionOverride(installed, previous);
    }

    internal static Scope AcquireForTesting(string gameDirectory, IEnumerable<string>? managedPaths,
        IEnumerable<(int ProcessId, string ProcessName)> processCandidates, Func<int, string> commandLineReader) =>
        AcquireCore(gameDirectory, managedPaths, processCandidates.ToArray(), commandLineReader);

    private static Scope AcquireCore(string gameDirectory, IEnumerable<string>? managedPaths,
        IReadOnlyList<(int ProcessId, string ProcessName)>? processCandidates, Func<int, string>? commandLineReader)
    {
        var fullGameDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameDirectory));
        var current = CurrentLease.Value;
        if (current is { IsActive: true })
        {
            if (!current.GameDirectory.Equals(fullGameDirectory, StringComparison.OrdinalIgnoreCase))
                throw new InstallerException("GAME_USE_UNVERIFIED", LocalizedText.Get("GameUseUnverified"));
            current.Protect(managedPaths);
            current.Recheck();
            return new Scope(current, ownsLease: false, previousLease: null);
        }

        var lease = new Lease(fullGameDirectory, processCandidates, commandLineReader);
        var previousLease = current;
        try
        {
            lease.Recheck();
            lease.Protect(managedPaths);
            lease.Recheck();
            CurrentLease.Value = lease;
            return new Scope(lease, ownsLease: true, previousLease);
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    internal static void EnsureNoGameProcess(string gameDirectory,
        IEnumerable<(int ProcessId, string ProcessName)>? processCandidates = null,
        Func<int, string>? commandLineReader = null)
    {
        if (!OperatingSystem.IsWindows())
            throw new InstallerException("GAME_USE_UNVERIFIED", LocalizedText.Get("GameUseUnverified"));

        IReadOnlyList<(int ProcessId, string ProcessName)> candidates;
        try { candidates = (processCandidates ?? FindJavaProcesses()).ToArray(); }
        catch (Exception ex)
        {
            throw new InstallerException("GAME_USE_UNVERIFIED", LocalizedText.Get("GameUseUnverified"), ex);
        }

        var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameDirectory));
        foreach (var process in candidates)
        {
            string commandLine;
            try { commandLine = (commandLineReader ?? ReadProcessCommandLine)(process.ProcessId); }
            catch (Exception) when (HasExited(process.ProcessId)) { continue; }
            catch (Exception ex)
            {
                throw new InstallerException("GAME_USE_UNVERIFIED", LocalizedText.Get("GameUseUnverified"), ex);
            }

            try
            {
                var arguments = ParseCommandLine(commandLine);
                var isMinecraft = IsMinecraftLaunch(arguments);
                var gameDirectories = ReadGameDirectories(arguments, out var malformed);
                if (malformed)
                {
                    if (isMinecraft)
                        throw new InstallerException("GAME_USE_UNVERIFIED", LocalizedText.Get("GameUseUnverified"));
                    continue;
                }

                if (gameDirectories.Count == 0)
                {
                    if (isMinecraft)
                        throw new InstallerException("GAME_USE_UNVERIFIED", LocalizedText.Get("GameUseUnverified"));
                    continue;
                }
                if (!isMinecraft) continue;
                if (gameDirectories.Count != 1)
                    throw new InstallerException("GAME_USE_UNVERIFIED", LocalizedText.Get("GameUseUnverified"));

                if (!Path.IsPathFullyQualified(gameDirectories[0]) ||
                    gameDirectories[0].StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase) ||
                    gameDirectories[0].StartsWith(@"\\.\", StringComparison.OrdinalIgnoreCase))
                    throw new InstallerException("GAME_USE_UNVERIFIED", LocalizedText.Get("GameUseUnverified"));

                string candidate;
                try { candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameDirectories[0])); }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    throw new InstallerException("GAME_USE_UNVERIFIED", LocalizedText.Get("GameUseUnverified"), ex);
                }
                if (candidate.Equals(target, StringComparison.OrdinalIgnoreCase))
                    throw new InstallerException("GAME_IN_USE", LocalizedText.Get("GameIsRunning"));
            }
            catch (InstallerException) { throw; }
            catch (Exception ex)
            {
                throw new InstallerException("GAME_USE_UNVERIFIED", LocalizedText.Get("GameUseUnverified"), ex);
            }
        }
    }

    internal static string ReadProcessCommandLine(int processId)
    {
        using var process = OpenProcess(ProcessQueryInformation, inheritHandle: false, (uint)processId);
        if (process.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (GetProcessId(process) != (uint)processId) throw new InvalidOperationException("Process identity changed during inspection.");

        var status = NtQueryInformationProcess(process, ProcessCommandLineInformation, IntPtr.Zero, 0, out var required);
        if (status != StatusInfoLengthMismatch || required < Marshal.SizeOf<UnicodeString>() || required > MaximumCommandLineBuffer)
            throw new InvalidOperationException("The process command line is unavailable or outside the supported buffer limit.");

        var buffer = Marshal.AllocHGlobal(checked((int)required));
        try
        {
            status = NtQueryInformationProcess(process, ProcessCommandLineInformation, buffer, required, out var returned);
            var headerSize = Marshal.SizeOf<UnicodeString>();
            if (status < 0 || returned < headerSize || returned > required)
                throw new InvalidOperationException("The process command line response is invalid.");

            var commandLine = Marshal.PtrToStructure<UnicodeString>(buffer);
            if (commandLine.Buffer == IntPtr.Zero || commandLine.Length == 0 ||
                commandLine.Length % sizeof(char) != 0 || commandLine.Length > commandLine.MaximumLength)
                throw new InvalidOperationException("The process command line metadata is invalid.");

            var start = buffer.ToInt64();
            var end = checked(start + returned);
            var textStart = commandLine.Buffer.ToInt64();
            var textEnd = checked(textStart + commandLine.Length);
            if (textStart < checked(start + headerSize) || textEnd > end)
                throw new InvalidOperationException("The process command line pointer is outside its response buffer.");

            var value = Marshal.PtrToStringUni(commandLine.Buffer, commandLine.Length / sizeof(char))
                ?? throw new InvalidOperationException("The process command line could not be decoded.");
            if (value.Contains('\0')) throw new InvalidOperationException("The process command line contains an embedded terminator.");
            return value;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static IEnumerable<(int ProcessId, string ProcessName)> FindJavaProcesses()
    {
        foreach (var name in new[] { "java", "javaw" })
        foreach (var process in Process.GetProcessesByName(name))
        {
            using (process)
            {
                if (!process.HasExited) yield return (process.Id, name);
            }
        }
    }

    private static bool HasExited(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.HasExited;
        }
        catch (ArgumentException) { return true; }
        catch (InvalidOperationException) { return true; }
        catch { return false; }
    }

    private static string[] ParseCommandLine(string commandLine)
    {
        var argumentsPointer = CommandLineToArgvW(commandLine, out var count);
        if (argumentsPointer == IntPtr.Zero)
            throw new InvalidOperationException("The process command line could not be parsed.");
        try
        {
            if (count is < 1 or > 4096)
                throw new InvalidOperationException("The process command line argument count is invalid.");
            var arguments = new string[count];
            for (var index = 0; index < count; index++)
            {
                var argumentPointer = Marshal.ReadIntPtr(argumentsPointer, index * IntPtr.Size);
                arguments[index] = Marshal.PtrToStringUni(argumentPointer)
                    ?? throw new InvalidOperationException("A process argument could not be decoded.");
            }
            return arguments;
        }
        finally { _ = LocalFree(argumentsPointer); }
    }

    private static bool IsMinecraftLaunch(IEnumerable<string> arguments)
    {
        var markers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "net.minecraft.client.main.Main",
            "net.fabricmc.loader.impl.launch.knot.KnotClient",
            "org.prismlauncher.EntryPoint",
            "org.multimc.EntryPoint"
        };
        return arguments.Any(markers.Contains);
    }

    private static List<string> ReadGameDirectories(IReadOnlyList<string> arguments, out bool malformed)
    {
        malformed = false;
        var result = new List<string>();
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (argument.Equals("--gameDir", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= arguments.Count || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    malformed = true;
                    continue;
                }
                result.Add(arguments[++index]);
            }
            else if (argument.StartsWith("--gameDir=", StringComparison.OrdinalIgnoreCase))
            {
                var value = argument["--gameDir=".Length..];
                if (value.Length == 0) malformed = true;
                else result.Add(value);
            }
        }
        return result;
    }

    internal sealed class Lease(string gameDirectory,
        IReadOnlyList<(int ProcessId, string ProcessName)>? processCandidates,
        Func<int, string>? commandLineReader) : IDisposable
    {
        private readonly List<FileStream> _handles = [];
        private readonly HashSet<string> _protectedPaths = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FileStream> _managedHandles = new(StringComparer.OrdinalIgnoreCase);
        private int _active = 1;

        public string GameDirectory { get; } = gameDirectory;
        public bool IsActive => Volatile.Read(ref _active) != 0;

        public void Recheck()
        {
            if (!IsActive) throw new InstallerException("GAME_USE_UNVERIFIED", LocalizedText.Get("GameUseUnverified"));
            EnsureNoGameProcess(GameDirectory, processCandidates, commandLineReader);
            ProtectGameWorldLocks();
            EnsureNoGameProcess(GameDirectory, processCandidates, commandLineReader);
        }

        public void Protect(IEnumerable<string>? managedPaths)
        {
            if (!IsActive) throw new InstallerException("GAME_USE_UNVERIFIED", LocalizedText.Get("GameUseUnverified"));
            ProtectGameWorldLocks();
            if (managedPaths is not null)
                foreach (var relative in managedPaths)
                {
                    var target = SafePath.Resolve(GameDirectory, relative);
                    ProtectManagedFile(target);
                }
        }

        public void ProtectManagedFile(string path)
        {
            if (!IsActive) throw new InstallerException("GAME_USE_UNVERIFIED", LocalizedText.Get("GameUseUnverified"));
            SafePath.EnsureNoReparsePoints(GameDirectory, path);
            var fullPath = Path.GetFullPath(path);
            if (_managedHandles.ContainsKey(fullPath) || ReadAttributes(fullPath) is null) return;
            try
            {
                var handle = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Delete);
                _handles.Add(handle);
                _managedHandles[fullPath] = handle;
                _protectedPaths.Add(fullPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new InstallerException("INSTANCE_IN_USE", LocalizedText.Get("InstanceFileInUse"), ex);
            }
        }

        public void ProtectCommittedFile(string path)
        {
            if (!IsActive) throw new InstallerException("GAME_USE_UNVERIFIED", LocalizedText.Get("GameUseUnverified"));
            var fullPath = Path.GetFullPath(path);
            SafePath.EnsureNoReparsePoints(GameDirectory, fullPath);
            if (ReadAttributes(fullPath) is null) return;
            try
            {
                var handle = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Delete);
                _handles.Add(handle);
                _managedHandles[fullPath] = handle;
                _protectedPaths.Add(fullPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new InstallerException("INSTANCE_IN_USE", LocalizedText.Get("InstanceFileInUse"), ex);
            }
        }

        public void ReleaseManagedFile(string path)
        {
            var fullPath = Path.GetFullPath(path);
            if (!_managedHandles.Remove(fullPath, out var handle)) return;
            _protectedPaths.Remove(fullPath);
            _handles.Remove(handle);
            handle.Dispose();
        }

        public string? HashManagedFile(string path)
        {
            var fullPath = Path.GetFullPath(path);
            if (!_managedHandles.TryGetValue(fullPath, out var stream)) return null;
            stream.Position = 0;
            return Convert.ToHexString(System.Security.Cryptography.SHA512.HashData(stream));
        }

        private void ProtectGameWorldLocks()
        {
            var saves = Path.Combine(GameDirectory, "saves");
            var savesAttributes = ReadAttributes(saves);
            if (savesAttributes is null) return;
            SafePath.EnsureNoReparsePoints(GameDirectory, saves);
            try
            {
                foreach (var world in Directory.EnumerateDirectories(saves))
                {
                    var attributes = File.GetAttributes(world);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                    ProtectSessionLock(Path.Combine(world, "session.lock"));
                }
            }
            catch (InstallerException) { throw; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new InstallerException("GAME_USE_UNVERIFIED", LocalizedText.Get("GameUseUnverified"), ex);
            }
        }

        private void ProtectSessionLock(string path)
        {
            if (!IsActive) throw new InstallerException("GAME_USE_UNVERIFIED", LocalizedText.Get("GameUseUnverified"));
            var fullPath = Path.GetFullPath(path);
            if (_protectedPaths.Contains(fullPath) || ReadAttributes(fullPath) is null) return;
            try
            {
                SafePath.EnsureNoReparsePoints(Path.GetDirectoryName(fullPath)!, fullPath);
                _handles.Add(new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.None));
                _protectedPaths.Add(fullPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new InstallerException("INSTANCE_IN_USE", LocalizedText.Get("InstanceFileInUse"), ex);
            }
        }

        private static FileAttributes? ReadAttributes(string path)
        {
            try { return File.GetAttributes(path); }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new InstallerException("INSTANCE_IN_USE", LocalizedText.Get("InstanceFileInUse"), ex);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _active, 0) == 0) return;
            for (var index = _handles.Count - 1; index >= 0; index--) _handles[index].Dispose();
            _handles.Clear();
        }
    }

    private sealed record ProcessInspectionForTesting(
        IReadOnlyList<(int ProcessId, string ProcessName)> Candidates, Func<int, string> CommandLineReader);

    private sealed class ProcessInspectionOverride(
        ProcessInspectionForTesting installed, ProcessInspectionForTesting? previous) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _ = Interlocked.CompareExchange(ref _processInspectionForTesting, previous, installed);
        }
    }

    public sealed class Scope : IDisposable
    {
        private readonly Lease _lease;
        private readonly bool _ownsLease;
        private readonly Lease? _previousLease;
        private int _disposed;

        internal Scope(Lease lease, bool ownsLease, Lease? previousLease)
        {
            _lease = lease;
            _ownsLease = ownsLease;
            _previousLease = previousLease;
        }

        public void Recheck() => _lease.Recheck();

        public void ProtectManagedFile(string path) => _lease.ProtectManagedFile(path);

        internal void ReleaseManagedFile(string path) => _lease.ReleaseManagedFile(path);

        internal void ProtectCommittedFile(string path) => _lease.ProtectCommittedFile(path);

        internal string? HashManagedFile(string path) => _lease.HashManagedFile(path);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0 || !_ownsLease) return;
            _lease.Dispose();
            if (ReferenceEquals(CurrentLease.Value, _lease)) CurrentLease.Value = _previousLease;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetProcessId(SafeProcessHandle process);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(SafeProcessHandle process, int processInformationClass,
        IntPtr processInformation, uint processInformationLength, out uint returnLength);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW(string commandLine, out int argumentCount);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
