using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace MinePack.Core;

public sealed class InstallService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly DownloadEngine _downloads;
    private OperationLog? _activeOperationLog;

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
        var pending = ManagedFileTransaction.FindPendingPathUnderRoot(root);
        if (pending is not null)
            throw new InstallerException("TRANSACTION_RECOVERY_REQUIRED", LocalizedText.Get("TransactionRecoveryRequired", pending));
        var marker = ReadActiveMarker(root);
        return marker.State == ActiveMarkerState.Valid ? marker.InstancePath : null;
    }

    public ActiveMarkerInspection InspectActiveMarker(string installRoot)
    {
        var root = Path.TrimEndingDirectorySeparator(ValidateInstallRoot(installRoot));
        var pending = ManagedFileTransaction.FindPendingPathUnderRoot(root);
        if (pending is not null)
            throw new InstallerException("TRANSACTION_RECOVERY_REQUIRED", LocalizedText.Get("TransactionRecoveryRequired", pending));
        var marker = ReadActiveMarker(root);
        return new ActiveMarkerInspection(marker.State, marker.InstancePath, marker.SchemaVersion);
    }

    public string? GetLatestLogPath(string installRoot)
    {
        var directory = Path.Combine(Path.GetFullPath(installRoot), "logs");
        if (!Directory.Exists(directory)) return null;
        return Directory.EnumerateFiles(directory, "*.jsonl").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
    }

    public (string InstancePath, string PackVersion)? GetPendingOperation(string installRoot)
    {
        var pending = ManagedFileTransaction.FindPendingUnderRoot(installRoot);
        return pending is null ? null : (pending.InstancePath, pending.PackVersion);
    }

    public Task<InstallResult> InstallAsync(string packPath, string expectedPackSha512, string installRoot,
        IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default,
        FabricLauncherService? launcher = null, OperationLog? operationLog = null,
        bool allowActiveMarkerRecovery = false)
    {
        var ownsLog = operationLog is null;
        operationLog ??= new OperationLog("install", null, installRoot);
        return GuardInstallResultAsync(() => InstallCoreAsync(packPath, expectedPackSha512, installRoot, progress,
                cancellationToken, launcher, allowActiveMarkerRecovery), cancellationToken, "INSTALL_FAILED", "InstallFailed", operationLog, ownsLog);
    }

    private async Task<InstallResult> InstallCoreAsync(string packPath, string expectedPackSha512, string installRoot,
        IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default,
        FabricLauncherService? launcher = null, bool allowActiveMarkerRecovery = false)
    {
        string? instancePath = null;
        string? stagingRoot = null;
        InstanceUseGuard.Scope? instanceUse = null;
        var movedToFinal = false;
        var installCommitted = false;
        string? markerBackupPath = null;
        string? markerRecoveryBackupPath = null;
        try
        {
            var pack = PackArchive.Open(packPath, expectedPackSha512);
            var root = ValidateInstallRoot(installRoot);
            SetOperationRelease(pack);
            var instancesRoot = Path.Combine(root, "instances");
            SafePath.EnsureNoReparsePoints(root, instancesRoot);
            var instanceName = $"test-pack-{pack.VersionId}-{pack.ArchiveSha512[..12].ToLowerInvariant()}";
            instancePath = SafePath.Resolve(instancesRoot, instanceName);
            RebindCurrentLog(root, pack, instancePath);
            var pending = ManagedFileTransaction.OpenPending(instancePath, _activeOperationLog);
            if (pending is not null)
            {
                var pendingManifest = pending.LoadTrustedManifest(pack);
                StartLog(root, pack, instancePath);
                using var pendingUse = InstanceUseGuard.Acquire(instancePath,
                    pendingManifest.Files.Where(file => !InitialConfiguration.IsInitialUserConfig(pack, file.Path)).Select(file => file.Path));
                pending.Recover(root, pack, pendingManifest, pendingUse, launcher);
            }
            ManagedFileTransaction.EnsureNoPendingUnderRoot(root, instancePath);
            var activeMarker = ReadActiveMarker(root);
            EnsureActiveMarkerCanChange(activeMarker, allowActiveMarkerRecovery);
            if (allowActiveMarkerRecovery && activeMarker.State is (ActiveMarkerState.Malformed or ActiveMarkerState.MissingTarget) &&
                activeMarker.Bytes is not null && !Directory.Exists(instancePath) &&
                !InstalledInstanceCatalog.Enumerate(root, AppContext.BaseDirectory).Any(entry => entry.IsTrusted))
            {
                StartLog(root, pack, instancePath);
                markerRecoveryBackupPath = BackupActiveMarker(root, activeMarker.Bytes);
                var markerPath = SafePath.Resolve(root, ".minepack-active.json");
                SafePath.EnsureNoReparsePoints(root, markerPath);
                var currentMarker = ReadActiveMarker(root);
                if (currentMarker.State != activeMarker.State || currentMarker.Bytes is null ||
                    !currentMarker.Bytes.AsSpan().SequenceEqual(activeMarker.Bytes))
                    throw new InstallerException("ACTIVE_MARKER_CONFLICT", LocalizedText.Get("ActiveMarkerCorrupt"));
                File.Delete(markerPath);
                LogSafe("active_marker_recovery_prepared", new { backupPath = markerRecoveryBackupPath });
            }
            if (Directory.Exists(instancePath))
            {
                SafePath.EnsureNoReparsePoints(instancesRoot, instancePath);
                if (File.Exists(Path.Combine(instancePath, InstallationManifest.FileName)))
                {
                    var existingManifest = InstallationManifest.Load(instancePath);
                    ValidateMatchesRelease(existingManifest, pack);
                    LogSafe("release_metadata_verified", new { managedFiles = existingManifest.Files.Count });
                    var composedMarkerRecovery = allowActiveMarkerRecovery &&
                        activeMarker.State is (ActiveMarkerState.Malformed or ActiveMarkerState.MissingTarget);
                    if (composedMarkerRecovery)
                    {
                        if (launcher is null)
                            throw new InstallerException("LAUNCHER_CONFIGURATION_REQUIRED",
                                LocalizedText.Get("ActivationServiceRequired"));
                        if (!launcher.MinecraftVersion.Equals(pack.MinecraftVersion, StringComparison.Ordinal))
                            throw new InstallerException("FABRIC_VERSION_MISMATCH", LocalizedText.Get("FabricVersionMismatch"));
                    }
                    instanceUse = InstanceUseGuard.Acquire(instancePath);
                    var restored = await RepairCoreAsync(instancePath, packPath, expectedPackSha512, progress, cancellationToken, launcher);
                    if (!restored.Success) return restored;
                    var existingRelativePath = Path.GetRelativePath(root, instancePath).Replace(Path.DirectorySeparatorChar, '/');
                    cancellationToken.ThrowIfCancellationRequested();
                    instanceUse.Recheck();
                    if (composedMarkerRecovery)
                    {
                        var recoveryLog = _activeOperationLog!;
                        await launcher!.ConfigureAndCommitActivationAsync(instancePath, instanceUse, () =>
                        {
                            instanceUse.Recheck();
                            markerBackupPath = WriteActive(root, existingRelativePath, allowRecovery: true,
                                expectedCurrent: activeMarker);
                            LogSafe("instance_activated", new { instancePath, markerBackupPath });
                            return Task.CompletedTask;
                        }, cancellationToken, recoveryLog).ConfigureAwait(false);
                    }
                    else
                    {
                        markerBackupPath = WriteActive(root, existingRelativePath, allowActiveMarkerRecovery);
                    }
                    try { Log("install_reactivated", new { instancePath }); }
                    catch { }
                    return markerBackupPath is null ? restored : restored with
                    { Message = restored.Message + " " + LocalizedText.Get("ActiveMarkerBackupSaved", markerBackupPath) };
                }
                instanceName += "-reinstall-" + Guid.NewGuid().ToString("N");
                instancePath = SafePath.Resolve(instancesRoot, instanceName);
            }

            instanceUse ??= InstanceUseGuard.Acquire(instancePath);
            root = PrepareRoot(root);
            StartLog(root, pack, instancePath);
            Log("install_started", new { packPath = Path.GetFileName(packPath) });
            Log("pack_validated", new { pack.VersionId, pack.MinecraftVersion, pack.FabricLoaderVersion, pack.ArchiveSha512 });
            Directory.CreateDirectory(instancesRoot);
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
                if (!InitialConfiguration.IsInitialUserConfig(pack, file.Path))
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
            var initialConfigurations = InitialConfiguration.Create(pack, stagingRoot);
            InitialConfiguration.WriteToRoot(stagingRoot, initialConfigurations);
            manifest.SaveAtomic(stagingRoot);
            Log("staging_verified", new { files = manifest.Files.Count, stagingRoot });

            cancellationToken.ThrowIfCancellationRequested();
            instanceUse.Recheck();
            Directory.CreateDirectory(Path.GetDirectoryName(instancePath)!);
            Directory.Move(stagingRoot, instancePath);
            movedToFinal = true;
            foreach (var file in manifest.Files)
                instanceUse.ProtectCommittedFile(SafePath.Resolve(instancePath, file.Path));
            var relativeInstancePath = Path.GetRelativePath(root, instancePath).Replace(Path.DirectorySeparatorChar, '/');
            cancellationToken.ThrowIfCancellationRequested();
            instanceUse.Recheck();
            markerBackupPath = WriteActive(root, relativeInstancePath, allowActiveMarkerRecovery);
            installCommitted = true;
            try { Log("install_committed", new { instancePath }); }
            catch { }
            try { progress?.Report(new InstallProgress("complete", LocalizedText.Get("InstallFilesComplete"), total, total)); }
            catch { }
            var installMessage = LocalizedText.Get("PackFilesInstalled");
            if (markerBackupPath is not null) installMessage += " " + LocalizedText.Get("ActiveMarkerBackupSaved", markerBackupPath);
            if (markerRecoveryBackupPath is not null) installMessage += " " + LocalizedText.Get("ActiveMarkerBackupSaved", markerRecoveryBackupPath);
            return new InstallResult(true, "OK", installMessage, instancePath, CurrentLogPath);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LogSafe("install_cancelled");
            return new InstallResult(false, "CANCELLED", AppendMarkerBackup(LocalizedText.Get("InstallCancelled"), markerRecoveryBackupPath),
                installCommitted ? instancePath : null, CurrentLogPath);
        }
        catch (InstallerException ex)
        {
            LogException("install_failed", ex);
            return new InstallResult(false, ex.Code, AppendMarkerBackup(ex.Message, markerRecoveryBackupPath),
                installCommitted ? instancePath : null, CurrentLogPath);
        }
        catch (Exception ex)
        {
            LogException("install_failed", ex);
            return new InstallResult(false, "INSTALL_FAILED", AppendMarkerBackup(LocalizedText.Get("InstallFailed"), markerRecoveryBackupPath),
                installCommitted ? instancePath : null, CurrentLogPath);
        }
        finally
        {
            instanceUse?.Dispose();
            if (movedToFinal && !installCommitted && instancePath is not null)
                TryDeleteDirectory(Path.GetDirectoryName(instancePath)!, instancePath);
            if (stagingRoot is not null) TryDeleteDirectory(root: Path.GetDirectoryName(Path.GetDirectoryName(stagingRoot)!)!, path: stagingRoot);
        }
    }

    public Task<InstallResult> RepairAsync(string instancePath, string packPath, string expectedPackSha512,
        IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default,
        FabricLauncherService? launcher = null, OperationLog? operationLog = null)
    {
        var ownsLog = operationLog is null;
        operationLog ??= new OperationLog("repair", null, instancePath);
        return GuardInstallResultAsync(() => RepairCoreAsync(instancePath, packPath, expectedPackSha512, progress,
                cancellationToken, launcher), cancellationToken, "REPAIR_FAILED", "RepairFailed", operationLog, ownsLog);
    }

    private async Task<InstallResult> RepairCoreAsync(string instancePath, string packPath, string expectedPackSha512,
        IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default,
        FabricLauncherService? launcher = null)
    {
        ManagedFileTransaction? transaction = null;
        InstanceUseGuard.Scope? instanceUse = null;
        var fullInstance = Path.TrimEndingDirectorySeparator(Path.GetFullPath(instancePath));
        var instancesRoot = Path.GetDirectoryName(fullInstance)!;
        var root = Path.GetDirectoryName(instancesRoot)!;
        InstallationManifest? manifest = null;
        PackArchive? pack = null;
        var repaired = 0;
        var restoredDefaults = new List<string>();
        var total = 0;
        try
        {
            EnsureNotVanilla(fullInstance);
            if (!Path.GetFileName(instancesRoot).Equals("instances", StringComparison.OrdinalIgnoreCase) ||
                !Directory.Exists(root) || !Directory.Exists(instancesRoot) || !Directory.Exists(fullInstance))
                throw new InstallerException("INSTANCE_NOT_FOUND", LocalizedText.Get("UninstallInstanceNotFound"));
            root = ValidateInstallRoot(root);
            SafePath.EnsureNoReparsePoints(root, fullInstance);
            ManagedFileTransaction.EnsureNoPendingUnderRoot(root, fullInstance);
            pack = PackArchive.Open(packPath, expectedPackSha512);
            RebindCurrentLog(root, pack, fullInstance);

            var pending = ManagedFileTransaction.OpenPending(fullInstance, _activeOperationLog);
            if (pending is not null)
            {
                manifest = pending.LoadTrustedManifest(pack);
                StartLog(root, pack, fullInstance);
                instanceUse = InstanceUseGuard.Acquire(fullInstance,
                    manifest.Files.Where(file => !InitialConfiguration.IsInitialUserConfig(pack, file.Path)).Select(file => file.Path));
                pending.Recover(root, pack, manifest, instanceUse, launcher);
                instanceUse.Dispose();
                instanceUse = null;
            }

            manifest = InstallationManifest.Load(fullInstance);
            ValidateMatchesRelease(manifest, pack);
            SetOperationRelease(pack);
            var managedFiles = manifest.Files.Where(file => !InitialConfiguration.IsInitialUserConfig(pack, file.Path)).ToList();
            instanceUse = InstanceUseGuard.Acquire(fullInstance, managedFiles.Select(file => file.Path));
            instanceUse.Recheck();
            StartLog(root, pack, fullInstance);
            transaction = ManagedFileTransaction.BeginRepair(root, fullInstance, pack, _activeOperationLog);
            var stagingRoot = transaction.StagingRoot;
            var overrideFiles = await pack.ExtractOverridesAsync(stagingRoot, cancellationToken);
            var initialConfigurations = InitialConfiguration.Create(pack, stagingRoot);
            InitialConfiguration.WriteToRoot(stagingRoot, initialConfigurations);
            var overrideByPath = overrideFiles.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
            var packFiles = pack.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
            var initialByPath = initialConfigurations.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
            total = managedFiles.Count + initialConfigurations.Count;
            var completed = 0;
            var changedManaged = new List<string>();

            foreach (var managed in managedFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = SafePath.Resolve(fullInstance, managed.Path);
                SafePath.EnsureNoReparsePoints(fullInstance, target);
                var currentHash = instanceUse.HashManagedFile(target);
                var matchesRelease = currentHash is not null && PackArchive.FixedTimeHashEquals(currentHash, managed.Sha512);
                LogSafe("managed_file_check", new { path = managed.Path, matchesRelease });
                if (matchesRelease)
                {
                    progress?.Report(new InstallProgress("repair", LocalizedText.Get("VerifiedFile", Path.GetFileName(target)), ++completed, total));
                    continue;
                }

                var staged = SafePath.Resolve(stagingRoot, managed.Path);
                if (!managed.IsOverride)
                {
                    var source = packFiles[managed.Path];
                    await _downloads.DownloadVerifiedAsync(source, stagingRoot, null, cancellationToken);
                    if (source.Size >= 0 && new FileInfo(staged).Length != source.Size)
                        throw new InstallerException("DOWNLOAD_SIZE_MISMATCH", LocalizedText.Get("DownloadSizeMismatch"));
                }
                else if (!overrideByPath.ContainsKey(managed.Path))
                {
                    throw new InstallerException("MANIFEST_INVALID", LocalizedText.Get("ManifestOverridesMismatch"));
                }
                else if (new FileInfo(staged).Length != overrideByPath[managed.Path].Size)
                {
                    throw new InstallerException("OVERRIDE_SIZE_MISMATCH", LocalizedText.Get("OverrideSizeMismatch"));
                }
                if (!PackArchive.FixedTimeHashEquals(PackArchive.HashFile(staged), managed.Sha512))
                    throw new InstallerException("REPAIR_HASH_MISMATCH", LocalizedText.Get("RepairedHashMismatch"));
                var originalSize = currentHash is null ? 0 : new FileInfo(target).Length;
                transaction.AddFile(managed.Path, currentHash, originalSize, managed.Sha512);
                changedManaged.Add(managed.Path);
                repaired++;
                progress?.Report(new InstallProgress("prepare", LocalizedText.Get("RestoredFile", Path.GetFileName(target)), ++completed, total));
            }

            foreach (var file in initialConfigurations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = SafePath.Resolve(fullInstance, file.Path);
                SafePath.EnsureNoReparsePoints(fullInstance, target);
                if (File.Exists(target)) continue;
                var staged = SafePath.Resolve(stagingRoot, file.Path);
                if (new FileInfo(staged).Length != file.Contents.Length)
                    throw new InstallerException("PACK_INVALID", LocalizedText.Get("PackArchiveInvalid"));
                var replacementHash = PackArchive.HashFile(staged);
                transaction.AddFile(file.Path, null, 0, replacementHash, createOnly: true);
                restoredDefaults.Add(file.Path);
            }

            cancellationToken.ThrowIfCancellationRequested();
            instanceUse.Recheck();
            transaction.MarkPrepared();
            transaction.EnsureCommitSpace();
            transaction.CommitFiles(instanceUse);
            transaction.MarkCompleted();
            var cleanupResidue = transaction.CleanupAfterCommit(launcher, pack, manifest, instanceUse);
            if (cleanupResidue is not null)
                LogSafe("transaction_cleanup_pending", new { recoveryPath = cleanupResidue });

            foreach (var path in changedManaged) LogSafe("repair_file", new { path, sha512 = managedFiles.First(file => file.Path == path).Sha512 });
            foreach (var path in restoredDefaults) LogSafe("repair_default", new { path });
            LogSafe("repair_complete", new { managedFilesRepaired = repaired, initialDefaultsRestored = restoredDefaults.Count, total });
            try
            {
                if (initialConfigurations.Count > 0)
                    progress?.Report(new InstallProgress("repair", LocalizedText.Get("RepairDefaultsRestored", restoredDefaults.Count), total, total));
                progress?.Report(new InstallProgress("complete", LocalizedText.Get("RepairAllHealthy"), total, total));
            }
            catch { }
            var messages = new List<string>();
            if (repaired > 0) messages.Add(LocalizedText.Get("RepairFilesRestored", repaired));
            if (restoredDefaults.Count > 0) messages.Add(LocalizedText.Get("RepairDefaultsRestored", restoredDefaults.Count));
            if (cleanupResidue is not null) messages.Add(LocalizedText.Get("TransactionCleanupResidue", cleanupResidue));
            var message = messages.Count == 0 ? LocalizedText.Get("RepairAllHealthy") : string.Join(" ", messages);
            return new InstallResult(true, "OK", message, fullInstance, CurrentLogPath);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var recovery = RollbackLiveTransaction(transaction, root, instanceUse, launcher, pack, manifest, fullInstance);
            LogSafe("repair_cancelled");
            return recovery ?? new InstallResult(false, "CANCELLED", LocalizedText.Get("RepairCancelled"), instancePath, CurrentLogPath);
        }
        catch (InstallerException ex)
        {
            var recovery = RollbackLiveTransaction(transaction, root, instanceUse, launcher, pack, manifest, fullInstance);
            LogException("repair_failed", ex);
            return recovery ?? new InstallResult(false, ex.Code, ex.Message, instancePath, CurrentLogPath);
        }
        catch (Exception ex)
        {
            var recovery = RollbackLiveTransaction(transaction, root, instanceUse, launcher, pack, manifest, fullInstance);
            LogException("repair_failed", ex);
            return recovery ?? new InstallResult(false, "REPAIR_FAILED", LocalizedText.Get("RepairFailed"), instancePath, CurrentLogPath);
        }
        finally { instanceUse?.Dispose(); }
    }

    public InstallationManifest ValidateUninstallTarget(string installRoot, string instancePath, string packPath,
        string expectedPackSha512) => ValidateUninstallTargetCore(installRoot, instancePath, packPath, expectedPackSha512).Manifest;

    public async Task<string?> ActivateExistingInstanceAsync(string installRoot, string instancePath, string packPath,
        string expectedPackSha512, FabricLauncherService launcher, bool allowActiveMarkerRecovery = false,
        CancellationToken cancellationToken = default, OperationLog? operationLog = null)
    {
        var ownsLog = operationLog is null;
        operationLog ??= new OperationLog("activate_instance", null, installRoot);
        try
        {
            var backup = await OperationGuard.RunAsync(async () =>
            {
                var previous = _activeOperationLog;
                _activeOperationLog = operationLog;
                try
                {
                    var root = ValidateInstallRoot(installRoot);
                    ManagedFileTransaction.EnsureNoPendingUnderRoot(root);
                    var target = ValidateUninstallTargetCore(root, instancePath, packPath, expectedPackSha512);
                    var pack = PackArchive.Open(packPath, expectedPackSha512);
                    if (!InstalledInstanceCatalog.TryGetRelease(pack.VersionId, out var knownRelease) ||
                        !PackArchive.FixedTimeHashEquals(pack.ArchiveSha512, knownRelease.ArchiveSha512) ||
                        !InstalledInstanceCatalog.IsExpectedInstanceDirectory(target.InstancePath, knownRelease))
                        throw new InstallerException("INSTANCE_UNTRUSTED", LocalizedText.Get("SelectedInstanceUntrusted"));
                    if (!launcher.MinecraftVersion.Equals(target.Manifest.MinecraftVersion, StringComparison.Ordinal))
                        throw new InstallerException("FABRIC_VERSION_MISMATCH", LocalizedText.Get("FabricVersionMismatch"));
                    StartLog(target.Root, pack, target.InstancePath);
                    using var instanceUse = InstanceUseGuard.Acquire(target.InstancePath,
                        target.Manifest.Files.Where(file => !InitialConfiguration.IsInitialUserConfig(pack, file.Path)).Select(file => file.Path));
                    instanceUse.Recheck();
                    var marker = ReadActiveMarker(target.Root);
                    EnsureActiveMarkerCanChange(marker, allowActiveMarkerRecovery);
                    var relative = Path.GetRelativePath(target.Root, target.InstancePath).Replace(Path.DirectorySeparatorChar, '/');
                    string? saved = null;
                    await launcher.ConfigureAndCommitActivationAsync(target.InstancePath, instanceUse, () =>
                    {
                        instanceUse.Recheck();
                        saved = WriteActive(target.Root, relative, allowActiveMarkerRecovery, marker);
                        LogSafe("instance_activated", new { instancePath = target.InstancePath, markerBackupPath = saved });
                        return Task.CompletedTask;
                    }, cancellationToken, operationLog).ConfigureAwait(false);
                    return saved;
                }
                finally { _activeOperationLog = previous; }
            }, cancellationToken).ConfigureAwait(false);
            if (ownsLog) operationLog.Complete("completed");
            return backup;
        }
        catch (Exception ex)
        {
            operationLog.WriteException("end", "instance_activation_failed", ex);
            if (ownsLog) operationLog.Complete("failed", (ex as InstallerException)?.Code);
            throw;
        }
    }

    public Task<InstallResult> UninstallAsync(string installRoot, string instancePath, string packPath,
        string expectedPackSha512, FabricLauncherService? launcher = null, OperationLog? operationLog = null)
    {
        var ownsLog = operationLog is null;
        operationLog ??= new OperationLog("uninstall", null, instancePath);
        return GuardInstallResultAsync(() => UninstallCoreAsync(installRoot, instancePath, packPath, expectedPackSha512,
                launcher), CancellationToken.None, "UNINSTALL_FAILED", "UninstallFailed", operationLog, ownsLog);
    }

    private async Task<InstallResult> UninstallCoreAsync(string installRoot, string instancePath, string packPath,
        string expectedPackSha512, FabricLauncherService? launcher)
    {
        ManagedFileTransaction? transaction = null;
        InstanceUseGuard.Scope? instanceUse = null;
        PackArchive? pack = null;
        InstallationManifest? manifest = null;
        string? root = null;
        var fullInstance = Path.TrimEndingDirectorySeparator(Path.GetFullPath(instancePath));
        try
        {
            root = ValidateInstallRoot(installRoot);
            var instancesRoot = Path.Combine(root, "instances");
            if (!Directory.Exists(root) || !Directory.Exists(instancesRoot) || !Directory.Exists(fullInstance) ||
                !Path.GetDirectoryName(fullInstance)!.Equals(instancesRoot, StringComparison.OrdinalIgnoreCase))
                throw new InstallerException("INSTANCE_NOT_FOUND", LocalizedText.Get("UninstallInstanceNotFound"));
            SafePath.EnsureNoReparsePoints(root, root);
            SafePath.EnsureNoReparsePoints(root, instancesRoot);
            SafePath.EnsureNoReparsePoints(root, fullInstance);
            pack = PackArchive.Open(packPath, expectedPackSha512);
            SetOperationRelease(pack);
            var pending = ManagedFileTransaction.OpenPending(fullInstance, _activeOperationLog);
            if (pending is not null)
            {
                manifest = pending.LoadTrustedManifest(pack);
                StartLog(root, pack, fullInstance);
                instanceUse = InstanceUseGuard.Acquire(fullInstance,
                    manifest.Files.Where(file => !InitialConfiguration.IsInitialUserConfig(pack, file.Path)).Select(file => file.Path));
                pending.Recover(root, pack, manifest, instanceUse, launcher);
                instanceUse.Dispose();
                instanceUse = null;
            }

            var target = ValidateUninstallTargetCore(root, fullInstance, packPath, expectedPackSha512);
            manifest = target.Manifest;
            var managedFiles = manifest.Files.Where(file => !InitialConfiguration.IsInitialUserConfig(pack, file.Path)).ToList();
            instanceUse = InstanceUseGuard.Acquire(target.InstancePath, managedFiles.Select(file => file.Path));
            instanceUse.Recheck();
            StartLog(target.Root, pack, target.InstancePath);
            LogSafe("release_metadata_verified", new { managedFiles = managedFiles.Count });
            transaction = ManagedFileTransaction.BeginUninstall(target.Root, target.InstancePath, pack, _activeOperationLog);
            transaction.SnapshotUninstallMetadata(target.Root);

            var receipt = launcher?.PrepareOwnProfileRemoval(target.InstancePath, transaction.Id, pack, manifest);
            if (receipt is not null)
            {
                transaction.SetProfileReceipt(receipt);
                launcher!.WriteOwnProfileRemovalBackup(receipt);
                transaction.MarkProfileBackupReady();
            }

            foreach (var file in managedFiles)
            {
                var path = SafePath.Resolve(target.InstancePath, file.Path);
                SafePath.EnsureNoReparsePoints(target.InstancePath, path);
                var hash = instanceUse.HashManagedFile(path);
                var size = hash is null ? 0 : new FileInfo(path).Length;
                transaction.AddFile(file.Path, hash, size, replacementSha512: null);
            }
            transaction.MarkPrepared();
            instanceUse.Recheck();
            transaction.EnsureCommitSpace();
            transaction.CommitFiles(instanceUse);
            transaction.MarkProfileCommitIntent();
            if (receipt is not null) launcher!.CommitOwnProfileRemoval(target.InstancePath, receipt, pack, manifest);
            transaction.MarkProfileCommitted();
            transaction.RemoveUninstallMetadata(target.Root);
            transaction.MarkCompleted();
            var cleanupResidue = transaction.CleanupAfterCommit(launcher, pack, manifest, instanceUse);
            if (cleanupResidue is not null)
                LogSafe("transaction_cleanup_pending", new { recoveryPath = cleanupResidue });

            try
            {
                if (!Directory.Exists(transaction.Folder))
                {
                    SafePath.EnsureNoReparsePoints(target.InstancePath, target.InstancePath);
                    if (Directory.Exists(target.InstancePath) && !Directory.EnumerateFileSystemEntries(target.InstancePath).Any())
                        Directory.Delete(target.InstancePath);
                    RemoveEmptyParents(target.Root, Path.GetDirectoryName(target.InstancePath)!);
                }
            }
            catch { }
            var remains = Directory.Exists(target.InstancePath);
            LogSafe("uninstall_complete", new { remainingDirectory = remains ? target.InstancePath : null });
            var uninstallMessage = LocalizedText.Get(remains ? "UninstallFilesRemovedWithData" : "UninstallFilesRemoved");
            if (cleanupResidue is not null) uninstallMessage += " " + LocalizedText.Get("TransactionCleanupResidue", cleanupResidue);
            return new InstallResult(true, "OK", uninstallMessage, remains ? target.InstancePath : null, CurrentLogPath);
        }
        catch (InstallerException ex)
        {
            var recovery = RollbackLiveTransaction(transaction, root ?? Path.GetFullPath(installRoot), instanceUse,
                launcher, pack, manifest, fullInstance);
            LogException("uninstall_failed", ex);
            return recovery ?? new InstallResult(false, ex.Code, ex.Message, instancePath, CurrentLogPath);
        }
        catch (Exception ex)
        {
            var recovery = RollbackLiveTransaction(transaction, root ?? Path.GetFullPath(installRoot), instanceUse,
                launcher, pack, manifest, fullInstance);
            LogException("uninstall_failed", ex);
            return recovery ?? new InstallResult(false, "UNINSTALL_FAILED", LocalizedText.Get("UninstallFailed"), instancePath, CurrentLogPath);
        }
        finally { instanceUse?.Dispose(); }
    }

    private async Task<InstallResult> GuardInstallResultAsync(Func<Task<InstallResult>> operation,
        CancellationToken cancellationToken, string failureCode, string failureMessageKey,
        OperationLog operationLog, bool ownsLog)
    {
        InstallResult result;
        try
        {
            result = await OperationGuard.RunAsync(async () =>
            {
                var previous = _activeOperationLog;
                _activeOperationLog = operationLog;
                try { return await operation(); }
                finally { _activeOperationLog = previous; }
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            operationLog.Write("end", "cancelled", "operation_cancelled");
            result = new InstallResult(false, "CANCELLED", LocalizedText.Get("InstallCancelled"), null, null);
        }
        catch (OperationCanceledException ex)
        {
            operationLog.WriteException("end", "operation_failed", ex);
            result = new InstallResult(false, failureCode, LocalizedText.Get(failureMessageKey), null, null);
        }
        catch (InstallerException ex)
        {
            operationLog.WriteException("end", "operation_failed", ex);
            result = new InstallResult(false, ex.Code, ex.Message, null, null);
        }
        if (ownsLog)
            operationLog.Complete(result.Success ? "completed" : result.Code == "CANCELLED" ? "cancelled" : "failed", result.Code);
        return result with { LogPath = operationLog.CurrentLogPath };
    }

    private static InstallResult? RollbackLiveTransaction(ManagedFileTransaction? transaction, string root,
        InstanceUseGuard.Scope? instanceUse, FabricLauncherService? launcher, PackArchive? pack,
        InstallationManifest? manifest, string? resultPath)
    {
        if (transaction is null || transaction.Phase is "Completed" or "RolledBack") return null;
        try
        {
            if (instanceUse is null || pack is null || manifest is null)
                throw new InstallerException("TRANSACTION_RECOVERY_REQUIRED", LocalizedText.Get("TransactionRecoveryRequired", transaction.Folder));
            transaction.Rollback(root, instanceUse, launcher, pack, manifest);
            return null;
        }
        catch
        {
            return new InstallResult(false, "TRANSACTION_RECOVERY_REQUIRED",
                LocalizedText.Get("TransactionRecoveryRequired", transaction.Folder), resultPath, null);
        }
    }

    private (string Root, string InstancePath, InstallationManifest Manifest)
        ValidateUninstallTargetCore(string installRoot, string instancePath, string packPath, string expectedPackSha512)
    {
        var root = ValidateInstallRoot(installRoot);
        var fullInstance = Path.TrimEndingDirectorySeparator(Path.GetFullPath(instancePath));
        ManagedFileTransaction.EnsureNoPendingUnderRoot(root);
        var instancesRoot = Path.Combine(root, "instances");
        if (!Directory.Exists(root) ||
            !string.Equals(Path.GetDirectoryName(fullInstance), instancesRoot, StringComparison.OrdinalIgnoreCase) ||
            !Directory.Exists(instancesRoot) || !Directory.Exists(fullInstance))
            throw new InstallerException("INSTANCE_NOT_FOUND", LocalizedText.Get("UninstallInstanceNotFound"));

        SafePath.EnsureNoReparsePoints(root, root);
        SafePath.EnsureNoReparsePoints(root, instancesRoot);
        SafePath.EnsureNoReparsePoints(root, fullInstance);
        var manifest = InstallationManifest.Load(fullInstance);
        var pack = PackArchive.Open(packPath, expectedPackSha512);
        ValidateMatchesRelease(manifest, pack);
        foreach (var file in manifest.Files)
        {
            if (InitialConfiguration.IsInitialUserConfig(pack, file.Path)) continue;
            var filePath = SafePath.Resolve(fullInstance, file.Path);
            SafePath.EnsureNoReparsePoints(fullInstance, filePath);
        }
        var manifestPath = SafePath.Resolve(fullInstance, InstallationManifest.FileName);
        SafePath.EnsureNoReparsePoints(fullInstance, manifestPath);
        var activeMarkerPath = SafePath.Resolve(root, ".minepack-active.json");
        SafePath.EnsureNoReparsePoints(root, activeMarkerPath);
        return (root, fullInstance, manifest);
    }

    private static string PrepareRoot(string installRoot)
    {
        var root = ValidateInstallRoot(installRoot);
        SafePath.EnsureNoReparsePoints(root, root);
        Directory.CreateDirectory(root);
        SafePath.EnsureNoReparsePoints(root, root);
        return root;
    }

    public static string ValidateInstallRoot(string installRoot)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installRoot));
        var volumeRoot = Path.GetPathRoot(root);
        if (string.Equals(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            volumeRoot?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new InstallerException("ROOT_UNSAFE", LocalizedText.Get("DriveRootUnsafe"));
        EnsureNotVanilla(root);
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

    private string? CurrentLogPath => _activeOperationLog?.CurrentLogPath;

    private void SetOperationRelease(PackArchive pack) =>
        _activeOperationLog?.SetRelease(pack.VersionId, pack.MinecraftVersion, pack.FabricLoaderVersion, pack.ArchiveSha512);

    private void StartLog(string root, PackArchive pack, string? targetPath)
    {
        SetOperationRelease(pack);
        _activeOperationLog?.BindValidatedRoot(root, targetPath);
    }

    private void RebindCurrentLog(string root, PackArchive pack, string targetPath)
    {
        if (_activeOperationLog?.CurrentLogPath is not null) StartLog(root, pack, targetPath);
    }

    private void Log(string name, object? detail = null) => _activeOperationLog?.Event(name, detail);

    private void LogSafe(string name, object? detail = null)
    {
        try { Log(name, detail); }
        catch { }
    }

    private void LogException(string name, Exception exception) =>
        _activeOperationLog?.WriteException("end", name, exception);

    private void OnDownloadDiagnostic(string name, object? detail) => LogSafe(name, detail);

    internal static ActiveMarkerSnapshot ReadActiveMarker(string root)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (!Directory.Exists(fullRoot)) return new ActiveMarkerSnapshot(ActiveMarkerState.Absent, null, null, null);
        try { SafePath.EnsureNoReparsePoints(fullRoot, fullRoot); }
        catch (InstallerException) { return new ActiveMarkerSnapshot(ActiveMarkerState.UnsafePath, null, null, null); }

        var marker = SafePath.Resolve(fullRoot, ".minepack-active.json");
        try { SafePath.EnsureNoReparsePoints(fullRoot, marker); }
        catch (InstallerException) { return new ActiveMarkerSnapshot(ActiveMarkerState.UnsafePath, null, null, null); }
        if (!File.Exists(marker)) return new ActiveMarkerSnapshot(ActiveMarkerState.Absent, null, null, null);

        byte[] bytes;
        try
        {
            using var stream = new FileStream(marker, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > 16 * 1024)
                return new ActiveMarkerSnapshot(ActiveMarkerState.UnsafePath, null, null, null);
            bytes = new byte[(int)stream.Length];
            stream.ReadExactly(bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ActiveMarkerSnapshot(ActiveMarkerState.UnsafePath, null, null, null);
        }

        int? schemaVersion = null;
        string? relativePath = null;
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
            var node = document.RootElement;
            if (node.ValueKind != JsonValueKind.Object ||
                !node.TryGetProperty(nameof(ActiveInstallation.SchemaVersion), out var schema) ||
                schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out var version))
                return new ActiveMarkerSnapshot(ActiveMarkerState.Malformed, null, null, bytes);
            schemaVersion = version;
            if (version != 1)
                return new ActiveMarkerSnapshot(ActiveMarkerState.UnsupportedSchema, null, version, bytes);
            if (node.EnumerateObject().Count() != 2 ||
                !node.TryGetProperty(nameof(ActiveInstallation.InstanceDirectory), out var directory) ||
                directory.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(directory.GetString()))
                return new ActiveMarkerSnapshot(ActiveMarkerState.Malformed, null, version, bytes);
            relativePath = directory.GetString();
        }
        catch (JsonException)
        {
            return new ActiveMarkerSnapshot(ActiveMarkerState.Malformed, null, null, bytes);
        }

        try
        {
            var path = SafePath.Resolve(fullRoot, relativePath!);
            var instancesRoot = Path.Combine(fullRoot, "instances");
            if (!Path.GetDirectoryName(path)!.Equals(instancesRoot, StringComparison.OrdinalIgnoreCase))
                return new ActiveMarkerSnapshot(ActiveMarkerState.UnsafePath, null, schemaVersion, bytes);
            SafePath.EnsureNoReparsePoints(fullRoot, instancesRoot);
            SafePath.EnsureNoReparsePoints(fullRoot, path);
            var manifestPath = SafePath.Resolve(path, InstallationManifest.FileName);
            SafePath.EnsureNoReparsePoints(path, manifestPath);
            if (!Directory.Exists(path) || !File.Exists(manifestPath))
                return new ActiveMarkerSnapshot(ActiveMarkerState.MissingTarget, null, schemaVersion, bytes);
            return new ActiveMarkerSnapshot(ActiveMarkerState.Valid, path, schemaVersion, bytes);
        }
        catch (InstallerException)
        {
            return new ActiveMarkerSnapshot(ActiveMarkerState.UnsafePath, null, schemaVersion, bytes);
        }
    }

    private static void EnsureActiveMarkerCanChange(ActiveMarkerSnapshot marker, bool allowRecovery)
    {
        if (marker.State is ActiveMarkerState.Absent or ActiveMarkerState.Valid) return;
        if (marker.State == ActiveMarkerState.UnsupportedSchema)
            throw new InstallerException("ACTIVE_MARKER_UNSUPPORTED", LocalizedText.Get("ActiveMarkerUnsupported"));
        if (marker.State == ActiveMarkerState.UnsafePath)
            throw new InstallerException("ACTIVE_MARKER_UNSAFE", LocalizedText.Get("ActiveMarkerUnsafe"));
        if (!allowRecovery || marker.Bytes is null || marker.SchemaVersion is not null and not 1)
            throw new InstallerException("ACTIVE_MARKER_RECOVERY_REQUIRED", LocalizedText.Get("ActiveMarkerRecoveryRequired"));
    }

    private static string AppendMarkerBackup(string message, string? backupPath) =>
        backupPath is null ? message : message + " " + LocalizedText.Get("ActiveMarkerBackupSaved", backupPath);

    private static string? WriteActive(string root, string relativeInstancePath, bool allowRecovery = false,
        ActiveMarkerSnapshot? expectedCurrent = null)
    {
        var marker = SafePath.Resolve(root, ".minepack-active.json");
        SafePath.EnsureNoReparsePoints(root, marker);
        var current = ReadActiveMarker(root);
        EnsureActiveMarkerCanChange(current, allowRecovery);
        if (expectedCurrent is not null &&
            (current.State != expectedCurrent.State || current.SchemaVersion != expectedCurrent.SchemaVersion ||
             !(current.Bytes is null && expectedCurrent.Bytes is null || current.Bytes is not null &&
                 expectedCurrent.Bytes is not null && current.Bytes.AsSpan().SequenceEqual(expectedCurrent.Bytes))))
            throw new InstallerException("ACTIVE_MARKER_CONFLICT", LocalizedText.Get("ActiveMarkerCorrupt"));
        string? backupPath = null;
        if (current.State is ActiveMarkerState.Malformed or ActiveMarkerState.MissingTarget)
        {
            backupPath = BackupActiveMarker(root, current.Bytes!);
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
            ManagedFileTransaction.Checkpoint("before-active-marker-replace");
            File.Move(temp, marker, overwrite: true);
        }
        finally
        {
            try { File.Delete(temp); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return backupPath;
    }

    private static string BackupActiveMarker(string root, byte[] originalBytes)
    {
        var marker = SafePath.Resolve(root, ".minepack-active.json");
        SafePath.EnsureNoReparsePoints(root, marker);
        if (!File.Exists(marker) || !File.ReadAllBytes(marker).AsSpan().SequenceEqual(originalBytes))
            throw new InstallerException("ACTIVE_MARKER_CONFLICT", LocalizedText.Get("ActiveMarkerCorrupt"));
        var backupDirectory = Path.Combine(root, "recovery");
        SafePath.EnsureNoReparsePoints(root, backupDirectory);
        Directory.CreateDirectory(backupDirectory);
        SafePath.EnsureNoReparsePoints(root, backupDirectory);
        var backup = Path.Combine(backupDirectory, "active-marker-" + Guid.NewGuid().ToString("N") + ".json");
        SafePath.EnsureNoReparsePoints(root, backup);
        using (var stream = new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.Write(originalBytes);
            stream.Flush(flushToDisk: true);
        }
        return backup;
    }

    private static async Task VerifyManagedFilesAsync(string root, IEnumerable<ManagedFile> files, CancellationToken cancellationToken,
        InstanceUseGuard.Scope? instanceUse = null)
    {
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = SafePath.Resolve(root, file.Path);
            SafePath.EnsureNoReparsePoints(root, path);
            var actualHash = instanceUse is null
                ? await Task.Run(() => File.Exists(path) ? PackArchive.HashFile(path) : null, cancellationToken)
                : await Task.Run(() => instanceUse.HashManagedFile(path), cancellationToken);
            if (actualHash is null || !PackArchive.FixedTimeHashEquals(actualHash, file.Sha512))
                throw new InstallerException("INSTALL_VERIFY_FAILED", LocalizedText.Get("InstallFileVerificationFailed", Path.GetFileName(path)));
        }
    }

    internal static void ValidateMatchesRelease(InstallationManifest manifest, PackArchive pack)
    {
        if (!PackArchive.FixedTimeHashEquals(manifest.PackArchiveSha512, pack.ArchiveSha512) ||
            manifest.PackVersion != pack.VersionId || manifest.MinecraftVersion != pack.MinecraftVersion ||
            manifest.FabricLoaderVersion != pack.FabricLoaderVersion)
            throw new InstallerException("RELEASE_MISMATCH", LocalizedText.Get("InstalledReleaseMismatch"));

        var packFiles = pack.Files.ToDictionary(x => x.Path, StringComparer.OrdinalIgnoreCase);
        var allOverrides = pack.Overrides.ToDictionary(x => x.Path, StringComparer.OrdinalIgnoreCase);
        var overrides = pack.Overrides.Where(x => !InitialConfiguration.IsInitialUserConfig(pack, x.Path))
            .ToDictionary(x => x.Path, StringComparer.OrdinalIgnoreCase);
        var expectedCount = packFiles.Count + overrides.Count;
        var hasLegacyIris = manifest.Files.Any(file => file.Path.Equals("config/iris.properties", StringComparison.OrdinalIgnoreCase));
        var allowsLegacyIris = InitialConfiguration.IsInitialUserConfig(pack, "config/iris.properties") &&
            allOverrides.ContainsKey("config/iris.properties");
        if (manifest.Files.Count != expectedCount + (allowsLegacyIris && hasLegacyIris ? 1 : 0))
            throw new InstallerException("MANIFEST_INVALID", LocalizedText.Get("ManifestReleaseMismatch"));
        foreach (var item in manifest.Files)
        {
            if (InitialConfiguration.IsInitialUserConfig(pack, item.Path))
            {
                if (!item.Path.Equals("config/iris.properties", StringComparison.OrdinalIgnoreCase) || !item.IsOverride ||
                    !allOverrides.TryGetValue(item.Path, out var iris) || !item.Path.Equals(iris.Path, StringComparison.Ordinal) ||
                    item.Size != iris.Size || item.Downloads.Length != 0 || !PackArchive.FixedTimeHashEquals(item.Sha512, iris.Sha512))
                    throw new InstallerException("MANIFEST_INVALID", LocalizedText.Get("ManifestUnknownOverride"));
                continue;
            }
            if (item.IsOverride)
            {
                if (!overrides.TryGetValue(item.Path, out var source) ||
                    !item.Path.Equals(source.Path, StringComparison.Ordinal) || item.Size != source.Size ||
                    item.Downloads.Length != 0 || !PackArchive.FixedTimeHashEquals(item.Sha512, source.Sha512))
                    throw new InstallerException("MANIFEST_INVALID", LocalizedText.Get("ManifestUnknownOverride"));
            }
            else if (!packFiles.TryGetValue(item.Path, out var source) ||
                     !item.Path.Equals(source.Path, StringComparison.Ordinal) ||
                     (source.Size >= 0 && item.Size != source.Size) ||
                     !PackArchive.FixedTimeHashEquals(item.Sha512, source.Sha512) ||
                     !item.Downloads.SequenceEqual(source.Downloads.Select(x => x.AbsoluteUri), StringComparer.Ordinal))
                throw new InstallerException("MANIFEST_INVALID", LocalizedText.Get("ManifestUnknownManagedFile"));
        }
    }

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

public enum ActiveMarkerState
{
    Absent,
    Valid,
    Malformed,
    MissingTarget,
    UnsupportedSchema,
    UnsafePath
}

public sealed record ActiveMarkerInspection(ActiveMarkerState State, string? InstancePath, int? SchemaVersion);

internal sealed record ActiveMarkerSnapshot(ActiveMarkerState State, string? InstancePath, int? SchemaVersion, byte[]? Bytes);
