using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MinePack.Core;

public sealed class InstanceCleanupRequest
{
    internal InstanceCleanupRequest(InstalledInstanceEntry entry, string installRoot, string applicationDirectory,
        string officialProfilesRoot, string targetPath, string gameDirectory, KnownPackRelease release,
        string snapshotHash, IReadOnlyList<string> categories)
    {
        Entry = entry;
        InstallRoot = installRoot;
        ApplicationDirectory = applicationDirectory;
        OfficialProfilesRoot = officialProfilesRoot;
        TargetPath = targetPath;
        GameDirectory = gameDirectory;
        Release = release;
        SnapshotHash = snapshotHash;
        Categories = Array.AsReadOnly(categories.ToArray());
    }

    internal InstalledInstanceEntry Entry { get; }
    internal string InstallRoot { get; }
    internal string ApplicationDirectory { get; }
    internal string OfficialProfilesRoot { get; }
    internal string GameDirectory { get; }
    internal KnownPackRelease Release { get; }
    internal string SnapshotHash { get; }
    public string TargetPath { get; }
    public IReadOnlyList<string> Categories { get; }
    public bool IsPrism => Entry.Layout?.IsPrism == true;
}

public sealed record InstanceCleanupResult(bool OwnershipRecordRetained);

public static class InstanceRemovalService
{
    private const int MaximumTreeEntries = 200_000;

    private static readonly (string Name, string Category)[] UserCategories =
    [
        ("saves", "UiCleanupCategorySaves"),
        ("options.txt", "UiCleanupCategorySettings"),
        ("screenshots", "UiCleanupCategoryScreenshots"),
        ("resourcepacks", "UiCleanupCategoryResourcePacks"),
        ("shaderpacks", "UiCleanupCategoryShaderPacks"),
        ("logs", "UiCleanupCategoryLogs")
    ];

    public static bool CanCleanup(InstalledInstanceEntry entry, string installRoot, string applicationDirectory)
    {
        try
        {
            _ = PrepareCleanup(entry, installRoot, applicationDirectory, FabricLauncherService.DefaultLauncherRoot);
            return true;
        }
        catch (Exception ex) when (IsExpectedFailure(ex)) { return false; }
    }

    public static InstanceCleanupRequest PrepareCleanup(InstalledInstanceEntry entry, string installRoot,
        string applicationDirectory, string officialProfilesRoot) =>
        PrepareCleanupCore(entry, installRoot, applicationDirectory, officialProfilesRoot, inspectGameUse: true);

    internal static InstanceCleanupRequest PrepareCleanupForTesting(InstalledInstanceEntry entry, string installRoot,
        string applicationDirectory, string officialProfilesRoot) =>
        PrepareCleanupCore(entry, installRoot, applicationDirectory, officialProfilesRoot, inspectGameUse: true);

