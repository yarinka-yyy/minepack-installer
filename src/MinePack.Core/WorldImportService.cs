namespace MinePack.Core;

public sealed record WorldImportResult(int Imported, int Skipped);

public static class WorldImportService
{
    public static async Task<WorldImportResult> ImportAsync(string sourceFolder, string instancePath,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
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
        _ = InstallationManifest.Load(instance);
        var destination = Path.Combine(instance, "saves");
        if (source.Equals(destination, StringComparison.OrdinalIgnoreCase) ||
            source.StartsWith(destination + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            destination.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InstallerException("WORLDS_SAME_FOLDER", LocalizedText.Get("WorldFoldersOverlap"));
        SafePath.EnsureNoReparsePoints(source, source);
        SafePath.EnsureNoReparsePoints(instance, destination);
        Directory.CreateDirectory(destination);

        var imported = 0;
        var skipped = 0;
        foreach (var world in Directory.EnumerateDirectories(source))
        {
            cancellationToken.ThrowIfCancellationRequested();
            SafePath.EnsureNoReparsePoints(source, world);
            if (!File.Exists(Path.Combine(world, "level.dat"))) continue;
            var name = Path.GetFileName(world);
            var target = Path.Combine(destination, name);
            SafePath.EnsureNoReparsePoints(destination, target);
            if (Directory.Exists(target) || File.Exists(target))
            {
                skipped++;
                continue;
            }

            var lockFile = Path.Combine(world, "session.lock");
            if (File.Exists(lockFile))
            {
                try { using var probe = new FileStream(lockFile, FileMode.Open, FileAccess.Read, FileShare.None); }
                catch (IOException ex) { throw new InstallerException("WORLD_OPEN", LocalizedText.Get("WorldIsOpen", name), ex); }
            }

            var staging = Path.Combine(destination, ".minepack-import-" + Guid.NewGuid().ToString("N"));
            SafePath.EnsureNoReparsePoints(destination, staging);
            try
            {
                Directory.CreateDirectory(staging);
                foreach (var file in EnumerateSafeFiles(world))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var relative = Path.GetRelativePath(world, file);
                    var output = Path.Combine(staging, relative);
                    SafePath.EnsureNoReparsePoints(staging, output);
                    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                    await using var inputStream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read,
                        81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    await using var outputStream = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                        81920, FileOptions.Asynchronous);
                    await inputStream.CopyToAsync(outputStream, cancellationToken);
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (Directory.Exists(target) || File.Exists(target)) { skipped++; continue; }
                Directory.Move(staging, target);
                imported++;
                progress?.Report(LocalizedText.Get("WorldCopied", name));
            }
            finally
            {
                SafePath.EnsureNoReparsePoints(destination, staging);
                if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            }
        }
        return new WorldImportResult(imported, skipped);
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
}
