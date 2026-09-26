using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MinePack.Core;

public enum MinecraftLauncherKind
{
    Store,
    Win32
}

public sealed record MinecraftLauncherTarget(MinecraftLauncherKind Kind, string Identity);

public interface IMinecraftLauncherProcess : IDisposable
{
    bool HasExited { get; }
    bool RequestClose();
    Task WaitForExitAsync(CancellationToken cancellationToken);
}

public interface IMinecraftLauncherPlatform
{
    IReadOnlyList<MinecraftLauncherTarget> FindTargets();
    IReadOnlyList<IMinecraftLauncherProcess> FindRunningProcesses(MinecraftLauncherTarget target);
    bool HasUnidentifiedLauncherProcess();
    void Start(MinecraftLauncherTarget target);
}

public enum MinecraftLauncherStartStatus
{
    Requested,
    TargetUnavailable,
    Failed
}

public sealed record MinecraftLauncherStartResult(MinecraftLauncherStartStatus Status, string? Diagnostic = null);

public sealed class MinecraftLauncherController
{
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(20);
    private readonly IMinecraftLauncherPlatform _platform;
    private readonly TimeSpan _closeTimeout;

    public MinecraftLauncherController(IMinecraftLauncherPlatform? platform = null, TimeSpan? closeTimeout = null)
    {
        _platform = platform ?? CreateDefaultPlatform();
        _closeTimeout = closeTimeout ?? CloseTimeout;
    }