    public static Task<InstanceCleanupResult> RemoveAsync(InstanceCleanupRequest request,
        Func<string, Task> moveToRecycleBin)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(moveToRecycleBin);
        return OperationGuard.RunAsync(async () =>
        {
            var beforeLock = PrepareCleanupCore(request.Entry, request.InstallRoot, request.ApplicationDirectory,
                request.OfficialProfilesRoot, inspectGameUse: false);
            RequireSameSnapshot(request, beforeLock);

            using (var instanceUse = InstanceUseGuard.Acquire(request.GameDirectory))
            {
                instanceUse.Recheck();
                var lockedSnapshot = PrepareCleanupCore(request.Entry, request.InstallRoot,
                    request.ApplicationDirectory, request.OfficialProfilesRoot, inspectGameUse: false,
                    instanceUse.ProtectedWorldLockPaths);
                RequireSameSnapshot(request, lockedSnapshot);
                instanceUse.Recheck();
            }

            var immediatelyBeforeRecycle = PrepareCleanupCore(request.Entry, request.InstallRoot,
                request.ApplicationDirectory, request.OfficialProfilesRoot, inspectGameUse: true);
            RequireSameSnapshot(request, immediatelyBeforeRecycle);

            await moveToRecycleBin(request.TargetPath).ConfigureAwait(false);
            if (Directory.Exists(request.TargetPath) || File.Exists(request.TargetPath))
                throw CleanupError("INSTANCE_CLEANUP_NOT_COMPLETED", "InstanceCleanupRecycleFailed");

            if (!request.IsPrism) return new InstanceCleanupResult(false);
            var layout = request.Entry.Layout!;
            var archive = request.Release.ArchivePath(request.ApplicationDirectory);
            if (!File.Exists(archive)) return new InstanceCleanupResult(true);
            try
            {
                var pack = PackArchive.Open(archive, request.Release.ArchiveSha512);
                PrismLauncherService.RemoveLocalOwnershipRecord(layout, pack);
                return new InstanceCleanupResult(false);
            }
            catch (Exception ex) when (ex is InstallerException or IOException or UnauthorizedAccessException)
            {
                return new InstanceCleanupResult(true);
            }
        });
    }

    private static InstanceCleanupRequest PrepareCleanupCore(InstalledInstanceEntry entry, string installRoot,
        string applicationDirectory, string officialProfilesRoot, bool inspectGameUse,
        IReadOnlySet<string>? protectedWorldLockPaths = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.State != InstalledInstanceState.Residue || !entry.IsOpenable)
            throw CleanupError("INSTANCE_CLEANUP_UNAVAILABLE", "InstanceCleanupUnavailable");
        if (!OperatingSystem.IsWindows())
            throw CleanupError("INSTANCE_CLEANUP_UNAVAILABLE", "InstanceCleanupUnavailable");

        var appRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(applicationDirectory));
        string root;
        string target;
        string gameDirectory;
        KnownPackRelease release;
        var layout = entry.Layout;
        if (layout is { IsPrism: true })
        {
            layout.Validate();
            LauncherDiscovery.RevalidatePrismTarget(new PrismLauncherTarget(layout.LauncherIdentity,
                layout.PrismDataRoot!, layout.InstancesRoot, layout.Fingerprint, "residue-cleanup"));
            root = layout.InstancesRoot;
            target = layout.InstanceDirectory;
            gameDirectory = layout.GameDirectory;
            if (!SamePath(entry.Path, gameDirectory) ||
                !PrismLauncherService.TryGetOwnedResidueRelease(layout, out release))
                throw CleanupError("INSTANCE_CLEANUP_UNAVAILABLE", "InstanceCleanupUnavailable");
            if (entry.Release is { } catalogRelease && !SameRelease(catalogRelease, release))
                throw CleanupError("INSTANCE_CLEANUP_UNAVAILABLE", "InstanceCleanupUnavailable");
            ManagedFileTransaction.EnsureNoPendingForInstance(layout);
            ManagedFileTransaction.EnsureNoPendingForOtherOwnedInstances(layout);
            var marker = InstallService.ReadActiveMarker(layout);
            if (marker.State == ActiveMarkerState.Valid && SamePath(marker.InstancePath, gameDirectory))
                throw CleanupError("INSTANCE_CLEANUP_ACTIVE", "InstanceCleanupActive");
            if (marker.State is not (ActiveMarkerState.Absent or ActiveMarkerState.Valid))
                throw CleanupError("INSTANCE_CLEANUP_UNAVAILABLE", "InstanceCleanupUnavailable");

            EnsureMissing(layout.GameDirectory, InstallationManifest.FileName);
            EnsureMissing(layout.InstanceDirectory, "instance.cfg");
            EnsureMissing(layout.InstanceDirectory, "mmc-pack.json");
        }
        else
        {
            root = InstallService.ValidateInstallRoot(installRoot);
            target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(entry.Path));
            gameDirectory = target;
            var instancesRoot = Path.Combine(root, "instances");
            if (!SamePath(Path.GetDirectoryName(target), instancesRoot) ||
                !InstanceDirectoryNaming.TryGetReleaseFromName(Path.GetFileName(target), out release) ||
                (entry.Release is { } catalogRelease && !SameRelease(catalogRelease, release)))
                throw CleanupError("INSTANCE_CLEANUP_UNAVAILABLE", "InstanceCleanupUnavailable");
            EnsureMissing(target, InstallationManifest.FileName);
            ManagedFileTransaction.EnsureNoPendingUnderRoot(root);
            var marker = InstallService.ReadActiveMarker(root);
            if (marker.State == ActiveMarkerState.Valid && SamePath(marker.InstancePath, target))
                throw CleanupError("INSTANCE_CLEANUP_ACTIVE", "InstanceCleanupActive");
            if (marker.State is not (ActiveMarkerState.Absent or ActiveMarkerState.Valid))
                throw CleanupError("INSTANCE_CLEANUP_UNAVAILABLE", "InstanceCleanupUnavailable");
        }

        EnsureNoLauncherProfileReference(officialProfilesRoot, target);

        var instanceParent = layout?.InstancesRoot ?? Path.Combine(root, "instances");
        if (!SamePath(Path.GetDirectoryName(target), instanceParent) || !Directory.Exists(target))
            throw CleanupError("INSTANCE_CLEANUP_UNAVAILABLE", "InstanceCleanupUnavailable");
        EnsureFixedLocalDrive(target);
        SafePath.EnsureNoReparsePoints(instanceParent, target);
        var snapshot = SnapshotDirectory(target, out var categories, protectedWorldLockPaths);
        if (inspectGameUse) InstanceUseGuard.EnsureNoGameProcess(gameDirectory);
        return new InstanceCleanupRequest(entry, root, appRoot,
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(officialProfilesRoot)), target, gameDirectory,
            release, snapshot, categories);
    }

    private static void RequireSameSnapshot(InstanceCleanupRequest expected, InstanceCleanupRequest current)
    {
        var expectedFingerprint = expected.Entry.Layout?.Fingerprint ?? "official";
        var currentFingerprint = current.Entry.Layout?.Fingerprint ?? "official";
        if (!SamePath(expected.TargetPath, current.TargetPath) ||
            !expectedFingerprint.Equals(currentFingerprint, StringComparison.OrdinalIgnoreCase) ||
            !SameRelease(expected.Release, current.Release) ||
            !expected.SnapshotHash.Equals(current.SnapshotHash, StringComparison.Ordinal))
            throw CleanupError("INSTANCE_CLEANUP_CHANGED", "InstanceCleanupChanged");
    }

    private static string SnapshotDirectory(string root, out IReadOnlyList<string> categories,
        IReadOnlySet<string>? protectedWorldLockPaths = null)
    {
        var records = new List<string>();
        var rootAttributes = File.GetAttributes(root);
        if ((rootAttributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != FileAttributes.Directory)
            throw CleanupError("INSTANCE_CLEANUP_UNAVAILABLE", "InstanceCleanupUnavailable");
        records.Add($"<ROOT>\0D\0{(int)(rootAttributes & (FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System))}\0{Directory.GetCreationTimeUtc(root).Ticks}\0{Directory.GetLastWriteTimeUtc(root).Ticks}");
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count != 0)
        {
            var directory = stack.Pop();
            var attributes = File.GetAttributes(directory);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != FileAttributes.Directory)
                throw CleanupError("INSTANCE_CLEANUP_UNAVAILABLE", "InstanceCleanupUnavailable");
            var entries = Directory.EnumerateFileSystemEntries(directory).OrderBy(path => path, StringComparer.Ordinal).ToArray();
            foreach (var path in entries)
            {
                if (records.Count >= MaximumTreeEntries)
                    throw CleanupError("INSTANCE_CLEANUP_LIMIT", "InstanceCleanupTooLarge");
                var itemAttributes = File.GetAttributes(path);
                if ((itemAttributes & FileAttributes.ReparsePoint) != 0)
                    throw CleanupError("INSTANCE_CLEANUP_UNAVAILABLE", "InstanceCleanupUnavailable");
                var relative = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
                if (Path.IsPathRooted(relative) || relative.Split('/').Any(part => part is "" or "." or ".."))
                    throw CleanupError("INSTANCE_CLEANUP_UNAVAILABLE", "InstanceCleanupUnavailable");
                var isDirectory = (itemAttributes & FileAttributes.Directory) != 0;
                var lastWrite = Directory.GetLastWriteTimeUtc(path).Ticks;
                long length = 0;
                if (isDirectory) stack.Push(path);
                else
                {
                    var info = new FileInfo(path);
                    length = info.Length;
                    lastWrite = info.LastWriteTimeUtc.Ticks;
                    if (protectedWorldLockPaths?.Contains(Path.GetFullPath(path)) != true)
                    {
                        try
                        {
                            using var probe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None, 1,
                                FileOptions.SequentialScan);
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            throw CleanupError("INSTANCE_CLEANUP_LOCKED", "InstanceCleanupLocked");
                        }
                    }
                }
                records.Add($"{relative}\0{(isDirectory ? 'D' : 'F')}\0{length}\0{(int)(itemAttributes & (FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System))}\0{lastWrite}");
            }
        }

        records.Sort(StringComparer.Ordinal);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var record in records) hash.AppendData(Encoding.UTF8.GetBytes(record + "\n"));
        categories = Array.AsReadOnly(UserCategories.Where(category => PathExistsStrict(Path.Combine(root, category.Name)))
            .Select(category => category.Category).ToArray());
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void EnsureNoLauncherProfileReference(string launcherRoot, string target)
    {
        var fullLauncherRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(launcherRoot));
        if (!InspectDirectoryPath(fullLauncherRoot)) return;
        SafePath.EnsureNoReparsePoints(fullLauncherRoot, fullLauncherRoot);
        foreach (var name in new[] { "launcher_profiles.json", "launcher_profiles_microsoft_store.json" })
        {
            var path = SafePath.Resolve(fullLauncherRoot, name);
            SafePath.EnsureNoReparsePoints(fullLauncherRoot, path);
            if (!PathExistsStrict(path)) continue;
            var info = new FileInfo(path);
            if (info.Length > 8 * 1024 * 1024)
                throw CleanupError("INSTANCE_CLEANUP_PROFILE_UNKNOWN", "InstanceCleanupProfileUnknown");
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 64 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || HasDuplicateProperties(root) ||
                !root.TryGetProperty("profiles", out var profiles) ||
                profiles.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null))
                throw CleanupError("INSTANCE_CLEANUP_PROFILE_UNKNOWN", "InstanceCleanupProfileUnknown");
            if (profiles.ValueKind == JsonValueKind.Null) continue;
            foreach (var profile in profiles.EnumerateObject())
            {
                if (profile.Value.ValueKind != JsonValueKind.Object || HasDuplicateProperties(profile.Value))
                    throw CleanupError("INSTANCE_CLEANUP_PROFILE_UNKNOWN", "InstanceCleanupProfileUnknown");
                if (!profile.Value.TryGetProperty("gameDir", out var gameDir)) continue;
                if (gameDir.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(gameDir.GetString()) ||
                    !Path.IsPathFullyQualified(gameDir.GetString()!) ||
                    gameDir.GetString()!.StartsWith("\\\\?\\", StringComparison.OrdinalIgnoreCase) ||
                    gameDir.GetString()!.StartsWith("\\\\.\\", StringComparison.OrdinalIgnoreCase))
                    throw CleanupError("INSTANCE_CLEANUP_PROFILE_UNKNOWN", "InstanceCleanupProfileUnknown");
                if (SameOrUnder(gameDir.GetString()!, target))
                    throw CleanupError("INSTANCE_CLEANUP_PROFILE_REFERENCE", "InstanceCleanupProfileReference");
            }
        }
    }

    private static bool InspectDirectoryPath(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.StartsWith("\\\\", StringComparison.Ordinal) ||
            path.StartsWith("\\\\?\\", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("\\\\.\\", StringComparison.OrdinalIgnoreCase))
            throw CleanupError("INSTANCE_CLEANUP_PROFILE_UNKNOWN", "InstanceCleanupProfileUnknown");
        var current = Path.GetPathRoot(path) ??
            throw CleanupError("INSTANCE_CLEANUP_PROFILE_UNKNOWN", "InstanceCleanupProfileUnknown");
        foreach (var segment in Path.GetRelativePath(current, path).Split(Path.DirectorySeparatorChar,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            FileAttributes attributes;
            try { attributes = File.GetAttributes(current); }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw CleanupError("INSTANCE_CLEANUP_PROFILE_UNKNOWN", "InstanceCleanupProfileUnknown");
            }
            if ((attributes & FileAttributes.ReparsePoint) != 0 ||
                (attributes & FileAttributes.Directory) == 0)
                throw CleanupError("INSTANCE_CLEANUP_PROFILE_UNKNOWN", "InstanceCleanupProfileUnknown");
        }
        return true;
    }

    private static bool HasDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
                if (!names.Add(property.Name) || HasDuplicateProperties(property.Value)) return true;
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                if (HasDuplicateProperties(item)) return true;
        }
        return false;
    }

    private static void EnsureMissing(string root, string name)
    {
        var path = SafePath.Resolve(root, name);
        SafePath.EnsureNoReparsePoints(root, path);
        if (PathExistsStrict(path))
            throw CleanupError("INSTANCE_CLEANUP_UNAVAILABLE", "InstanceCleanupUnavailable");
    }

    private static bool PathExistsStrict(string path)
    {
        try { _ = File.GetAttributes(path); return true; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    private static void EnsureFixedLocalDrive(string path)
    {
        if (path.StartsWith("\\\\", StringComparison.Ordinal) || Path.GetPathRoot(path) is not { } root)
            throw CleanupError("INSTANCE_CLEANUP_UNAVAILABLE", "InstanceCleanupDriveUnavailable");
        var drive = DriveInfo.GetDrives().FirstOrDefault(candidate =>
            candidate.Name.Equals(root, StringComparison.OrdinalIgnoreCase));
        if (drive is null || !drive.IsReady || drive.DriveType != DriveType.Fixed)
            throw CleanupError("INSTANCE_CLEANUP_UNAVAILABLE", "InstanceCleanupDriveUnavailable");
    }

    private static bool SameRelease(KnownPackRelease first, KnownPackRelease second) =>
        first.PackVersion == second.PackVersion && first.MinecraftVersion == second.MinecraftVersion &&
        first.FabricLoaderVersion == second.FabricLoaderVersion &&
        first.ArchiveSha512.Equals(second.ArchiveSha512, StringComparison.OrdinalIgnoreCase);

    private static bool SamePath(string? first, string? second) => first is not null && second is not null &&
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)).Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)), StringComparison.OrdinalIgnoreCase);

    private static bool SameOrUnder(string path, string root)
    {
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase) ||
               fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExpectedFailure(Exception exception) => exception is InstallerException or IOException or
        UnauthorizedAccessException or ArgumentException or NotSupportedException or JsonException or FormatException or
        InvalidOperationException or KeyNotFoundException;

    private static InstallerException CleanupError(string code, string resource) =>
        new(code, LocalizedText.Get(resource));
}
