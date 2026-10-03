using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32;

namespace MinePack.Core;

public enum LauncherDiscoveryState { Found, Absent, Unknown }
public enum LauncherDecisionKind { Automatic, ChooseLauncher, ChooseInstallation, LocateOrRetry, Refuse }

public sealed record LauncherDecision(LauncherDecisionKind Kind, LauncherKind? LauncherKind = null);
public sealed record PrismLauncherTarget(string ExecutablePath, string DataRoot, string InstancesRoot,
    string Fingerprint, string IdentityEvidence);
public sealed record PrismTargetHint(string ExecutablePath, string DataRoot, string InstancesRoot, string Fingerprint);
public sealed record LauncherScanResult(LauncherDiscoveryState State, IReadOnlyList<PrismLauncherTarget> PrismTargets,
    string? DiagnosticCode = null);

public static class LauncherDiscovery
{
    private const int MaxShortcutCount = 2048;
    private const int MaxRegistryEntries = 4096;
    private const int MaxConfigBytes = 64 * 1024;

    private sealed class PrismCandidate(string executablePath)
    {
        public string ExecutablePath { get; } = executablePath;
        public bool HasNamedShortcut { get; set; }
        public bool HasRegistration { get; set; }
        public bool HasVerifiedProcess { get; set; }
        public bool HasMetadataProof { get; set; }
        public List<(string[] Arguments, string? WorkingDirectory)> LaunchHints { get; } = [];
    }

    private sealed record ShortcutTarget(string Path, string[] Arguments, string? WorkingDirectory);

