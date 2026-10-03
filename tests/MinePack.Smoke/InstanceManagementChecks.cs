using MinePack.Core;

internal static class InstanceManagementChecks
{
    public static async Task RunAsync(string smokeRoot)
    {
        var root = Path.Combine(smokeRoot, "instance cleanup fixtures");
        var installRoot = Path.Combine(root, "MinePack state");
        var instancesRoot = Path.Combine(installRoot, "instances");
        var profileRoot = Path.Combine(root, "official launcher profiles");
        Directory.CreateDirectory(instancesRoot);
        Directory.CreateDirectory(profileRoot);
        File.WriteAllText(Path.Combine(profileRoot, "launcher_profiles.json"), "{\"profiles\":{}}\n");

        var release = InstalledInstanceCatalog.KnownReleases.Single(item =>
            item.PackName == "Frontier" && item.PackVersion == Vanilla2PlusRelease.PackVersion);
        var targetName = InstanceDirectoryNaming.CreateBaseName(release);
        var target = Path.Combine(instancesRoot, targetName);
        var world = Path.Combine(target, "saves", "fixture-world");
        Directory.CreateDirectory(world);
        File.WriteAllText(Path.Combine(world, "level.dat"), "preserve world");
        File.WriteAllText(Path.Combine(world, "session.lock"), "fixture lock");
        File.WriteAllText(Path.Combine(target, "options.txt"), "enableVsync:true\n");
        var entry = new InstalledInstanceEntry(target, targetName, InstalledInstanceState.Residue, release, null);
        var sibling = Path.Combine(instancesRoot, targetName + "-02");
        Directory.CreateDirectory(sibling);
        File.WriteAllText(Path.Combine(sibling, "sentinel.txt"), "sibling untouched");

        var request = InstanceRemovalService.PrepareCleanupForTesting(entry, installRoot, AppContext.BaseDirectory, profileRoot);
        Require(request.Categories.Contains("UiCleanupCategorySaves") &&
                request.Categories.Contains("UiCleanupCategorySettings"),
            "cleanup summary identifies saved worlds and settings");

        File.WriteAllText(Path.Combine(target, "added-during-confirmation.txt"), "changed");
        var invoked = false;
        await ExpectCodeAsync(() => InstanceRemovalService.RemoveAsync(request, path =>
        {
            invoked = true;
            Directory.Move(path, Path.Combine(root, "unexpectedly moved"));
            return Task.CompletedTask;
        }), "INSTANCE_CLEANUP_CHANGED", "cleanup rechecks the folder snapshot after confirmation");
        Require(!invoked && Directory.Exists(target), "changed target is not moved");
        File.Delete(Path.Combine(target, "added-during-confirmation.txt"));

        var lockedPath = Path.Combine(target, "locked-by-another-process.txt");
        File.WriteAllText(lockedPath, "locked");
        var lockedEntry = new InstalledInstanceEntry(target, targetName, InstalledInstanceState.Residue, release, null);
        using (new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None))
            await ExpectCodeAsync(() => Task.Run(() => InstanceRemovalService.PrepareCleanupForTesting(
                    lockedEntry, installRoot, AppContext.BaseDirectory, profileRoot)),
                "INSTANCE_CLEANUP_LOCKED", "cleanup refuses an externally locked file");
        File.Delete(lockedPath);

        foreach (var gameDirectory in new[] { target, Path.Combine(target, "saves") })
        {
            File.WriteAllText(Path.Combine(profileRoot, "launcher_profiles.json"),
                System.Text.Json.JsonSerializer.Serialize(new { profiles = new { minepack = new { gameDir = gameDirectory } } }));
            await ExpectCodeAsync(() => Task.Run(() => InstanceRemovalService.PrepareCleanupForTesting(
                    entry, installRoot, AppContext.BaseDirectory, profileRoot)),
                "INSTANCE_CLEANUP_PROFILE_REFERENCE", "cleanup blocks a profile reference to the target or its descendant");
        }
        File.WriteAllText(Path.Combine(profileRoot, "launcher_profiles.json"), "{\"profiles\":{}}\n");

        using (InstanceUseGuard.UseProcessInspectionForTesting(
                   [(42, "java")], _ => $"java.exe net.minecraft.client.main.Main --gameDir \"{target}\""))
        {
            await ExpectCodeAsync(() => Task.Run(() => InstanceRemovalService.PrepareCleanupForTesting(
                    entry, installRoot, AppContext.BaseDirectory, profileRoot)),
                "GAME_IN_USE", "cleanup refuses a running game whose exact gameDir matches the selected target");
        }

