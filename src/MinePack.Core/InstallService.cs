using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace MinePack.Core;

public sealed class InstallService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly DownloadEngine _downloads;
    private readonly object _logLock = new();
    private string? _logPath;

    public InstallService(DownloadEngine? downloads = null)
    {
        _downloads = downloads ?? new DownloadEngine();
        _downloads.Diagnostic += OnDownloadDiagnostic;
    }

    public static string DefaultInstallRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MinePack");

    public string? GetActiveInstancePath(string installRoot)
    {
        var root = Path.GetFullPath(installRoot);
        var marker = SafePath.Resolve(root, ".minepack-active.json");
        SafePath.EnsureNoReparsePoints(root, marker);
        if (!File.Exists(marker)) return null;
        try
        {
            var active = JsonSerializer.Deserialize<ActiveInstallation>(File.ReadAllText(marker));
            if (active is null || active.SchemaVersion != 1 || string.IsNullOrWhiteSpace(active.InstanceDirectory)) return null;
            var path = SafePath.Resolve(root, active.InstanceDirectory);
            SafePath.EnsureNoReparsePoints(root, path);
            return Directory.Exists(path) && File.Exists(Path.Combine(path, InstallationManifest.FileName)) ? path : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or InstallerException)
        {
            return null;
        }
    }

    public string? GetLatestLogPath(string installRoot)
    {
        var directory = Path.Combine(Path.GetFullPath(installRoot), "logs");
        if (!Directory.Exists(directory)) return null;
        return Directory.EnumerateFiles(directory, "*.jsonl").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
    }

    public async Task<InstallResult> InstallAsync(string packPath, string expectedPackSha512, string installRoot,
        IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        string? instancePath = null;
        string? stagingRoot = null;
        var movedToFinal = false;
        var installCommitted = false;
        try
        {
            var root = PrepareRoot(installRoot);
            StartLog(root);
            Log("install_started", new { packPath = Path.GetFileName(packPath) });
            var pack = PackArchive.Open(packPath, expectedPackSha512);
            Log("pack_validated", new { pack.VersionId, pack.MinecraftVersion, pack.FabricLoaderVersion, pack.ArchiveSha512 });

            var instancesRoot = Path.Combine(root, "instances");
            SafePath.EnsureNoReparsePoints(root, instancesRoot);
            Directory.CreateDirectory(instancesRoot);
            var instanceName = $"test-pack-{pack.VersionId}-{pack.ArchiveSha512[..12].ToLowerInvariant()}";
            instancePath = SafePath.Resolve(instancesRoot, instanceName);
            if (Directory.Exists(instancePath))
            {
                SafePath.EnsureNoReparsePoints(instancesRoot, instancePath);
                if (File.Exists(Path.Combine(instancePath, InstallationManifest.FileName)))
                {
                    ValidateMatchesRelease(InstallationManifest.Load(instancePath), pack);
                    var restored = await RepairAsync(instancePath, packPath, expectedPackSha512, progress, cancellationToken);
                    if (!restored.Success) return restored;
                    var existingRelativePath = Path.GetRelativePath(root, instancePath).Replace(Path.DirectorySeparatorChar, '/');
                    cancellationToken.ThrowIfCancellationRequested();
                    WriteActive(root, existingRelativePath);
                    try { Log("install_reactivated", new { instancePath }); }
                    catch { }
                    return restored;
                }
                instanceName += "-reinstall-" + Guid.NewGuid().ToString("N");
                instancePath = SafePath.Resolve(instancesRoot, instanceName);
            }

            stagingRoot = Path.Combine(root, "staging", instanceName + "-" + Guid.NewGuid().ToString("N"));
            SafePath.EnsureNoReparsePoints(root, stagingRoot);
            Directory.CreateDirectory(stagingRoot);
            var managed = new ConcurrentBag<ManagedFile>();
            var total = pack.Files.Count + pack.Overrides.Count;
            var completed = 0;
            progress?.Report(new InstallProgress("prepare", LocalizedText.Get("PreparingInstance"), 0, total));

            var tasks = pack.Files.Select(async file =>
            {
                var lastByteReport = 0L;
                var downloaded = await _downloads.DownloadVerifiedAsync(file, stagingRoot, (bytes, expected) =>
                {
                    var now = Stopwatch.GetTimestamp();
                    if (now - lastByteReport < Stopwatch.Frequency / 5) return;
                    lastByteReport = now;
                    progress?.Report(new InstallProgress("download", LocalizedText.Get("DownloadingFile", Path.GetFileName(file.Path)), Volatile.Read(ref completed), total, bytes, expected));
                }, cancellationToken);
                var size = new FileInfo(downloaded).Length;
                managed.Add(new ManagedFile(file.Path, file.Sha512, file.Downloads.Select(x => x.AbsoluteUri).ToArray(), false, size));
                Log("download_verified", new { path = file.Path, source = file.Downloads[0].GetLeftPart(UriPartial.Authority), file.Sha512, size });
                progress?.Report(new InstallProgress("download", LocalizedText.Get("VerifiedFile", Path.GetFileName(file.Path)), Interlocked.Increment(ref completed), total));
            });
            await Task.WhenAll(tasks);

            var overrides = await pack.ExtractOverridesAsync(stagingRoot, cancellationToken);
            foreach (var file in overrides)
            {
                if (!IsInitialUserConfig(pack, file.Path))
                    managed.Add(file);
                Log("override_applied", new { path = file.Path, file.Sha512, file.Size });
                progress?.Report(new InstallProgress("override", LocalizedText.Get("AppliedOverride", Path.GetFileName(file.Path)), Interlocked.Increment(ref completed), total));
            }

            var manifest = new InstallationManifest
            {
                PackVersion = pack.VersionId,
                MinecraftVersion = pack.MinecraftVersion,
                FabricLoaderVersion = pack.FabricLoaderVersion,
                PackArchiveSha512 = pack.ArchiveSha512,
                InstalledAt = DateTimeOffset.UtcNow,
                Files = managed.OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToList()
            };
            await VerifyManagedFilesAsync(stagingRoot, manifest.Files, cancellationToken);
            if (pack.ArchiveSha512.Equals(TestPackRelease.ArtifactSha512, StringComparison.OrdinalIgnoreCase) ||
                pack.ArchiveSha512.Equals(TestPackRelease.SmoothArtifactSha512, StringComparison.OrdinalIgnoreCase) ||
                pack.ArchiveSha512.Equals(TestPackRelease.PriorArtifactSha512, StringComparison.OrdinalIgnoreCase) ||
                pack.ArchiveSha512.Equals(Vanilla2PlusRelease.ArtifactSha512, StringComparison.OrdinalIgnoreCase) ||
                pack.ArchiveSha512.Equals(Vanilla2PlusRelease.YungsArtifactSha512, StringComparison.OrdinalIgnoreCase) ||
                pack.ArchiveSha512.Equals(Vanilla2PlusRelease.TunedArtifactSha512, StringComparison.OrdinalIgnoreCase) ||
                pack.ArchiveSha512.Equals(Vanilla2PlusRelease.WorldgenArtifactSha512, StringComparison.OrdinalIgnoreCase) ||
                pack.ArchiveSha512.Equals(Vanilla2PlusRelease.GuardArtifactSha512, StringComparison.OrdinalIgnoreCase) ||
                pack.ArchiveSha512.Equals(Vanilla2PlusRelease.PriorArtifactSha512, StringComparison.OrdinalIgnoreCase) ||
                pack.ArchiveSha512.Equals(Vanilla2PlusRelease.PreviousArtifactSha512, StringComparison.OrdinalIgnoreCase) ||
                pack.ArchiveSha512.Equals(Vanilla2PlusRelease.LegacyArtifactSha512, StringComparison.OrdinalIgnoreCase) ||
                pack.ArchiveSha512.Equals(Vanilla2PlusRelease.OriginalArtifactSha512, StringComparison.OrdinalIgnoreCase))
            {
                var guardAnimationPacks = pack.ArchiveSha512.Equals(Vanilla2PlusRelease.ArtifactSha512, StringComparison.OrdinalIgnoreCase) ||
                    pack.ArchiveSha512.Equals(Vanilla2PlusRelease.YungsArtifactSha512, StringComparison.OrdinalIgnoreCase) ||
                    pack.ArchiveSha512.Equals(Vanilla2PlusRelease.TunedArtifactSha512, StringComparison.OrdinalIgnoreCase) ||
                    pack.ArchiveSha512.Equals(Vanilla2PlusRelease.WorldgenArtifactSha512, StringComparison.OrdinalIgnoreCase) ||
                    pack.ArchiveSha512.Equals(Vanilla2PlusRelease.GuardArtifactSha512, StringComparison.OrdinalIgnoreCase) ||
                    pack.ArchiveSha512.Equals(Vanilla2PlusRelease.PriorArtifactSha512, StringComparison.OrdinalIgnoreCase);
                var resourcePacks = guardAnimationPacks ? Vanilla2PlusRelease.InitialResourcePacks : TestPackRelease.InitialResourcePacks;
                if (resourcePacks.Any(name => !manifest.Files.Any(file =>
                    file.Path.Equals("resourcepacks/" + name, StringComparison.OrdinalIgnoreCase))))
                    throw new InstallerException("PACK_INVALID", LocalizedText.Get("PinnedResourcePackMissing"));
                await File.WriteAllTextAsync(Path.Combine(stagingRoot, "options.txt"),
                    guardAnimationPacks ? Vanilla2PlusRelease.InitialOptions : TestPackRelease.InitialOptions, cancellationToken);
                if (!manifest.Files.Any(file => file.Path.Equals("mods/bbe-fabric-1.3.7+mc26.2.jar", StringComparison.OrdinalIgnoreCase)))
                    throw new InstallerException("PACK_INVALID", LocalizedText.Get("PinnedBbeMissing"));
                var bbeConfig = SafePath.Resolve(stagingRoot, "config/BBEConfig.json");
                SafePath.EnsureNoReparsePoints(stagingRoot, bbeConfig);
                if (File.Exists(bbeConfig))
                    throw new InstallerException("PACK_INVALID", LocalizedText.Get("PinnedBbeConfigExists"));
                await File.WriteAllTextAsync(bbeConfig,
                    "{\"bbe.config.storage.main\":[{\"option\":\"optimize.chest\",\"value\":false},{\"option\":\"optimize.shulker\",\"value\":false}]}",
                    cancellationToken);
            }
            manifest.SaveAtomic(stagingRoot);
            Log("staging_verified", new { files = manifest.Files.Count, stagingRoot });

            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(instancePath)!);
            Directory.Move(stagingRoot, instancePath);
            movedToFinal = true;
            var relativeInstancePath = Path.GetRelativePath(root, instancePath).Replace(Path.DirectorySeparatorChar, '/');
            cancellationToken.ThrowIfCancellationRequested();
            WriteActive(root, relativeInstancePath);
            installCommitted = true;
            try { Log("install_committed", new { instancePath }); }
            catch { }
            try { progress?.Report(new InstallProgress("complete", LocalizedText.Get("InstallFilesComplete"), total, total)); }
            catch { }
            return new InstallResult(true, "OK", LocalizedText.Get("PackFilesInstalled"), instancePath, _logPath);
        }
        catch (OperationCanceledException)
        {
            LogSafe("install_cancelled");
            return new InstallResult(false, "CANCELLED", LocalizedText.Get("InstallCancelled"), installCommitted ? instancePath : null, _logPath);
        }
        catch (InstallerException ex)
        {
            LogSafe("install_failed", new { ex.Code, ex.Message });
            return new InstallResult(false, ex.Code, ex.Message, installCommitted ? instancePath : null, _logPath);
        }
        catch (Exception ex)
        {
            LogSafe("install_failed", new { error = ex.GetType().Name });
            return new InstallResult(false, "INSTALL_FAILED", LocalizedText.Get("InstallFailed"), installCommitted ? instancePath : null, _logPath);
        }
        finally
        {
            if (movedToFinal && !installCommitted && instancePath is not null)
                TryDeleteDirectory(Path.GetDirectoryName(instancePath)!, instancePath);
            if (stagingRoot is not null) TryDeleteDirectory(root: Path.GetDirectoryName(Path.GetDirectoryName(stagingRoot)!)!, path: stagingRoot);
        }
    }

    public async Task<InstallResult> RepairAsync(string instancePath, string packPath, string expectedPackSha512,
        IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var repairRoot = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(instancePath))!, ".repair-" + Guid.NewGuid().ToString("N"));
        try
        {
            var fullInstance = Path.GetFullPath(instancePath);
            EnsureNotVanilla(fullInstance);
            StartLog(Path.GetDirectoryName(Path.GetDirectoryName(fullInstance)!)!);
            var manifest = InstallationManifest.Load(fullInstance);
            var pack = PackArchive.Open(packPath, expectedPackSha512);
            ValidateMatchesRelease(manifest, pack);
            var instanceParent = Path.GetDirectoryName(fullInstance)!;
            SafePath.EnsureNoReparsePoints(instanceParent, repairRoot);
            Directory.CreateDirectory(repairRoot);
            var total = manifest.Files.Count;
            var completed = 0;
            var overrideFiles = await pack.ExtractOverridesAsync(repairRoot, cancellationToken);
            var overrideByPath = overrideFiles.ToDictionary(x => x.Path, StringComparer.OrdinalIgnoreCase);
            var packFiles = pack.Files.ToDictionary(x => x.Path, StringComparer.OrdinalIgnoreCase);
            var repaired = 0;
            foreach (var managed in manifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = SafePath.Resolve(fullInstance, managed.Path);
                SafePath.EnsureNoReparsePoints(fullInstance, target);
                if (File.Exists(target) && PackArchive.FixedTimeHashEquals(PackArchive.HashFile(target), managed.Sha512))
                {
                    progress?.Report(new InstallProgress("repair", LocalizedText.Get("VerifiedFile", Path.GetFileName(target)), ++completed, total));
                    continue;
                }

                var staged = SafePath.Resolve(repairRoot, managed.Path);
                if (!managed.IsOverride)
                {
                    var source = packFiles[managed.Path];
                    await _downloads.DownloadVerifiedAsync(source, repairRoot, null, cancellationToken);
                }
                else if (!overrideByPath.ContainsKey(managed.Path))
                {
                    throw new InstallerException("MANIFEST_INVALID", LocalizedText.Get("ManifestOverridesMismatch"));
                }
                if (!PackArchive.FixedTimeHashEquals(PackArchive.HashFile(staged), managed.Sha512))
                    throw new InstallerException("REPAIR_HASH_MISMATCH", LocalizedText.Get("RepairedHashMismatch"));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                SafePath.EnsureNoReparsePoints(fullInstance, target);
                File.Move(staged, target, overwrite: true);
                repaired++;
                progress?.Report(new InstallProgress("repair", LocalizedText.Get("RestoredFile", Path.GetFileName(target)), ++completed, total));
                Log("repair_file", new { path = managed.Path, managed.Sha512 });
            }
            await VerifyManagedFilesAsync(fullInstance, manifest.Files, cancellationToken);
            Log("repair_complete", new { repaired, total });
            return new InstallResult(true, "OK", repaired == 0 ? LocalizedText.Get("RepairAllHealthy") : LocalizedText.Get("RepairFilesRestored", repaired), fullInstance, _logPath);
        }
        catch (OperationCanceledException)
        {
            LogSafe("repair_cancelled");
            return new InstallResult(false, "CANCELLED", LocalizedText.Get("RepairCancelled"), instancePath, _logPath);
        }
        catch (InstallerException ex)
        {
            LogSafe("repair_failed", new { ex.Code, ex.Message });
            return new InstallResult(false, ex.Code, ex.Message, instancePath, _logPath);
        }
        catch (Exception ex)
        {
            LogSafe("repair_failed", new { error = ex.GetType().Name });
            return new InstallResult(false, "REPAIR_FAILED", LocalizedText.Get("RepairFailed"), instancePath, _logPath);
        }
        finally { TryDeleteDirectory(Path.GetDirectoryName(repairRoot)!, repairRoot); }
    }

    public Task<InstallResult> UninstallAsync(string instancePath, string packPath, string expectedPackSha512)
    {
        try
        {
            var fullInstance = Path.GetFullPath(instancePath);
            EnsureNotVanilla(fullInstance);
            var installRoot = Path.GetDirectoryName(Path.GetDirectoryName(fullInstance)!)!;
            StartLog(installRoot);
            var manifest = InstallationManifest.Load(fullInstance);
            var pack = PackArchive.Open(packPath, expectedPackSha512);
            ValidateMatchesRelease(manifest, pack);
            var activeInstance = GetActiveInstancePath(installRoot);
            Log("uninstall_started", new { fullInstance, managedFiles = manifest.Files.Count });

            foreach (var file in manifest.Files)
            {
                var target = SafePath.Resolve(fullInstance, file.Path);
                SafePath.EnsureNoReparsePoints(fullInstance, target);
                if (File.Exists(target)) File.Delete(target);
                RemoveEmptyParents(fullInstance, Path.GetDirectoryName(target)!);
            }
            var marker = SafePath.Resolve(fullInstance, InstallationManifest.FileName);
            SafePath.EnsureNoReparsePoints(fullInstance, marker);
            if (File.Exists(marker)) File.Delete(marker);
            var activeMarker = SafePath.Resolve(installRoot, ".minepack-active.json");
            SafePath.EnsureNoReparsePoints(installRoot, activeMarker);
            if (File.Exists(activeMarker) && activeInstance?.Equals(fullInstance, StringComparison.OrdinalIgnoreCase) == true)
                File.Delete(activeMarker);

            SafePath.EnsureNoReparsePoints(fullInstance, fullInstance);
            if (Directory.Exists(fullInstance) && !Directory.EnumerateFileSystemEntries(fullInstance).Any())
                Directory.Delete(fullInstance);
            RemoveEmptyParents(installRoot, Path.GetDirectoryName(fullInstance)!);

            Log("uninstall_complete", new { remainingDirectory = Directory.Exists(fullInstance) ? fullInstance : null });
            var remains = Directory.Exists(fullInstance);
            return Task.FromResult(new InstallResult(true, "OK", LocalizedText.Get(remains ? "UninstallFilesRemovedWithData" : "UninstallFilesRemoved"), remains ? fullInstance : null, _logPath));
        }
        catch (InstallerException ex)
        {
            LogSafe("uninstall_failed", new { ex.Code, ex.Message });
            return Task.FromResult(new InstallResult(false, ex.Code, ex.Message, instancePath, _logPath));
        }
        catch (Exception ex)
        {
            LogSafe("uninstall_failed", new { error = ex.GetType().Name });
            return Task.FromResult(new InstallResult(false, "UNINSTALL_FAILED", LocalizedText.Get("UninstallFailed"), instancePath, _logPath));
        }
    }

    private static string PrepareRoot(string installRoot)
    {
        var root = Path.GetFullPath(installRoot);
        var volumeRoot = Path.GetPathRoot(root);
        if (string.Equals(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            volumeRoot?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new InstallerException("ROOT_UNSAFE", LocalizedText.Get("DriveRootUnsafe"));
        EnsureNotVanilla(root);
        SafePath.EnsureNoReparsePoints(root, root);
        Directory.CreateDirectory(root);
        SafePath.EnsureNoReparsePoints(root, root);
        return root;
    }

    private static void EnsureNotVanilla(string path)
    {
        var vanilla = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft"));
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var canonicalVanilla = vanilla.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (full.Equals(canonicalVanilla, StringComparison.OrdinalIgnoreCase) ||
            full.StartsWith(canonicalVanilla + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InstallerException("VANILLA_PATH_BLOCKED", LocalizedText.Get("VanillaPathBlocked"));
    }

    private void StartLog(string root)
    {
        var logDirectory = Path.Combine(root, "logs");
        SafePath.EnsureNoReparsePoints(root, logDirectory);
        Directory.CreateDirectory(logDirectory);
        _logPath = Path.Combine(logDirectory, $"minepack-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.jsonl");
    }

    private void Log(string name, object? detail = null)
    {
        if (_logPath is null) return;
        var line = JsonSerializer.Serialize(new { timestamp = DateTimeOffset.UtcNow, eventName = name, detail });
        lock (_logLock) File.AppendAllText(_logPath, line + Environment.NewLine);
    }

    private void LogSafe(string name, object? detail = null)
    {
        try { Log(name, detail); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void OnDownloadDiagnostic(string name, object? detail) => LogSafe(name, detail);

    private void WriteActive(string root, string relativeInstancePath)
    {
        var marker = SafePath.Resolve(root, ".minepack-active.json");
        SafePath.EnsureNoReparsePoints(root, marker);
        if (File.Exists(marker))
        {
            try
            {
                var current = JsonSerializer.Deserialize<ActiveInstallation>(File.ReadAllText(marker));
                if (current is null || current.SchemaVersion != 1 || string.IsNullOrWhiteSpace(current.InstanceDirectory) ||
                    GetActiveInstancePath(root) is null)
                    throw new InstallerException("ACTIVE_MARKER_CONFLICT", LocalizedText.Get("ActiveMarkerCorrupt"));
                _ = SafePath.Resolve(root, current.InstanceDirectory);
            }
            catch (JsonException ex)
            {
                throw new InstallerException("ACTIVE_MARKER_CONFLICT", LocalizedText.Get("ActiveMarkerCorrupt"), ex);
            }
        }
        var temp = marker + "." + Guid.NewGuid().ToString("N") + ".tmp";
        SafePath.EnsureNoReparsePoints(root, temp);
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, new ActiveInstallation(1, relativeInstancePath), JsonOptions);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, marker, overwrite: true);
        }
        finally
        {
            try { File.Delete(temp); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static async Task VerifyManagedFilesAsync(string root, IEnumerable<ManagedFile> files, CancellationToken cancellationToken)
    {
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = SafePath.Resolve(root, file.Path);
            SafePath.EnsureNoReparsePoints(root, path);
            if (!File.Exists(path) || !PackArchive.FixedTimeHashEquals(await Task.Run(() => PackArchive.HashFile(path), cancellationToken), file.Sha512))
                throw new InstallerException("INSTALL_VERIFY_FAILED", LocalizedText.Get("InstallFileVerificationFailed", Path.GetFileName(path)));
        }
    }

    private static void ValidateMatchesRelease(InstallationManifest manifest, PackArchive pack)
    {
        if (!PackArchive.FixedTimeHashEquals(manifest.PackArchiveSha512, pack.ArchiveSha512) ||
            manifest.PackVersion != pack.VersionId || manifest.MinecraftVersion != pack.MinecraftVersion ||
            manifest.FabricLoaderVersion != pack.FabricLoaderVersion)
            throw new InstallerException("RELEASE_MISMATCH", LocalizedText.Get("InstalledReleaseMismatch"));

        var packFiles = pack.Files.ToDictionary(x => x.Path, StringComparer.OrdinalIgnoreCase);
        var overrides = pack.Overrides.Where(x => !IsInitialUserConfig(pack, x.Path))
            .ToDictionary(x => x.Path, StringComparer.OrdinalIgnoreCase);
        if (manifest.Files.Count != packFiles.Count + overrides.Count)
            throw new InstallerException("MANIFEST_INVALID", LocalizedText.Get("ManifestReleaseMismatch"));
        foreach (var item in manifest.Files)
        {
            if (item.IsOverride)
            {
                if (!overrides.ContainsKey(item.Path)) throw new InstallerException("MANIFEST_INVALID", LocalizedText.Get("ManifestUnknownOverride"));
            }
            else if (!packFiles.TryGetValue(item.Path, out var source) ||
                     !PackArchive.FixedTimeHashEquals(item.Sha512, source.Sha512) ||
                     !item.Downloads.SequenceEqual(source.Downloads.Select(x => x.AbsoluteUri), StringComparer.OrdinalIgnoreCase))
                throw new InstallerException("MANIFEST_INVALID", LocalizedText.Get("ManifestUnknownManagedFile"));
        }
    }

    private static bool IsInitialUserConfig(PackArchive pack, string path) =>
        (pack.ArchiveSha512.Equals(Vanilla2PlusRelease.ArtifactSha512, StringComparison.OrdinalIgnoreCase) &&
         (path.Equals("config/guardvillagers.json", StringComparison.OrdinalIgnoreCase) ||
          path.Equals("config/voxyworldgenv2.json", StringComparison.OrdinalIgnoreCase))) ||
        (pack.ArchiveSha512.Equals(Vanilla2PlusRelease.YungsArtifactSha512, StringComparison.OrdinalIgnoreCase) &&
         (path.Equals("config/guardvillagers.json", StringComparison.OrdinalIgnoreCase) ||
          path.Equals("config/voxyworldgenv2.json", StringComparison.OrdinalIgnoreCase))) ||
        (pack.ArchiveSha512.Equals(Vanilla2PlusRelease.TunedArtifactSha512, StringComparison.OrdinalIgnoreCase) &&
         (path.Equals("config/guardvillagers.json", StringComparison.OrdinalIgnoreCase) ||
          path.Equals("config/voxyworldgenv2.json", StringComparison.OrdinalIgnoreCase))) ||
        (pack.ArchiveSha512.Equals(Vanilla2PlusRelease.UntunedArtifactSha512, StringComparison.OrdinalIgnoreCase) &&
         (path.Equals("config/guardvillagers.json", StringComparison.OrdinalIgnoreCase) ||
          path.Equals("config/voxyworldgenv2.json", StringComparison.OrdinalIgnoreCase))) ||
        (pack.ArchiveSha512.Equals(Vanilla2PlusRelease.WorldgenArtifactSha512, StringComparison.OrdinalIgnoreCase) &&
         (path.Equals("config/guardvillagers.json", StringComparison.OrdinalIgnoreCase) ||
          path.Equals("config/voxyworldgenv2.json", StringComparison.OrdinalIgnoreCase))) ||
        (pack.ArchiveSha512.Equals(Vanilla2PlusRelease.GuardArtifactSha512, StringComparison.OrdinalIgnoreCase) &&
         path.Equals("config/guardvillagers.json", StringComparison.OrdinalIgnoreCase));

    private void RemoveEmptyParents(string root, string directory)
    {
        var fullRoot = Path.GetFullPath(root);
        var current = Path.GetFullPath(directory);
        while (current.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && Directory.Exists(current))
        {
            SafePath.EnsureNoReparsePoints(fullRoot, current);
            if (Directory.EnumerateFileSystemEntries(current).Any()) break;
            Directory.Delete(current);
            current = Path.GetDirectoryName(current)!;
        }
    }

    private static void TryDeleteDirectory(string root, string path)
    {
        try
        {
            SafePath.EnsureNoReparsePoints(root, path);
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (InstallerException) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        _downloads.Diagnostic -= OnDownloadDiagnostic;
        _downloads.Dispose();
    }

    private sealed record ActiveInstallation(int SchemaVersion, string InstanceDirectory);
}