    public static LauncherScanResult DiscoverPrism(IEnumerable<PrismTargetHint>? savedHints = null)
    {
        if (!OperatingSystem.IsWindows())
            return new LauncherScanResult(LauncherDiscoveryState.Unknown, [], "PRISM_DISCOVERY_UNAVAILABLE");

        var candidates = new Dictionary<string, PrismCandidate>(StringComparer.OrdinalIgnoreCase);
        var unknown = false;
        var hasUnverifiedHint = false;
        string? diagnosticCode = null;
        try { AddStartMenuCandidates(candidates, ref unknown, ref diagnosticCode); }
        catch { unknown = true; diagnosticCode ??= "PRISM_SHORTCUT_DISCOVERY_FAILED"; }
        try { AddRegisteredCandidates(candidates, ref unknown, ref diagnosticCode); }
        catch { unknown = true; diagnosticCode ??= "PRISM_REGISTRY_DISCOVERY_FAILED"; }
        try { AddKnownPathCandidates(candidates); }
        catch { unknown = true; diagnosticCode ??= "PRISM_PATH_DISCOVERY_FAILED"; }
        try { AddRunningProcessCandidates(candidates, ref unknown, ref diagnosticCode); }
        catch { unknown = true; diagnosticCode ??= "PRISM_PROCESS_DISCOVERY_FAILED"; }

        var targets = new Dictionary<string, PrismLauncherTarget>(StringComparer.OrdinalIgnoreCase);
        foreach (var hint in savedHints?.Take(32) ?? [])
        {
            try
            {
                if (!File.Exists(hint.ExecutablePath)) continue;
                var target = new PrismLauncherTarget(hint.ExecutablePath, hint.DataRoot, hint.InstancesRoot,
                    hint.Fingerprint, "saved-hint");
                RevalidatePrismTarget(target);
                targets.TryAdd(TargetKey(target), target);
            }
            catch (InstallerException ex) { unknown = true; diagnosticCode ??= ex.Code; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            { unknown = true; diagnosticCode ??= "PRISM_SAVED_TARGET_UNKNOWN"; }
        }

        foreach (var candidate in candidates.Values)
        {
            try
            {
                var info = FileVersionInfo.GetVersionInfo(candidate.ExecutablePath);
                candidate.HasMetadataProof = IsPrismMetadata(info);
                var trustedPathEvidence = (candidate.HasNamedShortcut || candidate.HasRegistration || candidate.HasVerifiedProcess) &&
                    HasExpectedExecutableLayout(candidate.ExecutablePath);
                if (!candidate.HasMetadataProof && !trustedPathEvidence)
                {
                    hasUnverifiedHint = true;
                        diagnosticCode ??= "PRISM_IDENTITY_UNKNOWN";
                    continue;
                }

                var hints = candidate.LaunchHints.Count == 0
                    ? [(Array.Empty<string>(), (string?)null)]
                    : candidate.LaunchHints.DistinctBy(hint => string.Join('\0', hint.Arguments) + "\n" + hint.WorkingDirectory,
                        StringComparer.OrdinalIgnoreCase).ToList();
                foreach (var (arguments, workingDirectory) in hints)
                {
                    var dataRoot = ResolveDataRoot(candidate.ExecutablePath, arguments,
                        workingDirectory, Environment.GetEnvironmentVariable("PRISMLAUNCHER_DATA_DIR"));
                    var setting = ReadInstanceDirectorySetting(dataRoot);
                    var instancesRoot = ResolveInstancesRootFromSetting(dataRoot, setting);
                    var fingerprint = InstallationLayout.ComputePrismFingerprint(dataRoot, instancesRoot);
                    var identityEvidence = candidate.HasMetadataProof ? "binary-metadata" :
                        candidate.HasNamedShortcut ? "start-menu-shortcut" :
                        candidate.HasRegistration ? "installation-registration" : "running-process";
                    var target = new PrismLauncherTarget(FullPath(candidate.ExecutablePath), dataRoot,
                        instancesRoot, fingerprint, identityEvidence);
                    targets.TryAdd(TargetKey(target), target);
                }
            }
            catch (InstallerException ex) { unknown = true; diagnosticCode ??= ex.Code; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            { unknown = true; diagnosticCode ??= "PRISM_ACCESS_OR_PATH_UNKNOWN"; }
        }

        return CreatePrismScanResult(unknown, hasUnverifiedHint, targets.Values, diagnosticCode);
    }

    public static LauncherScanResult LocatePrismExecutable(string executablePath, string? dataRootOverride = null)
    {
        try
        {
            var executable = FullPath(executablePath);
            SafePath.EnsureNoReparsePoints(Path.GetDirectoryName(executable)!, executable);
            var info = FileVersionInfo.GetVersionInfo(executable);
            if (!File.Exists(executable) || !Path.GetFileName(executable).Equals("prismlauncher.exe", StringComparison.OrdinalIgnoreCase) ||
                (!IsPrismMetadata(info) && !HasExpectedExecutableLayout(executable)))
                return new LauncherScanResult(LauncherDiscoveryState.Unknown, [], "PRISM_IDENTITY_UNKNOWN");

            var dataRoot = string.IsNullOrWhiteSpace(dataRootOverride)
                ? ResolveDataRoot(executable, environmentDataRoot: Environment.GetEnvironmentVariable("PRISMLAUNCHER_DATA_DIR"))
                : FullPath(dataRootOverride);
            var setting = ReadInstanceDirectorySetting(dataRoot);
            var instancesRoot = ResolveInstancesRootFromSetting(dataRoot, setting);
            var target = new PrismLauncherTarget(executable, dataRoot, instancesRoot,
                InstallationLayout.ComputePrismFingerprint(dataRoot, instancesRoot), "located-executable");
            RevalidatePrismTarget(target);
            return new LauncherScanResult(LauncherDiscoveryState.Found, [target]);
        }
        catch (InstallerException ex)
        {
            return new LauncherScanResult(LauncherDiscoveryState.Unknown, [], ex.Code);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new LauncherScanResult(LauncherDiscoveryState.Unknown, [], "PRISM_LOCATE_UNKNOWN");
        }
    }

    public static void RevalidatePrismTarget(PrismLauncherTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var executable = FullPath(target.ExecutablePath);
        SafePath.EnsureNoReparsePoints(Path.GetDirectoryName(executable)!, executable);
        var attributes = File.GetAttributes(executable);
        if ((attributes & FileAttributes.Directory) != 0 ||
            !Path.GetFileName(executable).Equals("prismlauncher.exe", StringComparison.OrdinalIgnoreCase))
            throw new InstallerException("PRISM_IDENTITY_CHANGED", LocalizedText.Get("PrismIdentityChanged"));

        var metadata = IsPrismMetadata(FileVersionInfo.GetVersionInfo(executable));
        if (!metadata && !HasExpectedExecutableLayout(executable))
            throw new InstallerException("PRISM_IDENTITY_CHANGED", LocalizedText.Get("PrismIdentityChanged"));

        var dataRoot = FullPath(target.DataRoot);
        var setting = ReadInstanceDirectorySetting(dataRoot);
        var instancesRoot = ResolveInstancesRootFromSetting(dataRoot, setting);
        var fingerprint = InstallationLayout.ComputePrismFingerprint(dataRoot, instancesRoot);
        if (!executable.Equals(FullPath(target.ExecutablePath), StringComparison.OrdinalIgnoreCase) ||
            !dataRoot.Equals(FullPath(target.DataRoot), StringComparison.OrdinalIgnoreCase) ||
            !instancesRoot.Equals(FullPath(target.InstancesRoot), StringComparison.OrdinalIgnoreCase) ||
            !fingerprint.Equals(target.Fingerprint, StringComparison.OrdinalIgnoreCase))
            throw new InstallerException("PRISM_IDENTITY_CHANGED", LocalizedText.Get("PrismIdentityChanged"));
    }

    internal static LauncherScanResult CreatePrismScanResult(bool hasUnknown, bool hasUnverifiedCandidate,
        IEnumerable<PrismLauncherTarget> targets, string? diagnosticCode)
    {
        var uniqueTargets = DeduplicateTargets(targets);
        if (hasUnknown || hasUnverifiedCandidate)
            return new LauncherScanResult(LauncherDiscoveryState.Unknown, uniqueTargets,
                diagnosticCode ?? "PRISM_DISCOVERY_UNKNOWN");
        return uniqueTargets.Count > 0
            ? new LauncherScanResult(LauncherDiscoveryState.Found, uniqueTargets)
            : new LauncherScanResult(LauncherDiscoveryState.Absent, []);
    }

    [SupportedOSPlatform("windows")]
    private static void AddStartMenuCandidates(Dictionary<string, PrismCandidate> candidates, ref bool unknown, ref string? diagnosticCode)
    {
        var count = 0;
        foreach (var menu in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                     Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
                 }.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            var programs = Path.Combine(menu, "Programs");
            var attributes = GetOptionalAttributes(programs);
            if (attributes is null || (attributes.Value & FileAttributes.Directory) == 0) continue;
            SafePath.EnsureNoReparsePoints(programs, programs);
            var pending = new Stack<string>();
            pending.Push(programs);
            while (pending.Count > 0)
            {
                if (++count > MaxShortcutCount)
                {
                    unknown = true;
                    diagnosticCode ??= "PRISM_SCAN_LIMIT";
                    return;
                }
                var directory = pending.Pop();
                try
                {
                    foreach (var child in Directory.EnumerateDirectories(directory))
                    {
                        var childAttributes = GetOptionalAttributes(child);
                        if (childAttributes is null || (childAttributes.Value & FileAttributes.ReparsePoint) != 0) continue;
                        pending.Push(child);
                    }
                    foreach (var shortcutPath in Directory.EnumerateFiles(directory, "*.lnk", SearchOption.TopDirectoryOnly))
                    {
                        if (++count > MaxShortcutCount)
                        {
                            unknown = true;
                            diagnosticCode ??= "PRISM_SCAN_LIMIT";
                            return;
                        }
                        var shortcutName = Path.GetFileNameWithoutExtension(shortcutPath);
                        if (!IsPrismName(shortcutName)) continue;
                        var target = ReadShortcut(shortcutPath);
                        if (target is null || !Path.IsPathFullyQualified(target.Path))
                        {
                            unknown = true;
                            diagnosticCode ??= "PRISM_SHORTCUT_TARGET_UNKNOWN";
                            continue;
                        }
                        AddCandidate(candidates, target.Path, candidate =>
                        {
                            candidate.HasNamedShortcut = true;
                            candidate.LaunchHints.Add((target.Arguments, target.WorkingDirectory));
                        });
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                { unknown = true; diagnosticCode ??= "PRISM_SHORTCUT_ACCESS_UNKNOWN"; }
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static void AddRegisteredCandidates(Dictionary<string, PrismCandidate> candidates, ref bool unknown, ref string? diagnosticCode)
    {
        var scanned = 0;
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                foreach (var subKeyPath in new[]
                         {
                             @"Software\Microsoft\Windows\CurrentVersion\Uninstall",
                             @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
                         })
                {
                    using var uninstall = baseKey.OpenSubKey(subKeyPath, writable: false);
                    if (uninstall is null) continue;
                    foreach (var name in uninstall.GetSubKeyNames())
                    {
                        if (++scanned > MaxRegistryEntries)
                        {
                            unknown = true;
                            diagnosticCode ??= "PRISM_SCAN_LIMIT";
                            return;
                        }
                        using var entry = uninstall.OpenSubKey(name, writable: false);
                        if (entry is null) continue;
                        var displayName = Convert.ToString(entry.GetValue("DisplayName"));
                        if (!IsPrismName(displayName)) continue;
                        var installLocation = Convert.ToString(entry.GetValue("InstallLocation"));
                        var displayIcon = Convert.ToString(entry.GetValue("DisplayIcon"));
                        var executable = ParseRegisteredExecutable(displayIcon) ??
                            (!string.IsNullOrWhiteSpace(installLocation) && Path.IsPathFullyQualified(installLocation)
                                ? Path.Combine(installLocation, "prismlauncher.exe") : null);
                        if (executable is null)
                        {
                            unknown = true;
                            diagnosticCode ??= "PRISM_REGISTRATION_TARGET_UNKNOWN";
                            continue;
                        }
                        if (!string.IsNullOrWhiteSpace(installLocation) && Path.IsPathFullyQualified(installLocation) &&
                            !IsWithin(installLocation, executable))
                        {
                            unknown = true;
                            diagnosticCode ??= "PRISM_REGISTRATION_PATH_MISMATCH";
                            continue;
                        }
                        AddCandidate(candidates, executable, candidate =>
                        {
                            candidate.HasRegistration = true;
                            candidate.LaunchHints.Add(([], null));
                        });
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            { unknown = true; diagnosticCode ??= "PRISM_REGISTRY_ACCESS_UNKNOWN"; }
        }
    }

    private static void AddKnownPathCandidates(Dictionary<string, PrismCandidate> candidates)
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        };
        foreach (var root in roots.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            var executable = Path.Combine(root, "Programs", "PrismLauncher", "prismlauncher.exe");
            if (root.Equals(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), StringComparison.OrdinalIgnoreCase) ||
                root.Equals(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), StringComparison.OrdinalIgnoreCase))
                executable = Path.Combine(root, "PrismLauncher", "prismlauncher.exe");
            var attributes = GetOptionalAttributes(executable);
            if (attributes is null) continue;
            AddCandidate(candidates, executable, candidate => candidate.LaunchHints.Add(([], null)));
        }
    }

    private static void AddRunningProcessCandidates(Dictionary<string, PrismCandidate> candidates, ref bool unknown, ref string? diagnosticCode)
    {
        foreach (var process in Process.GetProcessesByName("prismlauncher"))
        {
            using (process)
            {
                string? executable;
                try { executable = process.MainModule?.FileName; }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
                { unknown = true; diagnosticCode ??= "PRISM_PROCESS_PATH_UNKNOWN"; continue; }
                if (string.IsNullOrWhiteSpace(executable) || !Path.IsPathFullyQualified(executable))
                {
                    unknown = true;
                    diagnosticCode ??= "PRISM_PROCESS_PATH_UNKNOWN";
                    continue;
                }
                if (process.HasExited) continue;
                string[] arguments;
                try
                {
                    var commandLine = InstanceUseGuard.ReadProcessCommandLine(process.Id);
                    try { arguments = InstanceUseGuard.ParseCommandLine(commandLine); }
                    finally { commandLine = string.Empty; }
                }
                catch (Exception) when (HasExited(process.Id)) { continue; }
                catch
                {
                    unknown = true;
                    diagnosticCode ??= "PRISM_PROCESS_COMMANDLINE_UNKNOWN";
                    continue;
                }
                if (arguments.Length == 0 || !Path.IsPathFullyQualified(arguments[0]) ||
                    !FullPath(arguments[0]).Equals(FullPath(executable), StringComparison.OrdinalIgnoreCase))
                {
                    unknown = true;
                    diagnosticCode ??= "PRISM_PROCESS_IDENTITY_CHANGED";
                    continue;
                }
                try
                {
                    var dataRootArguments = ExtractDataRootArguments(arguments.Skip(1));
                    AddCandidate(candidates, executable,
                        candidate =>
                        {
                            candidate.HasVerifiedProcess = true;
                            candidate.LaunchHints.Add((dataRootArguments, null));
                        });
                }
                catch (InstallerException ex) { unknown = true; diagnosticCode ??= ex.Code; }
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
    }

    [SupportedOSPlatform("windows")]
    private static ShortcutTarget? ReadShortcut(string shortcutPath)
    {
        object? shell = null;
        object? shortcut = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell")
                ?? throw new InvalidOperationException("Windows shortcut shell is unavailable.");
            shell = Activator.CreateInstance(shellType)
                ?? throw new InvalidOperationException("Windows shortcut shell could not be created.");
            shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, [shortcutPath]);
            if (shortcut is null) throw new InvalidOperationException("Windows shortcut could not be read.");
            var type = shortcut.GetType();
            var path = type.InvokeMember("TargetPath", BindingFlags.GetProperty, null, shortcut, null) as string;
            if (string.IsNullOrWhiteSpace(path)) return null;
            var argumentText = type.InvokeMember("Arguments", BindingFlags.GetProperty, null, shortcut, null) as string ?? string.Empty;
            var workingDirectory = type.InvokeMember("WorkingDirectory", BindingFlags.GetProperty, null, shortcut, null) as string;
            return new ShortcutTarget(path, ExtractDataRootArguments(
                InstanceUseGuard.ParseCommandLine($"\"{path}\" {argumentText}").Skip(1)), workingDirectory);
        }
        finally
        {
            ReleaseComObject(shortcut);
            ReleaseComObject(shell);
        }
    }

    private static string[] ExtractDataRootArguments(IEnumerable<string> tokens)
    {
        var arguments = tokens.ToArray();
        var result = new List<string>();
        for (var i = 0; i < arguments.Length; i++)
        {
            if (arguments[i] is "--dir" or "-d")
            {
                result.Add(arguments[i]);
                if (++i >= arguments.Length) throw new InstallerException("PRISM_DATA_ROOT_UNKNOWN", LocalizedText.Get("PrismDataRootUnknown"));
                result.Add(arguments[i]);
            }
            else if (arguments[i].StartsWith("--dir=", StringComparison.Ordinal))
            {
                result.Add("--dir");
                result.Add(arguments[i][6..]);
            }
            else if (arguments[i].StartsWith("-d", StringComparison.Ordinal))
                throw new InstallerException("PRISM_DATA_ROOT_UNKNOWN", LocalizedText.Get("PrismDataRootUnknown"));
        }
        return result.ToArray();
    }

    private static void AddCandidate(Dictionary<string, PrismCandidate> candidates, string path,
        Action<PrismCandidate> update)
    {
        if (!Path.IsPathFullyQualified(path)) throw new InstallerException("PRISM_DISCOVERY_UNKNOWN", LocalizedText.Get("PrismIdentityChanged"));
        var fullPath = FullPath(path);
        var attributes = File.GetAttributes(fullPath);
        if ((attributes & FileAttributes.Directory) != 0 || (attributes & FileAttributes.ReparsePoint) != 0 ||
            !Path.GetFileName(fullPath).Equals("prismlauncher.exe", StringComparison.OrdinalIgnoreCase))
            throw new InstallerException("PRISM_DISCOVERY_UNKNOWN", LocalizedText.Get("PrismIdentityChanged"));
        SafePath.EnsureNoReparsePoints(Path.GetDirectoryName(fullPath)!, fullPath);
        if (!candidates.TryGetValue(fullPath, out var candidate))
            candidates.Add(fullPath, candidate = new PrismCandidate(fullPath));
        update(candidate);
    }

    private static string? ParseRegisteredExecutable(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var input = value.Trim();
        string candidate;
        if (input[0] == '"')
        {
            var closing = input.IndexOf('"', 1);
            if (closing < 0) return null;
            candidate = input[1..closing];
        }
        else
        {
            var exeEnd = input.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exeEnd < 0) return null;
            candidate = input[..(exeEnd + 4)];
        }
        return Path.IsPathFullyQualified(candidate) ? candidate : null;
    }

    private static bool IsPrismMetadata(FileVersionInfo info)
    {
        var product = info.ProductName ?? string.Empty;
        var description = info.FileDescription ?? string.Empty;
        var company = info.CompanyName ?? string.Empty;
        var original = info.OriginalFilename ?? string.Empty;
        return (IsPrismName(product) || IsPrismName(description)) ||
               (original.Equals("prismlauncher.exe", StringComparison.OrdinalIgnoreCase) &&
                company.Contains("Prism", StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasExpectedExecutableLayout(string path) =>
        Path.GetFileName(path).Equals("prismlauncher.exe", StringComparison.OrdinalIgnoreCase) &&
        IsPrismName(Path.GetFileName(Path.GetDirectoryName(path)));

    private static bool IsPrismName(string? value) =>
        value?.Contains("Prism", StringComparison.OrdinalIgnoreCase) == true &&
        value.Contains("Launcher", StringComparison.OrdinalIgnoreCase);

    private static bool IsWithin(string root, string path)
    {
        try
        {
            var fullRoot = Path.TrimEndingDirectorySeparator(FullPath(root));
            var fullPath = FullPath(path);
            return fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase) ||
                   fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    [SupportedOSPlatform("windows")]
    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
    }

    public static LauncherDecision Decide(LauncherDiscoveryState official, int officialTargets,
        LauncherDiscoveryState prism, int prismTargets)
    {
        if (official == LauncherDiscoveryState.Unknown || prism == LauncherDiscoveryState.Unknown)
            return new LauncherDecision(LauncherDecisionKind.LocateOrRetry);
        if (official == LauncherDiscoveryState.Absent && prism == LauncherDiscoveryState.Absent)
            return new LauncherDecision(LauncherDecisionKind.LocateOrRetry);
        if (official == LauncherDiscoveryState.Found && prism == LauncherDiscoveryState.Found)
            return new LauncherDecision(LauncherDecisionKind.ChooseLauncher);
        var kind = official == LauncherDiscoveryState.Found ? LauncherKind.Official : LauncherKind.Prism;
        var count = kind == LauncherKind.Official ? officialTargets : prismTargets;
        if (count <= 0) return new LauncherDecision(LauncherDecisionKind.LocateOrRetry);
        return count == 1
            ? new LauncherDecision(LauncherDecisionKind.Automatic, kind)
            : new LauncherDecision(LauncherDecisionKind.ChooseInstallation, kind);
    }

    internal static IReadOnlyList<PrismLauncherTarget> DeduplicateTargets(IEnumerable<PrismLauncherTarget> targets) =>
        targets.GroupBy(TargetKey, StringComparer.OrdinalIgnoreCase).Select(group => group.First()).ToArray();

    private static string TargetKey(PrismLauncherTarget target) =>
        FullPath(target.ExecutablePath).ToUpperInvariant() + "\n" + target.Fingerprint.ToLowerInvariant();

    public static string ResolveInstancesRoot(string dataRoot, string? configuration) =>
        ResolveInstancesRootFromSetting(dataRoot, ParseInstanceDirectory(configuration));

    public static string ResolveInstancesRootFromSetting(string dataRoot, string? configured)
    {
        var data = FullPath(dataRoot);
        string path;
        if (configured is null) path = FullPath(Path.Combine(data, "instances"));
        else if (Path.IsPathFullyQualified(configured)) path = FullPath(configured);
        else if (Path.IsPathRooted(configured))
            throw new InstallerException("PRISM_CONFIG_UNKNOWN", LocalizedText.Get("PrismConfigurationUnknown"));
        else path = FullPath(Path.Combine(data, configured));
        if (string.Equals(Path.TrimEndingDirectorySeparator(Path.GetPathRoot(path) ?? string.Empty),
                Path.TrimEndingDirectorySeparator(path), StringComparison.OrdinalIgnoreCase))
            throw new InstallerException("PRISM_CONFIG_UNKNOWN", LocalizedText.Get("PrismConfigurationUnknown"));
        SafePath.EnsureNoReparsePoints(path, path);
        return path;
    }

    public static string? ParseInstanceDirectory(string? configuration)
    {
        if (configuration is null) return null;
        if (Encoding.UTF8.GetByteCount(configuration) > MaxConfigBytes)
            throw new InstallerException("PRISM_CONFIG_UNKNOWN", LocalizedText.Get("PrismConfigurationUnknown"));

        var inGeneral = false;
        var found = false;
        string? value = null;
        var lines = configuration.Split('\n');
        if (lines.Length > 4096)
            throw new InstallerException("PRISM_CONFIG_UNKNOWN", LocalizedText.Get("PrismConfigurationUnknown"));
        foreach (var raw in lines)
        {
            var line = raw.Trim().TrimEnd('\r');
            if (line.Length == 0 || line[0] is '#' or ';') continue;
            if (line[0] == '[')
            {
                if (line[^1] != ']')
                    throw new InstallerException("PRISM_CONFIG_UNKNOWN", LocalizedText.Get("PrismConfigurationUnknown"));
                inGeneral = line[1..^1].Trim().Equals("General", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inGeneral) continue;
            var equals = line.IndexOf('=');
            if (equals <= 0)
                throw new InstallerException("PRISM_CONFIG_UNKNOWN", LocalizedText.Get("PrismConfigurationUnknown"));
            if (!line[..equals].Trim().Equals("InstanceDir", StringComparison.OrdinalIgnoreCase)) continue;
            if (found) throw new InstallerException("PRISM_CONFIG_UNKNOWN", LocalizedText.Get("PrismConfigurationUnknown"));
            found = true;
            value = DecodeIniValue(line[(equals + 1)..].Trim());
            if (string.IsNullOrWhiteSpace(value) || value.IndexOfAny(Path.GetInvalidPathChars()) >= 0 ||
                value.StartsWith("\\\\.\\", StringComparison.Ordinal) || value.StartsWith("\\\\?\\", StringComparison.Ordinal))
                throw new InstallerException("PRISM_CONFIG_UNKNOWN", LocalizedText.Get("PrismConfigurationUnknown"));
        }
        return value;
    }

    public static string ResolveDataRoot(string executablePath, IEnumerable<string>? arguments = null,
        string? workingDirectory = null, string? environmentDataRoot = null)
    {
        var exe = FullPath(executablePath);
        SafePath.EnsureNoReparsePoints(Path.GetDirectoryName(exe)!, exe);
        var args = arguments?.ToArray() ?? [];
        string? commandLineRoot = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is "--dir" or "-d")
            {
                if (commandLineRoot is not null || i + 1 >= args.Length || args[i + 1].StartsWith("-", StringComparison.Ordinal))
                    throw new InstallerException("PRISM_DATA_ROOT_UNKNOWN", LocalizedText.Get("PrismDataRootUnknown"));
                commandLineRoot = args[++i];
            }
            else if (args[i].StartsWith("--dir=", StringComparison.Ordinal))
            {
                if (commandLineRoot is not null)
                    throw new InstallerException("PRISM_DATA_ROOT_UNKNOWN", LocalizedText.Get("PrismDataRootUnknown"));
                commandLineRoot = args[i][6..];
            }
            else if (args[i].StartsWith("-d", StringComparison.Ordinal))
                throw new InstallerException("PRISM_DATA_ROOT_UNKNOWN", LocalizedText.Get("PrismDataRootUnknown"));
        }
        if (commandLineRoot is not null)
            return ResolveRelative(commandLineRoot, workingDirectory);
        if (!string.IsNullOrWhiteSpace(environmentDataRoot)) return FullPath(environmentDataRoot);

        var executableDirectory = Path.GetDirectoryName(exe)!;
        var userData = Path.Combine(executableDirectory, "UserData");
        if (GetOptionalAttributes(userData) is { } userDataAttributes &&
            (userDataAttributes & FileAttributes.Directory) != 0)
        {
            SafePath.EnsureNoReparsePoints(executableDirectory, userData);
            return FullPath(userData);
        }
        var portableMarker = Path.Combine(executableDirectory, "portable.txt");
        if (GetOptionalAttributes(portableMarker) is not null)
        {
            SafePath.EnsureNoReparsePoints(executableDirectory, portableMarker);
            return FullPath(executableDirectory);
        }
        return FullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrismLauncher"));
    }

    internal static bool TryMatchPrismProcess(PrismLauncherTarget target, string processExecutablePath,
        IReadOnlyList<string> processArguments, out bool matchesTarget)
    {
        matchesTarget = false;
        try
        {
            var executable = FullPath(processExecutablePath);
            if (!executable.Equals(FullPath(target.ExecutablePath), StringComparison.OrdinalIgnoreCase) ||
                processArguments.Count < 1 || !Path.IsPathFullyQualified(processArguments[0]) ||
                !FullPath(processArguments[0]).Equals(executable, StringComparison.OrdinalIgnoreCase))
                return false;

            var dataRootArguments = ExtractDataRootArguments(processArguments.Skip(1));
            if (dataRootArguments.Length == 0) return false;
            var processDataRoot = ResolveDataRoot(executable, dataRootArguments);
            matchesTarget = processDataRoot.Equals(FullPath(target.DataRoot), StringComparison.OrdinalIgnoreCase);
            return true;
        }
        catch (Exception ex) when (ex is InstallerException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    internal static bool TryReadVerifiedPrismProcessDataRoot(string processExecutablePath,
        IReadOnlyList<string> processArguments, out string dataRoot)
    {
        dataRoot = "";
        try
        {
            var executable = FullPath(processExecutablePath);
            if (!Path.GetFileName(executable).Equals("prismlauncher.exe", StringComparison.OrdinalIgnoreCase) ||
                processArguments.Count < 1 || !Path.IsPathFullyQualified(processArguments[0]) ||
                !FullPath(processArguments[0]).Equals(executable, StringComparison.OrdinalIgnoreCase))
                return false;

            var metadata = IsPrismMetadata(FileVersionInfo.GetVersionInfo(executable));
            if (!metadata && !HasExpectedExecutableLayout(executable)) return false;
            var rootArguments = ExtractDataRootArguments(processArguments.Skip(1));
            if (rootArguments.Length != 2 || rootArguments[0] != "--dir" ||
                !Path.IsPathFullyQualified(rootArguments[1])) return false;
            dataRoot = ResolveDataRoot(executable, rootArguments);
            return true;
        }
        catch (Exception ex) when (ex is InstallerException or ArgumentException or IOException or
                                      UnauthorizedAccessException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    public static string? ReadInstanceDirectorySetting(string dataRoot)
    {
        var config = Path.Combine(FullPath(dataRoot), "prismlauncher.cfg");
        SafePath.EnsureNoReparsePoints(FullPath(dataRoot), config);
        try
        {
            using var stream = new FileStream(config, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaxConfigBytes)
                throw new InstallerException("PRISM_CONFIG_UNKNOWN", LocalizedText.Get("PrismConfigurationUnknown"));
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
            return ParseInstanceDirectory(reader.ReadToEnd());
        }
        catch (InstallerException) { throw; }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            throw new InstallerException("PRISM_CONFIG_UNKNOWN", LocalizedText.Get("PrismConfigurationUnknown"), ex);
        }
    }

    private static string DecodeIniValue(string value)
    {
        if (value.Length == 0 || value[0] != '"') return value.Trim();
        var builder = new StringBuilder(value.Length);
        var closed = false;
        for (var i = 1; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '"')
            {
                if (!string.IsNullOrWhiteSpace(value[(i + 1)..]))
                    throw new InstallerException("PRISM_CONFIG_UNKNOWN", LocalizedText.Get("PrismConfigurationUnknown"));
                closed = true;
                break;
            }
            if (c == '\\' && i + 1 < value.Length && value[i + 1] is '"' or '\\') c = value[++i];
            builder.Append(c);
        }
        if (!closed) throw new InstallerException("PRISM_CONFIG_UNKNOWN", LocalizedText.Get("PrismConfigurationUnknown"));
        return builder.ToString();
    }

    private static string ResolveRelative(string path, string? workingDirectory)
    {
        if (Path.IsPathFullyQualified(path)) return FullPath(path);
        if (Path.IsPathRooted(path))
            throw new InstallerException("PRISM_DATA_ROOT_UNKNOWN", LocalizedText.Get("PrismDataRootUnknown"));
        if (string.IsNullOrWhiteSpace(workingDirectory) || !Path.IsPathFullyQualified(workingDirectory))
            throw new InstallerException("PRISM_DATA_ROOT_UNKNOWN", LocalizedText.Get("PrismDataRootUnknown"));
        return FullPath(Path.Combine(workingDirectory, path));
    }

    private static string FullPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new InstallerException("PRISM_DATA_ROOT_UNKNOWN", LocalizedText.Get("PrismDataRootUnknown"));
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        { throw new InstallerException("PRISM_DATA_ROOT_UNKNOWN", LocalizedText.Get("PrismDataRootUnknown"), ex); }
    }

    private static FileAttributes? GetOptionalAttributes(string path)
    {
        try { return File.GetAttributes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InstallerException("PRISM_DATA_ROOT_UNKNOWN", LocalizedText.Get("PrismDataRootUnknown"), ex);
        }
    }
}

public interface IPrismLauncherPlatform
{
    IReadOnlyList<IMinecraftLauncherProcess> FindRunningProcesses(PrismLauncherTarget target, out bool unidentified);
    void Start(PrismLauncherTarget target);
}

public sealed class PrismLauncherController
{
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(20);
    private readonly IPrismLauncherPlatform _platform;

    public PrismLauncherController(IPrismLauncherPlatform? platform = null) =>
        _platform = platform ?? new WindowsPrismLauncherPlatform();

    public async Task CloseBeforeInstallAsync(PrismLauncherTarget target, CancellationToken cancellationToken = default)
    {
        LauncherDiscovery.RevalidatePrismTarget(target);
        var processes = GetSelectedProcesses(target);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CloseTimeout);
        try
        {
            foreach (var process in processes)
                if (!process.HasExited) _ = process.RequestClose();
            foreach (var process in processes)
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InstallerException("LAUNCHER_CLOSE_TIMEOUT", LocalizedText.Get("PrismLauncherCloseTimeout"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InstallerException("LAUNCHER_CLOSE_FAILED", LocalizedText.Get("PrismLauncherCloseFailed"), ex);
        }
        finally { foreach (var process in processes) process.Dispose(); }

        LauncherDiscovery.RevalidatePrismTarget(target);
        var remainingProcesses = GetSelectedProcesses(target);
        var stillRunning = false;
        try
        {
            foreach (var process in remainingProcesses)
                if (!process.HasExited) stillRunning = true;
        }
        finally { foreach (var process in remainingProcesses) process.Dispose(); }
        if (stillRunning)
            throw new InstallerException("LAUNCHER_CLOSE_TIMEOUT", LocalizedText.Get("PrismLauncherCloseTimeout"));
    }

    public void EnsureClosed(PrismLauncherTarget target)
    {
        LauncherDiscovery.RevalidatePrismTarget(target);
        var processes = GetSelectedProcesses(target);
        try
        {
            if (processes.Any(process => !process.HasExited))
                throw new InstallerException("LAUNCHER_RUNNING", LocalizedText.Get("PrismLauncherRunning"));
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    public void Start(PrismLauncherTarget target)
    {
        LauncherDiscovery.RevalidatePrismTarget(target);
        _platform.Start(target);
    }

    private IMinecraftLauncherProcess[] GetSelectedProcesses(PrismLauncherTarget target)
    {
        var processes = _platform.FindRunningProcesses(target, out var unidentified).ToArray();
        if (unidentified)
        {
            foreach (var process in processes) process.Dispose();
            throw new InstallerException("LAUNCHER_IDENTITY_UNKNOWN", LocalizedText.Get("PrismLauncherIdentityUnknown"));
        }
        return processes;
    }
}

internal sealed class WindowsPrismLauncherPlatform : IPrismLauncherPlatform
{
    public IReadOnlyList<IMinecraftLauncherProcess> FindRunningProcesses(PrismLauncherTarget target, out bool unidentified)
    {
        unidentified = false;
        var processes = new List<IMinecraftLauncherProcess>();
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(target.ExecutablePath)))
        {
            try
            {
                if (process.HasExited) { process.Dispose(); continue; }
                var path = process.MainModule?.FileName;
                if (path is null)
                {
                    unidentified = true;
                    process.Dispose();
                    continue;
                }
                string[] arguments;
                try
                {
                    var commandLine = InstanceUseGuard.ReadProcessCommandLine(process.Id);
                    try { arguments = InstanceUseGuard.ParseCommandLine(commandLine); }
                    finally { commandLine = string.Empty; }
                }
                catch (Exception) when (process.HasExited) { process.Dispose(); continue; }
                catch
                {
                    unidentified = true;
                    process.Dispose();
                    continue;
                }
                if (!LauncherDiscovery.TryReadVerifiedPrismProcessDataRoot(path, arguments, out var processDataRoot))
                {
                    unidentified = true;
                    process.Dispose();
                    continue;
                }
                var sameDataRoot = processDataRoot.Equals(Path.GetFullPath(target.DataRoot), StringComparison.OrdinalIgnoreCase);
                var sameExecutable = Path.GetFullPath(path).Equals(Path.GetFullPath(target.ExecutablePath), StringComparison.OrdinalIgnoreCase);
                if (!sameDataRoot) process.Dispose();
                else if (sameExecutable) processes.Add(new PrismProcess(process));
                else
                {
                    unidentified = true;
                    process.Dispose();
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
            {
                unidentified = true;
                process.Dispose();
            }
        }
        return processes;
    }

    public void Start(PrismLauncherTarget target)
    {
        var start = new ProcessStartInfo(target.ExecutablePath) { UseShellExecute = true };
        start.ArgumentList.Add("--dir");
        start.ArgumentList.Add(target.DataRoot);
        _ = Process.Start(start) ?? throw new InvalidOperationException("Windows did not accept the Prism Launcher start request.");
    }

    private sealed class PrismProcess(Process process) : IMinecraftLauncherProcess
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
}