    private static IMinecraftLauncherPlatform CreateDefaultPlatform()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Minecraft Launcher integration requires Windows.");
        return new WindowsMinecraftLauncherPlatform();
    }

    public async Task<MinecraftLauncherTarget?> CloseBeforeInstallAsync(CancellationToken cancellationToken = default)
    {
        var targets = _platform.FindTargets().Distinct().ToArray();
        if (_platform.HasUnidentifiedLauncherProcess())
            throw new InstallerException("LAUNCHER_IDENTITY_UNKNOWN", LocalizedText.Get("LauncherIdentityUnknown"));

        var running = targets.Where(HasRunningProcesses).ToArray();
        if (running.Length > 1 || (running.Length == 0 && targets.Length > 1))
            throw new InstallerException("LAUNCHER_TARGET_AMBIGUOUS", LocalizedText.Get("LauncherTargetsAmbiguous"));

        var target = running.Length == 1 ? running[0] : targets.SingleOrDefault();
        if (target is null) return null;

        var discoveredProcesses = _platform.FindRunningProcesses(target);
        var processes = discoveredProcesses.Where(process => !process.HasExited).ToArray();
        foreach (var process in discoveredProcesses.Except(processes)) process.Dispose();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_closeTimeout);
        try
        {
            foreach (var process in processes)
                if (!process.HasExited)
                    _ = process.RequestClose();
            foreach (var process in processes)
                await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InstallerException("LAUNCHER_CLOSE_TIMEOUT", LocalizedText.Get("LauncherCloseTimeout"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InstallerException("LAUNCHER_CLOSE_FAILED", LocalizedText.Get("LauncherCloseFailed"), ex);
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }

        if (HasRunningProcesses(target))
            throw new InstallerException("LAUNCHER_CLOSE_TIMEOUT", LocalizedText.Get("LauncherCloseTimeout"));
        if (_platform.HasUnidentifiedLauncherProcess())
            throw new InstallerException("LAUNCHER_IDENTITY_UNKNOWN", LocalizedText.Get("LauncherIdentityUnknown"));
        return target;
    }

    public void EnsureClosed()
    {
        if (_platform.HasUnidentifiedLauncherProcess())
            throw new InstallerException("LAUNCHER_IDENTITY_UNKNOWN", LocalizedText.Get("LauncherIdentityUnknown"));

        foreach (var target in _platform.FindTargets().Distinct())
        {
            if (HasRunningProcesses(target))
                throw new InstallerException("LAUNCHER_RUNNING", LocalizedText.Get("LauncherRunning"));
        }
    }

    private bool HasRunningProcesses(MinecraftLauncherTarget target)
    {
        var processes = _platform.FindRunningProcesses(target);
        try { return processes.Any(process => !process.HasExited); }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    public async Task<MinecraftLauncherStartResult> ConfigureAndStartAsync(
        MinecraftLauncherTarget? target, Func<Task> configureProfile)
    {
        await configureProfile();
        if (target is null)
            return new MinecraftLauncherStartResult(MinecraftLauncherStartStatus.TargetUnavailable, "LAUNCHER_TARGET_UNAVAILABLE");

        try
        {
            _platform.Start(target);
            return new MinecraftLauncherStartResult(MinecraftLauncherStartStatus.Requested);
        }
        catch (Exception ex)
        {
            return new MinecraftLauncherStartResult(MinecraftLauncherStartStatus.Failed,
                $"LAUNCHER_START_FAILED ({ex.GetType().Name})");
        }
    }
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsMinecraftLauncherPlatform : IMinecraftLauncherPlatform
{
    private const string AppsFolderClsid = "shell:::{4234d49b-0245-4df3-b780-3893943456e1}";
    private const string MinecraftStorePackageFamily = "Microsoft.4297127D64EC6_8wekyb3d8bbwe";
    private static readonly string[] LauncherProcessNames = ["MinecraftLauncher", "Minecraft"];

    public IReadOnlyList<MinecraftLauncherTarget> FindTargets()
    {
        var targets = new HashSet<MinecraftLauncherTarget>();
        foreach (var app in ReadAppsFolder())
        {
            if (IsOfficialStoreApp(app.Path))
                targets.Add(new MinecraftLauncherTarget(MinecraftLauncherKind.Store, app.Path));
            else if (Path.IsPathFullyQualified(app.Path) && IsOfficialLauncherExecutable(app.Path))
                targets.Add(new MinecraftLauncherTarget(MinecraftLauncherKind.Win32, Path.GetFullPath(app.Path)));
        }

        foreach (var shortcut in FindStartMenuShortcuts())
        {
            var targetPath = ResolveShortcut(shortcut);
            if (targetPath is not null && IsOfficialLauncherExecutable(targetPath))
                targets.Add(new MinecraftLauncherTarget(MinecraftLauncherKind.Win32, Path.GetFullPath(targetPath)));
        }

        foreach (var name in LauncherProcessNames)
        foreach (var process in Process.GetProcessesByName(name))
        {
            using (process)
            {
                var path = TryGetProcessPath(process);
                if (path is not null && !IsInWindowsApps(path) && IsOfficialLauncherExecutable(path))
                    targets.Add(new MinecraftLauncherTarget(MinecraftLauncherKind.Win32, Path.GetFullPath(path)));
            }
        }
        return targets.ToArray();
    }

    public IReadOnlyList<IMinecraftLauncherProcess> FindRunningProcesses(MinecraftLauncherTarget target)
    {
        var result = new List<IMinecraftLauncherProcess>();
        foreach (var process in Process.GetProcesses())
        {
            var path = TryGetProcessPath(process);
            var matches = path is not null && (target.Kind switch
            {
                MinecraftLauncherKind.Win32 => PathEquals(path, target.Identity),
                MinecraftLauncherKind.Store => IsLauncherExecutablePath(path) && IsInPackageFamily(path, target.Identity),
                _ => false
            });
            if (matches) result.Add(new SystemMinecraftLauncherProcess(process));
            else process.Dispose();
        }
        return result;
    }

    public bool HasUnidentifiedLauncherProcess()
    {
        var storeTargets = FindTargets().Where(target => target.Kind == MinecraftLauncherKind.Store).ToArray();
        foreach (var name in LauncherProcessNames)
        foreach (var process in Process.GetProcessesByName(name))
        {
            using (process)
            {
                var path = TryGetProcessPath(process);
                if (path is null) return true;

                var inWindowsApps = IsInWindowsApps(path);
                var registeredStoreProcess = inWindowsApps && IsLauncherExecutablePath(path) &&
                    storeTargets.Any(target => IsInPackageFamily(path, target.Identity));
                if (registeredStoreProcess) continue;

                if (!inWindowsApps && IsOfficialLauncherExecutable(path)) continue;
                return true;
            }
        }
        return false;
    }

    public void Start(MinecraftLauncherTarget target)
    {
        ProcessStartInfo startInfo;
        if (target.Kind == MinecraftLauncherKind.Store)
        {
            var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            if (!File.Exists(explorer)) throw new FileNotFoundException("Windows Explorer was not found.");
            startInfo = new ProcessStartInfo(explorer, $"shell:AppsFolder\\{target.Identity}") { UseShellExecute = true };
        }
        else
        {
            if (!IsOfficialLauncherExecutable(target.Identity))
                throw new InvalidOperationException("The registered Launcher executable could not be verified.");
            startInfo = new ProcessStartInfo(target.Identity) { UseShellExecute = true };
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Windows did not accept the Launcher start request.");
    }

    private static IReadOnlyList<(string Name, string Path)> ReadAppsFolder()
    {
        object? shell = null;
        object? folder = null;
        object? items = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application")
                ?? throw new InvalidOperationException("Windows Shell is unavailable.");
            shell = Activator.CreateInstance(shellType) ?? throw new InvalidOperationException("Windows Shell could not be created.");
            folder = shellType.InvokeMember("NameSpace", BindingFlags.InvokeMethod, null, shell, [AppsFolderClsid]);
            if (folder is null) throw new InvalidOperationException("The registered AppsFolder is unavailable.");
            items = folder.GetType().InvokeMember("Items", BindingFlags.InvokeMethod, null, folder, null);
            if (items is null) throw new InvalidOperationException("The registered AppsFolder could not be read.");

            var result = new List<(string, string)>();
            foreach (var item in (System.Collections.IEnumerable)items)
            {
                try
                {
                    if (item is null) continue;
                    var itemType = item.GetType();
                    var name = itemType.InvokeMember("Name", BindingFlags.GetProperty, null, item, null) as string;
                    var path = itemType.InvokeMember("Path", BindingFlags.GetProperty, null, item, null) as string;
                    if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(path)) result.Add((name, path));
                }
                finally { ReleaseComObject(item); }
            }
            return result;
        }
        catch (Exception ex)
        {
            throw new InstallerException("LAUNCHER_DISCOVERY_FAILED", LocalizedText.Get("LauncherDiscoveryFailed"), ex);
        }
        finally
        {
            ReleaseComObject(items);
            ReleaseComObject(folder);
            ReleaseComObject(shell);
        }
    }

    private static IEnumerable<string> FindStartMenuShortcuts()
    {
        foreach (var menu in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                     Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
                 }.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            var programs = Path.Combine(menu, "Programs");
            if (!Directory.Exists(programs)) continue;
            IEnumerable<string> shortcuts;
            try { shortcuts = Directory.EnumerateFiles(programs, "*.lnk", SearchOption.AllDirectories).ToArray(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (var shortcut in shortcuts)
            {
                yield return shortcut;
            }
        }
    }

    private static string? ResolveShortcut(string shortcutPath)
    {
        object? shell = null;
        object? shortcut = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return null;
            shell = Activator.CreateInstance(shellType);
            if (shell is null) return null;
            shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, [shortcutPath]);
            if (shortcut is null) return null;
            var value = shortcut.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, shortcut, null) as string;
            return !string.IsNullOrWhiteSpace(value) && Path.IsPathFullyQualified(value) ? Path.GetFullPath(value) : null;
        }
        catch { return null; }
        finally
        {
            ReleaseComObject(shortcut);
            ReleaseComObject(shell);
        }
    }

    private static bool IsOfficialStoreApp(string aumid)
    {
        var separator = aumid.IndexOf('!');
        return separator > 0 &&
               string.Equals(aumid[..separator], MinecraftStorePackageFamily, StringComparison.OrdinalIgnoreCase) &&
               separator < aumid.Length - 1;
    }

    private static bool IsOfficialLauncherExecutable(string path)
    {
        if (!File.Exists(path) || !Path.IsPathFullyQualified(path)) return false;
        var fileName = Path.GetFileName(path);
        if (!fileName.Equals("MinecraftLauncher.exe", StringComparison.OrdinalIgnoreCase) &&
            !fileName.Equals("Minecraft.exe", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            var publisher = info.CompanyName ?? string.Empty;
            var product = info.ProductName ?? string.Empty;
            var originalName = info.OriginalFilename ?? string.Empty;
            if (!product.Contains("Minecraft", StringComparison.OrdinalIgnoreCase) ||
                (!product.Contains("Launcher", StringComparison.OrdinalIgnoreCase) &&
                 !originalName.Contains("Launcher", StringComparison.OrdinalIgnoreCase))) return false;

            if (!WinTrust.VerifyAuthenticodeSignature(path)) return false;
#pragma warning disable SYSLIB0057 // Extract the signed PE's signer certificate after WinVerifyTrust validates its signature.
            using var certificate = new System.Security.Cryptography.X509Certificates.X509Certificate2(
                System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            var signer = certificate.GetNameInfo(
                System.Security.Cryptography.X509Certificates.X509NameType.SimpleName, false);
            var trustedSigner = signer.Equals("Mojang AB", StringComparison.OrdinalIgnoreCase) ||
                                signer.Equals("Microsoft Corporation", StringComparison.OrdinalIgnoreCase);
            return trustedSigner &&
                   (publisher.Contains("Mojang", StringComparison.OrdinalIgnoreCase) ||
                    publisher.Contains("Microsoft", StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    private static bool IsInPackageFamily(string executablePath, string aumid)
    {
        var separator = aumid.IndexOf('!');
        if (separator <= 0) return false;
        var family = aumid[..separator];
        var split = family.LastIndexOf('_');
        if (split <= 0) return false;
        var packageName = family[..split];
        var publisherId = family[(split + 1)..];
        var current = Path.GetDirectoryName(Path.GetFullPath(executablePath));
        while (!string.IsNullOrEmpty(current))
        {
            var directory = Path.GetFileName(current);
            if (directory.StartsWith(packageName + "_", StringComparison.OrdinalIgnoreCase) &&
                directory.EndsWith("_" + publisherId, StringComparison.OrdinalIgnoreCase)) return true;
            current = Path.GetDirectoryName(current);
        }
        return false;
    }

    private static bool IsInWindowsApps(string path) =>
        path.Contains("\\WindowsApps\\", StringComparison.OrdinalIgnoreCase);

    private static bool IsLauncherExecutablePath(string path) =>
        Path.GetFileName(path).Equals("MinecraftLauncher.exe", StringComparison.OrdinalIgnoreCase) ||
        Path.GetFileName(path).Equals("Minecraft.exe", StringComparison.OrdinalIgnoreCase);

    private static bool PathEquals(string left, string right)
    {
        try { return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    private static string? TryGetProcessPath(Process process)
    {
        try { return process.MainModule?.FileName; }
        catch { return null; }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
    }

    private sealed class SystemMinecraftLauncherProcess(Process process) : IMinecraftLauncherProcess
    {
        public bool HasExited
        {
            get
            {
                try { return process.HasExited; }
                catch (InvalidOperationException) { return true; }
            }
        }

        public bool RequestClose() => process.CloseMainWindow();
        public Task WaitForExitAsync(CancellationToken cancellationToken) => process.WaitForExitAsync(cancellationToken);
        public void Dispose() => process.Dispose();
    }

    private static class WinTrust
    {
        private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
        private const uint UiChoiceNone = 2;
        private const uint RevocationChecksNone = 0;
        private const uint ChoiceFile = 1;
        private const uint StateActionVerify = 1;
        private const uint StateActionClose = 2;
        private const uint CacheOnlyUrlRetrieval = 0x1000;

        public static bool VerifyAuthenticodeSignature(string path)
        {
            var pathPointer = Marshal.StringToCoTaskMemUni(path);
            var fileInfoPointer = IntPtr.Zero;
            try
            {
                var fileInfo = new WinTrustFileInfo
                {
                    StructSize = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
                    FilePath = pathPointer
                };
                fileInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
                Marshal.StructureToPtr(fileInfo, fileInfoPointer, false);
                var data = new WinTrustData
                {
                    StructSize = (uint)Marshal.SizeOf<WinTrustData>(),
                    UiChoice = UiChoiceNone,
                    RevocationChecks = RevocationChecksNone,
                    UnionChoice = ChoiceFile,
                    FileInfo = fileInfoPointer,
                    StateAction = StateActionVerify,
                    ProviderFlags = CacheOnlyUrlRetrieval
                };

                var status = WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, ref data);
                data.StateAction = StateActionClose;
                _ = WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, ref data);
                return status == 0;
            }
            finally
            {
                if (fileInfoPointer != IntPtr.Zero)
                {
                    Marshal.DestroyStructure<WinTrustFileInfo>(fileInfoPointer);
                    Marshal.FreeHGlobal(fileInfoPointer);
                }
                Marshal.FreeCoTaskMem(pathPointer);
            }
        }

        [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true)]
        private static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid actionId,
            ref WinTrustData data);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WinTrustFileInfo
        {
            public uint StructSize;
            public IntPtr FilePath;
            public IntPtr FileHandle;
            public IntPtr KnownSubject;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WinTrustData
        {
            public uint StructSize;
            public IntPtr PolicyCallbackData;
            public IntPtr SipClientData;
            public uint UiChoice;
            public uint RevocationChecks;
            public uint UnionChoice;
            public IntPtr FileInfo;
            public uint StateAction;
            public IntPtr StateData;
            public IntPtr UrlReference;
            public uint ProviderFlags;
            public uint UiContext;
            public IntPtr SignatureSettings;
        }
    }
}
