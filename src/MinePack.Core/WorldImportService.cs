namespace MinePack.Core;

public sealed record WorldImportResult(int Imported, int SkippedExisting, int SkippedMissingLock,
    int SkippedLockedOrUnverified)
{
    public int Skipped => SkippedExisting + SkippedMissingLock + SkippedLockedOrUnverified;
}

public sealed class WorldImportFailureException(string code, string message, WorldImportResult partialResult,
    Exception innerException) : InstallerException(code, message, innerException)
{
    public WorldImportResult PartialResult { get; } = partialResult;
}

public sealed class WorldImportCancelledException(WorldImportResult partialResult, OperationCanceledException innerException)
    : OperationCanceledException(innerException.Message, innerException, innerException.CancellationToken)
{
    public WorldImportResult PartialResult { get; } = partialResult;
}

public static class WorldImportService
{
    private static readonly AsyncLocal<Action<string>?> CheckpointForTesting = new();

    internal static IDisposable UseCheckpointForTesting(Action<string> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var previous = CheckpointForTesting.Value;
        CheckpointForTesting.Value = callback;
        return new CallbackScope(() => CheckpointForTesting.Value = previous);
    }

    public static async Task<WorldImportResult> ImportAsync(string sourceFolder, string instancePath,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default,
        OperationLog? operationLog = null)
    {
        var ownsLog = operationLog is null;
        operationLog ??= new OperationLog("world_import", null, instancePath);
        operationLog.Write("import", "started", "world_import_started");
        try
        {
            var result = await OperationGuard.RunAsync(() => ImportCoreAsync(sourceFolder, instancePath, progress,
                cancellationToken, operationLog), cancellationToken).ConfigureAwait(false);
            operationLog.Write("import", "completed", "world_import_summary", new
            {
                result.Imported, result.SkippedExisting, result.SkippedMissingLock, result.SkippedLockedOrUnverified
            });
            if (ownsLog) operationLog.Complete("completed");
            return result;
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            var partial = ex is WorldImportCancelledException canceled ? canceled.PartialResult : null;
            operationLog.WriteException("import", "world_import_cancelled", ex, "cancelled");
            if (partial is not null)
                operationLog.Write("import", "partial", "world_import_partial", new
                {
                    partial.Imported, partial.SkippedExisting, partial.SkippedMissingLock, partial.SkippedLockedOrUnverified
                });
            if (ownsLog) operationLog.Complete("cancelled", "CANCELLED");
            throw;
        }
        catch (Exception ex)
        {
            operationLog.WriteException("import", "world_import_failed", ex);
            if (ex is WorldImportFailureException failure)
                operationLog.Write("import", "partial", "world_import_partial", new
                {
                    failure.PartialResult.Imported, failure.PartialResult.SkippedExisting,
                    failure.PartialResult.SkippedMissingLock, failure.PartialResult.SkippedLockedOrUnverified
                });
            if (ownsLog) operationLog.Complete("failed", (ex as InstallerException)?.Code);
            throw;
        }
    }