        var transactionDirectory = target + ".minepack-transaction";
        Directory.CreateDirectory(transactionDirectory);
        File.WriteAllText(Path.Combine(transactionDirectory, "journal.json"), "{}");
        await ExpectCodeAsync(() => Task.Run(() => InstanceRemovalService.PrepareCleanupForTesting(
                entry, installRoot, AppContext.BaseDirectory, profileRoot)),
            "TRANSACTION_RECOVERY_REQUIRED", "cleanup refuses a pending transaction");
        Directory.Delete(transactionDirectory, recursive: true);

        File.WriteAllText(Path.Combine(installRoot, ".minepack-active.json"),
            System.Text.Json.JsonSerializer.Serialize(new { SchemaVersion = 1, InstanceDirectory = Path.Combine("instances", targetName) }));
        await ExpectCodeAsync(() => Task.Run(() => InstanceRemovalService.PrepareCleanupForTesting(
                entry, installRoot, AppContext.BaseDirectory, profileRoot)),
            "INSTANCE_CLEANUP_UNAVAILABLE", "cleanup refuses a stale active marker pointing to the selected residue");
        File.Delete(Path.Combine(installRoot, ".minepack-active.json"));

        var absentLauncherRoot = Path.Combine(root, "missing official launcher root");
        var prismOnlyRequest = InstanceRemovalService.PrepareCleanupForTesting(entry, installRoot,
            AppContext.BaseDirectory, absentLauncherRoot);
        Require(Directory.Exists(target), "a verified absent official profile root does not change files");
        await ExpectExceptionAsync<OperationCanceledException>(() => InstanceRemovalService.RemoveAsync(prismOnlyRequest,
                _ => Task.FromCanceled(new CancellationToken(true))),
            "canceled recycle request leaves the selected target in place");
        await ExpectExceptionAsync<IOException>(() => InstanceRemovalService.RemoveAsync(prismOnlyRequest,
                _ => Task.FromException(new IOException("synthetic Recycle Bin unavailable"))),
            "unavailable recycle request does not fall back to permanent deletion");
        Require(Directory.Exists(target) && File.ReadAllText(Path.Combine(sibling, "sentinel.txt")) == "sibling untouched",
            "cancel and Recycle Bin failure preserve target and neighboring instance");
        var recycleRoot = Path.Combine(root, "fixture recycle destination");
        Directory.CreateDirectory(recycleRoot);
        var movedPath = Path.Combine(recycleRoot, targetName);
        var cleanup = await InstanceRemovalService.RemoveAsync(prismOnlyRequest, path =>
        {
            Directory.Move(path, movedPath);
            return Task.CompletedTask;
        });
        Require(!Directory.Exists(target) && Directory.Exists(movedPath),
            "cleanup delegate receives only the selected folder and moves it intact");
        Require(File.ReadAllText(Path.Combine(sibling, "sentinel.txt")) == "sibling untouched",
            "moving the selected folder leaves its neighboring instance untouched");
        Require(File.ReadAllText(Path.Combine(movedPath, "options.txt")) == "enableVsync:true\n" &&
                File.ReadAllText(Path.Combine(movedPath, "saves", "fixture-world", "level.dat")) == "preserve world" &&
                File.Exists(Path.Combine(movedPath, "saves", "fixture-world", "session.lock")),
            "directory-move guard permits a world session.lock while preserving all user files");
        Require(!cleanup.OwnershipRecordRetained, "official residue cleanup has no Prism ownership record to retain");
        Console.WriteLine("PASS: residue cleanup snapshots, locked files, session.lock, and Prism-only launcher root fixture");
    }

    private static async Task ExpectCodeAsync(Func<Task> action, string expectedCode, string scenario)
    {
        try
        {
            await action();
        }
        catch (InstallerException ex) when (ex.Code == expectedCode)
        {
            Console.WriteLine("PASS: " + scenario);
            return;
        }
        throw new InvalidOperationException($"Expected {expectedCode}: {scenario}.");
    }

    private static async Task ExpectExceptionAsync<TException>(Func<Task> action, string scenario)
        where TException : Exception
    {
        try { await action(); }
        catch (TException)
        {
            Console.WriteLine("PASS: " + scenario);
            return;
        }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}: {scenario}.");
    }

    private static void Require(bool condition, string scenario)
    {
        if (!condition) throw new InvalidOperationException("Instance cleanup fixture failed: " + scenario);
    }
}