    private static async Task<WorldImportResult> ImportCoreAsync(string sourceFolder, string instancePath,
        IProgress<string>? progress, CancellationToken cancellationToken, OperationLog operationLog)
    {
        var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceFolder));
        if (!source.EndsWith(Path.DirectorySeparatorChar + "saves", StringComparison.OrdinalIgnoreCase) &&
            Directory.Exists(Path.Combine(source, "saves"))) source = Path.Combine(source, "saves");
        if (!Directory.Exists(source))
            throw new InstallerException("WORLDS_SOURCE_MISSING", LocalizedText.Get("WorldSourceMissing"));

        var instance = Path.GetFullPath(instancePath);
        var vanilla = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft"));
        if (instance.Equals(vanilla, StringComparison.OrdinalIgnoreCase) ||
            instance.StartsWith(vanilla + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InstallerException("WORLDS_TARGET_UNSAFE", LocalizedText.Get("WorldTargetVanilla"));
        ManagedFileTransaction.EnsureNoPendingForInstance(instance);
        var manifest = InstallationManifest.Load(instance);
        try
        {
            operationLog.SetRelease(manifest.PackVersion, manifest.MinecraftVersion, manifest.FabricLoaderVersion,
                manifest.PackArchiveSha512);
            var instancesRoot = Path.GetDirectoryName(instance);
            var installRoot = instancesRoot is null ? null : Path.GetDirectoryName(instancesRoot);
            if (installRoot is not null && instancesRoot is not null)
            {
                var validatedRoot = InstallService.ValidateInstallRoot(installRoot);
                if (validatedRoot.Equals(installRoot, StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileName(instancesRoot).Equals("instances", StringComparison.OrdinalIgnoreCase) &&
                    Directory.Exists(installRoot) && Directory.Exists(instancesRoot))
                {
                    SafePath.EnsureNoReparsePoints(installRoot, instancesRoot);
                    SafePath.EnsureNoReparsePoints(installRoot, instance);
                    operationLog.BindValidatedRoot(installRoot, instance);
                }
            }
        }
        catch { }
        var destination = Path.Combine(instance, "saves");
        if (source.Equals(destination, StringComparison.OrdinalIgnoreCase) ||
            source.StartsWith(destination + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            destination.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InstallerException("WORLDS_SAME_FOLDER", LocalizedText.Get("WorldFoldersOverlap"));
        SafePath.EnsureNoReparsePoints(source, source);
        SafePath.EnsureNoReparsePoints(instance, destination);
        using var instanceUse = InstanceUseGuard.Acquire(instance);
        instanceUse.Recheck();
        Directory.CreateDirectory(destination);

        var imported = 0;
        var skippedExisting = 0;
        var skippedMissingLock = 0;
        var skippedLockedOrUnverified = 0;
        WorldImportResult PartialResult() => new(imported, skippedExisting, skippedMissingLock, skippedLockedOrUnverified);
        try
        {
            foreach (var world in Directory.EnumerateDirectories(source)
                         .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                SafePath.EnsureNoReparsePoints(source, world);
                if (!File.Exists(Path.Combine(world, "level.dat"))) continue;
                var name = Path.GetFileName(world);
                var target = Path.Combine(destination, name);
                SafePath.EnsureNoReparsePoints(destination, target);
                if (Directory.Exists(target) || File.Exists(target))
                {
                    skippedExisting++;
                    operationLog.Write("import", "skipped", "world_import_skipped", new { reason = "existing" });
                    continue;
                }

                FileStream? sourceWorldLock;
                try { sourceWorldLock = OpenSourceWorldLock(world); }
                catch (InstallerException ex) when (ex.Code == "WORLD_OPEN")
                {
                    skippedLockedOrUnverified++;
                    operationLog.Write("import", "skipped", "world_import_skipped", new { reason = "locked_or_unverified" });
                    continue;
                }
                if (sourceWorldLock is null)
                {
                    skippedMissingLock++;
                    operationLog.Write("import", "skipped", "world_import_skipped", new { reason = "missing_lock" });
                    continue;
                }

                using (sourceWorldLock)
                {
                    var staging = Path.Combine(destination, ".minepack-import-" + Guid.NewGuid().ToString("N"));
                    SafePath.EnsureNoReparsePoints(destination, staging);
                    try
                    {
                        Directory.CreateDirectory(staging);
                        foreach (var file in EnumerateSafeFiles(world))
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var relative = Path.GetRelativePath(world, file);
                            if (relative.Equals("session.lock", StringComparison.OrdinalIgnoreCase))
                                continue;
                            var output = Path.Combine(staging, relative);
                            SafePath.EnsureNoReparsePoints(staging, output);
                            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                            await using var outputStream = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                                81920, FileOptions.Asynchronous);
                            await using var inputStream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read,
                                81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
                            await inputStream.CopyToAsync(outputStream, cancellationToken);
                            CheckpointForTesting.Value?.Invoke("after-world-file-copy:" + name);
                        }
                        cancellationToken.ThrowIfCancellationRequested();
                        instanceUse.Recheck();
                        var lockOutput = Path.Combine(staging, "session.lock");
                        SafePath.EnsureNoReparsePoints(staging, lockOutput);
                        sourceWorldLock.Position = 0;
                        await using (var outputStream = new FileStream(lockOutput, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                                         81920, FileOptions.Asynchronous))
                        {
                            await sourceWorldLock.CopyToAsync(outputStream, cancellationToken);
                        }
                        cancellationToken.ThrowIfCancellationRequested();
                        if (Directory.Exists(target) || File.Exists(target)) { skippedExisting++; continue; }
                        Directory.Move(staging, target);
                        imported++;
                        operationLog.Write("import", "completed", "world_imported", new { imported });
                        progress?.Report(LocalizedText.Get("WorldCopied", name));
                    }
                    finally
                    {
                        SafePath.EnsureNoReparsePoints(destination, staging);
                        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
                    }
                }
            }
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            operationLog.Write("import", "partial", "world_import_cancelled", new
            {
                imported, skippedExisting, skippedMissingLock, skippedLockedOrUnverified
            });
            throw new WorldImportCancelledException(PartialResult(), ex);
        }
        catch (InstallerException ex)
        {
            operationLog.WriteException("import", "world_import_failed", ex);
            operationLog.Write("import", "partial", "world_import_partial", new
            {
                imported, skippedExisting, skippedMissingLock, skippedLockedOrUnverified
            });
            throw new WorldImportFailureException(ex.Code, ex.Message, PartialResult(), ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            operationLog.WriteException("import", "world_import_failed", ex);
            operationLog.Write("import", "partial", "world_import_partial", new
            {
                imported, skippedExisting, skippedMissingLock, skippedLockedOrUnverified
            });
            throw new WorldImportFailureException("WORLD_IMPORT_IO", LocalizedText.Get("WorldImportFailedStatus"),
                PartialResult(), ex);
        }
        return PartialResult();
    }

    private static FileStream? OpenSourceWorldLock(string world)
    {
        var lockFile = Path.Combine(world, "session.lock");
        SafePath.EnsureNoReparsePoints(world, lockFile);
        try { _ = File.GetAttributes(lockFile); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InstallerException("WORLD_OPEN", LocalizedText.Get("WorldLockUnavailable"), ex);
        }
        try { return new FileStream(lockFile, FileMode.Open, FileAccess.Read, FileShare.None); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch (IOException ex) { throw new InstallerException("WORLD_OPEN", LocalizedText.Get("WorldLockUnavailable"), ex); }
        catch (UnauthorizedAccessException ex) { throw new InstallerException("WORLD_OPEN", LocalizedText.Get("WorldLockUnavailable"), ex); }
    }

    private static IEnumerable<string> EnumerateSafeFiles(string world)
    {
        var directories = new Stack<string>();
        directories.Push(world);
        while (directories.TryPop(out var directory))
        {
            SafePath.EnsureNoReparsePoints(world, directory);
            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                SafePath.EnsureNoReparsePoints(world, child);
                directories.Push(child);
            }
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                SafePath.EnsureNoReparsePoints(world, file);
                yield return file;
            }
        }
    }

    private sealed class CallbackScope(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
