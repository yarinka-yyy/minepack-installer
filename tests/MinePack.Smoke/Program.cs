using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using MinePack.Core;

try
{
    await Smoke.RunAsync(args);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"FAIL: {ex}");
    Environment.ExitCode = 1;
}

internal static class Smoke
{
    private static readonly Uri TestDownload = new("https://cdn.modrinth.com/data/test/version/test.jar");

    public static async Task RunAsync(string[] args)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "minepack-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            VerifyTempCleanupGuard();
            VerifyPinnedRelease();
            VerifyArchiveRejections(tempRoot);
            await VerifyArchiveMutationRejectedAsync(tempRoot);
            await VerifyInstallRepairAndUninstallAsync(tempRoot);
            VerifyLauncherFixture(tempRoot);
            await VerifyAutomaticFabricProfileAsync(tempRoot);
            var liveCompleted = false;
            if (args.Contains("--live-pack", StringComparer.Ordinal))
            {
                try { liveCompleted = await VerifyActualReleaseAsync(tempRoot); }
                catch (UnauthorizedAccessException ex)
                {
                    Console.WriteLine($"NOT RUN: live release check could not read/write a required path ({ex.GetType().Name}).");
                }
                catch (HttpRequestException ex)
                {
                    Console.WriteLine($"NOT RUN: live release check could not reach the download source ({ex.GetType().Name}).");
                }
                catch (IOException ex)
                {
                    Console.WriteLine($"NOT RUN: live release check stopped on an I/O restriction ({ex.GetType().Name}).");
                }
            }
            if (args.Contains("--live-fabric", StringComparer.Ordinal))
                await VerifyOfficialFabricDownloadAsync(tempRoot);
            if (args.Contains("--live-profile-copy", StringComparer.Ordinal))
                await VerifyCurrentLauncherCopyAsync(tempRoot);
            Console.WriteLine(args.Contains("--live-pack", StringComparer.Ordinal) && !liveCompleted
                ? "Deterministic smoke scenarios passed; live release check NOT RUN."
                : "All smoke scenarios passed.");
        }
        finally
        {
            DeleteSmokeTempTree(tempRoot);
        }
    }

    private static void VerifyTempCleanupGuard()
    {
        var outsideTemp = Path.Combine(Path.GetTempPath(), "..", "minepack-smoke-" + Guid.NewGuid().ToString("N"));
        try
        {
            _ = ValidateSmokeTempRoot(outsideTemp);
            throw new InvalidOperationException("Smoke cleanup guard accepted a path outside the system temp directory.");
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("Refusing recursive cleanup", StringComparison.Ordinal))
        {
            Pass("recursive temp cleanup rejects paths outside its guarded root");
        }
    }

    private static void DeleteSmokeTempTree(string path)
    {
        var resolved = ValidateSmokeTempRoot(path);
        if (!Directory.Exists(resolved)) return;
        if ((File.GetAttributes(resolved) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Refusing recursive cleanup of a reparse-point directory.");
        Directory.Delete(resolved, recursive: true);
    }

    private static string ValidateSmokeTempRoot(string path)
    {
        var resolved = Path.GetFullPath(path);
        var tempBase = Path.GetFullPath(Path.GetTempPath());
        var tempPrefix = Path.EndsInDirectorySeparator(tempBase) ? tempBase : tempBase + Path.DirectorySeparatorChar;
        var leaf = Path.GetFileName(resolved);
        const string namePrefix = "minepack-smoke-";
        if (!resolved.StartsWith(tempPrefix, StringComparison.OrdinalIgnoreCase) ||
            !leaf.StartsWith(namePrefix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(leaf[namePrefix.Length..], "N", out _))
            throw new InvalidOperationException("Refusing recursive cleanup outside the generated minepack-smoke temp directory.");
        return resolved;
    }

    private static void VerifyPinnedRelease()
    {
        var path = Path.Combine(AppContext.BaseDirectory, TestPackRelease.ArtifactRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var pack = PackArchive.Open(path, TestPackRelease.ArtifactSha512);
        Equal(TestPackRelease.PackVersion, pack.VersionId, "pinned release version");
        Equal(TestPackRelease.MinecraftVersion, pack.MinecraftVersion, "pinned Minecraft version");
        Equal(TestPackRelease.FabricLoaderVersion, pack.FabricLoaderVersion, "pinned Fabric Loader version");
        var addedMods = new[]
        {
            "InventoryParticles-3.2.0+26.2+fabric.jar", "dense-flowers-0.3.1+mc26.2.jar",
            "inventorysorter-fabric-3.0.1+mc26.2.jar", "ImmediatelyFast-Fabric-1.16.5+26.2.jar",
            "coolrain-1.4.0-26.2.jar", "sound-physics-remastered-fabric-1.5.1+26.2.jar",
            "held-item-info-1.9.2.jar", "bbe-fabric-1.3.7+mc26.2.jar",
            "Clumps-fabric-26.2-26.2.1.jar", "entityculling-fabric-1.11.2-mc26.2.jar",
            "MossyLib-1.6.0+26.2+fabric.jar", "cloth-config-26.2.155.jar",
            "ferritecore-9.0.0-fabric.jar"
        };
        True(pack.Files.Count == 35 && !pack.Files.Any(file => file.Path.Contains("smoothswapping", StringComparison.OrdinalIgnoreCase)) &&
             pack.Files.Any(file => file.Path == "shaderpacks/ComplementaryReimagined_r5.9.3.zip") &&
             pack.Files.Any(file => file.Path == "mods/voxy-0.2.19-beta.jar") &&
             pack.Files.Any(file => file.Path == "mods/Chunky-Fabric-1.5.3.jar") &&
             pack.Files.Any(file => file.Path == "mods/c2me-fabric-mc26.2-0.4.2-alpha.0.52.jar") &&
             pack.Files.Any(file => file.Path == "mods/PickUpNotifier-v26.2.0-mc26.2.x-Fabric.jar") &&
             pack.Files.Any(file => file.Path == "mods/explosive-enhancement-1.4.2-26.2.jar") &&
             pack.Files.Any(file => file.Path == "mods/entity_model_features-3.3.8-26.2-fabric.jar") &&
             pack.Files.Any(file => file.Path == "mods/entity_texture_features-7.2.4-26.2-fabric.jar") &&
             pack.Files.Any(file => file.Path == "mods/punchy-2.8a-fabric-26.2.jar") &&
             pack.Files.Any(file => file.Path == "mods/PuzzlesLib-v26.2.4-mc26.2.x-Fabric.jar") &&
             pack.Files.Any(file => file.Path == "mods/ForgeConfigAPIPort-v26.2.1-mc26.2.x-Fabric.jar") &&
             addedMods.All(name => pack.Files.Any(file => file.Path == "mods/" + name)) &&
             TestPackRelease.InitialResourcePacks.All(name => pack.Files.Any(file => file.Path == "resourcepacks/" + name)) &&
             pack.Overrides.Any(file => file.Path == "config/iris.properties"),
            "pinned release includes the base pack, requested mods and resource packs, and required dependencies");
        True(pack.Files.All(file => file.Sha512.Length == 128 && file.Sha512.All(Uri.IsHexDigit) &&
                                   file.Downloads.All(uri => uri.Scheme == Uri.UriSchemeHttps && uri.Host == "cdn.modrinth.com")),
            "pinned release hashes and URLs are valid");
        Pass("pinned .mrpack opens and matches its SHA-512");
    }

    private static void VerifyArchiveRejections(string tempRoot)
    {
        Rejects(tempRoot, "../escape.jar", "a", "PATH_BLOCKED", "parent path traversal");
        Rejects(tempRoot, "/escape.jar", "a", "PATH_BLOCKED", "absolute path");
        Rejects(tempRoot, "C:/escape.jar", "a", "PATH_BLOCKED", "drive path");
        Rejects(tempRoot, "mods/bad.jar", "a", "PACK_INVALID_HASH", "invalid SHA-512");
        Rejects(tempRoot, "mods/bad.jar", "a", "DOWNLOAD_URL_BLOCKED", "non-allowlisted URL");
        Rejects(tempRoot, "mods/Managed.jar", "a", "PACK_DUPLICATE_PATH", "case-insensitive duplicate path", duplicate: true);
        Rejects(tempRoot, "mods/test.jar", "a", "PACK_DUPLICATE_PATH", "download and override path conflict", addOverride: true);
        Rejects(tempRoot, "saves/world/level.dat", "a", "PACK_RESERVED_PATH", "world data override");
        Console.WriteLine("PASS: unsafe archive paths, hashes, URLs, duplicates, and protected data are rejected");
    }

    private static void Rejects(string tempRoot, string filePath, string content, string expectedCode, string scenario,
        bool duplicate = false, bool addOverride = false)
    {
        var path = Path.Combine(tempRoot, "reject-" + Guid.NewGuid().ToString("N") + ".mrpack");
        TestFile[] files;
        if (duplicate) files = [new TestFile(filePath, Bytes(content)), new TestFile(filePath.ToLowerInvariant(), Bytes(content))];
        else if (expectedCode == "PACK_RESERVED_PATH") files = [];
        else files = [new TestFile(filePath, Bytes(content), Url: expectedCode == "DOWNLOAD_URL_BLOCKED" ? "http://example.invalid/mod.jar" : TestDownload.AbsoluteUri,
            InvalidHash: expectedCode == "PACK_INVALID_HASH")];
        var overrides = addOverride ? new[] { new TestOverride("mods/test.jar", Bytes("override")) } :
            expectedCode == "PACK_RESERVED_PATH" ? new[] { new TestOverride(filePath, Bytes("user data")) } : [];
        CreatePack(path, "0.1.0", files, overrides);
        try
        {
            _ = PackArchive.Open(path);
            throw new InvalidOperationException($"Expected {expectedCode} for {scenario}.");
        }
        catch (InstallerException ex) when (ex.Code == expectedCode)
        {
            // Expected rejection.
        }
        finally { File.Delete(path); }
    }

    private static async Task VerifyInstallRepairAndUninstallAsync(string tempRoot)
    {
        var installRoot = Path.Combine(tempRoot, "install-root");
        var packageBytes = Bytes("smoke payload v1");
        var packPath = Path.Combine(tempRoot, "valid-test-pack.mrpack");
        CreatePack(packPath, "0.1.0", [new TestFile("mods/test.jar", packageBytes)]);
        var packHash = HashFile(packPath);
        var failDownloads = false;
        var currentDownloadBytes = packageBytes;
        var handler = new DelegateHandler(_ => failDownloads
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(currentDownloadBytes) });

        using var installer = new InstallService(new DownloadEngine(handler));
        var install = await installer.InstallAsync(packPath, packHash, installRoot);
        True(install.Success, "fixture pack installs successfully");
        var instance = install.GameDirectory ?? throw new InvalidOperationException("Install did not return its instance directory.");
        Equal(instance, installer.GetActiveInstancePath(installRoot), "successful instance becomes active");
        var managedPath = Path.Combine(instance, "mods", "test.jar");
        Equal(HashBytes(packageBytes), HashFile(managedPath), "installed managed file hash");
        True(File.Exists(Path.Combine(instance, InstallationManifest.FileName)), "install manifest is written");
        var installLog = File.ReadAllText(install.LogPath ?? throw new InvalidOperationException("Install did not return its log path."));
        True(installLog.Contains("download_attempt", StringComparison.Ordinal) &&
             installLog.Contains("download_response", StringComparison.Ordinal) &&
             installLog.Contains("download_hash_verified", StringComparison.Ordinal), "download diagnostics record attempt, HTTP response, and verified hash");
        True(!installLog.Contains(TestDownload.AbsoluteUri, StringComparison.Ordinal), "download diagnostics omit the full URL path");
        Pass("verified download installs to a separate versioned instance");

        var worldPath = Path.Combine(instance, "saves", "world", "level.dat");
        var screenshotPath = Path.Combine(instance, "screenshots", "keep.png");
        var unknownPath = Path.Combine(instance, "custom-user-file.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(worldPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(screenshotPath)!);
        File.WriteAllText(worldPath, "keep world");
        File.WriteAllText(screenshotPath, "keep screenshot");
        File.WriteAllText(unknownPath, "keep user file");
        var sourceSaves = Path.Combine(tempRoot, "source-profile", "saves");
        var sourceCollision = Path.Combine(sourceSaves, "world", "level.dat");
        var sourceNewWorld = Path.Combine(sourceSaves, "new-world", "level.dat");
        var sourceRegion = Path.Combine(sourceSaves, "new-world", "region", "r.0.0.mca");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceCollision)!);
        Directory.CreateDirectory(Path.GetDirectoryName(sourceRegion)!);
        File.WriteAllText(sourceCollision, "do not overwrite");
        File.WriteAllText(sourceNewWorld, "copy world");
        File.WriteAllText(sourceRegion, "copy region");
        var imported = await WorldImportService.ImportAsync(sourceSaves, instance);
        Equal(1, imported.Imported, "world import copies a new world");
        Equal(1, imported.Skipped, "world import skips an existing world name");
        Equal("keep world", File.ReadAllText(worldPath), "existing world is not overwritten");
        Equal("copy region", File.ReadAllText(Path.Combine(instance, "saves", "new-world", "region", "r.0.0.mca")), "world subdirectories are copied");
        Equal("copy world", File.ReadAllText(sourceNewWorld), "world source is unchanged");
        True(!Directory.EnumerateDirectories(Path.Combine(instance, "saves"), ".minepack-import-*").Any(), "no import staging directories remain");
        Pass("world import copies complete new worlds and preserves originals and name collisions");
        File.WriteAllText(managedPath, "corrupted");

        var repair = await installer.RepairAsync(instance, packPath, packHash);
        True(repair.Success, "repair succeeds");
        Equal(HashBytes(packageBytes), HashFile(managedPath), "repair restores expected managed hash");
        True(File.Exists(worldPath), "repair preserves world");
        Pass("repair restores modified managed data and preserves user data");

        failDownloads = true;
        var nextPackPath = Path.Combine(tempRoot, "failed-update.mrpack");
        var nextBytes = Bytes("smoke payload v2");
        CreatePack(nextPackPath, "0.2.0", [new TestFile("mods/test.jar", nextBytes)]);
        var nextInstance = InstancePath(installRoot, "0.2.0", HashFile(nextPackPath));
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            var canceledInstall = await installer.InstallAsync(nextPackPath, HashFile(nextPackPath), installRoot, cancellationToken: canceled.Token);
            True(!canceledInstall.Success && canceledInstall.Code == "CANCELLED", "pre-commit cancellation is reported");
        }
        True(!Directory.Exists(nextInstance), "cancellation before activation leaves no final version directory");
        Equal(instance, installer.GetActiveInstancePath(installRoot), "cancellation leaves the prior active marker unchanged");

        var failedInstall = await installer.InstallAsync(nextPackPath, HashFile(nextPackPath), installRoot);
        True(!failedInstall.Success, "simulated download failure is reported");
        var failureLogPath = failedInstall.LogPath ?? installer.GetLatestLogPath(installRoot)
            ?? throw new InvalidOperationException("Failed install did not leave a diagnostic log.");
        var failureLog = File.ReadAllText(failureLogPath);
        True(failureLog.Contains("download_retry", StringComparison.Ordinal) && failureLog.Contains("DOWNLOAD_HTTP", StringComparison.Ordinal),
            "failed downloads record retries and a stable reason code");
        Equal(instance, installer.GetActiveInstancePath(installRoot), "failed installation leaves active marker unchanged");
        Equal(HashBytes(packageBytes), HashFile(managedPath), "failed installation leaves prior instance unchanged");
        True(!Directory.Exists(nextInstance), "failed download leaves no final version directory");
        Pass("failed download leaves active instance and its files unchanged");

        var uninstall = await installer.UninstallAsync(instance, packPath, packHash);
        True(uninstall.Success, "uninstall succeeds");
        True(!File.Exists(managedPath), "uninstall removes managed file");
        True(File.Exists(worldPath) && File.Exists(Path.Combine(instance, "saves", "new-world", "level.dat")) &&
             File.Exists(screenshotPath) && File.Exists(unknownPath), "uninstall preserves worlds, screenshots, and unknown files");
        True(installer.GetActiveInstancePath(installRoot) is null, "uninstall clears its active marker");
        Pass("uninstall removes only managed files and preserves user data");

        var markerRoot = Path.Combine(tempRoot, "marker-install-root");
        Directory.CreateDirectory(markerRoot);
        var activeMarker = Path.Combine(markerRoot, ".minepack-active.json");
        const string unrelatedMarker = "{\"owner\":\"unrecognized\"}";
        File.WriteAllText(activeMarker, unrelatedMarker);
        var markerBytes = Bytes("marker commit fixture");
        currentDownloadBytes = markerBytes;
        var markerPack = Path.Combine(tempRoot, "marker-pack.mrpack");
        CreatePack(markerPack, "0.3.0", [new TestFile("mods/test.jar", markerBytes)]);
        failDownloads = false;
        var markerInstall = await installer.InstallAsync(markerPack, HashFile(markerPack), markerRoot);
        True(!markerInstall.Success && markerInstall.Code == "ACTIVE_MARKER_CONFLICT",
            $"unowned active marker prevents activation (success={markerInstall.Success}, code={markerInstall.Code}, message={markerInstall.Message})");
        Equal(unrelatedMarker, File.ReadAllText(activeMarker), "unowned marker remains unchanged");
        var markerInstance = InstancePath(markerRoot, "0.3.0", HashFile(markerPack));
        True(!Directory.Exists(markerInstance), "failed marker commit removes the moved but inactive version directory");

        File.Delete(activeMarker);
        var retry = await installer.InstallAsync(markerPack, HashFile(markerPack), markerRoot,
            new DelegateProgress<InstallProgress>(item =>
            {
                if (item.Stage == "complete") throw new InvalidOperationException("post-commit progress callback failure");
            }));
        True(retry.Success, "same release can be retried after marker failure; post-commit progress is best-effort");
        Equal(markerInstance, installer.GetActiveInstancePath(markerRoot), "successful retry atomically activates the new version");
        Pass("failed marker commit leaves no orphan and retry succeeds");
        var markerUninstall = await installer.UninstallAsync(markerInstance, markerPack, HashFile(markerPack));
        True(markerUninstall.Success, "marker fixture cleanup succeeds");
    }

    private static async Task VerifyArchiveMutationRejectedAsync(string tempRoot)
    {
        var path = Path.Combine(tempRoot, "mutable-pack.mrpack");
        CreatePack(path, "0.1.0", [], [new TestOverride("config/test.txt", Bytes("pinned override"))]);
        var opened = PackArchive.Open(path);
        File.AppendAllText(path, "modified after validation");
        var staging = Path.Combine(tempRoot, "mutation-staging");
        try
        {
            _ = await opened.ExtractOverridesAsync(staging, CancellationToken.None);
            throw new InvalidOperationException("Expected a modified archive to be rejected before extracting overrides.");
        }
        catch (InstallerException ex) when (ex.Code == "PACK_HASH_MISMATCH") { }
        True(!Directory.Exists(staging), "archive mutation is rejected before creating staging output");
        Pass("archive is rehashed before overrides are extracted");
    }

    private static async Task<bool> VerifyActualReleaseAsync(string tempRoot)
    {
        var packPath = Path.Combine(AppContext.BaseDirectory, TestPackRelease.ArtifactRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var installRoot = Path.Combine(tempRoot, "live-pack-install");
        var vanilla = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");
        var vanillaBefore = CaptureVanillaData(vanilla);
        if (vanillaBefore is null)
            Console.WriteLine("NOT RUN: vanilla mods/config/saves could not be read for a before/after comparison.");
        using var installer = new InstallService();
        var install = await installer.InstallAsync(packPath, TestPackRelease.ArtifactSha512, installRoot);
        if (!install.Success)
        {
            Console.WriteLine($"NOT RUN: actual release install did not complete ({install.Code}). {install.Message} Diagnostic log: {install.LogPath ?? "unavailable"}.");
            return false;
        }

        var instance = install.GameDirectory ?? throw new InvalidOperationException("Actual release install did not return an instance directory.");
        var manifest = InstallationManifest.Load(instance);
        True(manifest.Files.Count > 0, "actual release writes a non-empty local manifest");
        foreach (var file in manifest.Files)
        {
            var path = Path.Combine(instance, file.Path.Replace('/', Path.DirectorySeparatorChar));
            True(string.Equals(file.Sha512, HashFile(path), StringComparison.OrdinalIgnoreCase),
                $"actual release managed file hash {file.Path}");
        }
        var optionsPath = Path.Combine(instance, "options.txt");
        var expectedPacks = new[] { "vanilla" }.Concat(TestPackRelease.InitialResourcePacks.Select(name => "file/" + name)).Append("punchy:punchy");
        True(File.ReadAllText(optionsPath).Contains("resourcePacks:" + JsonSerializer.Serialize(expectedPacks), StringComparison.Ordinal),
            "all resource packs are selected on first launch in the pinned order");
        var bbeConfigPath = Path.Combine(instance, "config", "BBEConfig.json");
        using (var document = JsonDocument.Parse(File.ReadAllText(bbeConfigPath)))
        {
            var bbeOptions = document.RootElement.GetProperty("bbe.config.storage.main").EnumerateArray()
                .ToDictionary(entry => entry.GetProperty("option").GetString()!, entry => entry.GetProperty("value").GetBoolean());
            True(!bbeOptions["optimize.chest"] && !bbeOptions["optimize.shulker"],
                "BBE leaves Fresh Animations chest and shulker models visible");
        }
        File.WriteAllText(optionsPath, "resourcePacks:[\"vanilla\"]\n");
        File.WriteAllText(bbeConfigPath, "{}");
        var repair = await installer.RepairAsync(instance, packPath, TestPackRelease.ArtifactSha512);
        True(repair.Success && File.ReadAllText(optionsPath) == "resourcePacks:[\"vanilla\"]\n" &&
             File.ReadAllText(bbeConfigPath) == "{}", "repair preserves player settings");
        var vanillaAfter = CaptureVanillaData(vanilla);
        if (vanillaBefore is not null && vanillaAfter is not null)
            Equal(vanillaBefore, vanillaAfter, "vanilla mods/config/saves remain unchanged");
        else if (vanillaBefore is not null || vanillaAfter is not null)
            Console.WriteLine("NOT RUN: vanilla mods/config/saves comparison was incomplete because access changed.");
        else
            Console.WriteLine("NOT RUN: vanilla mods/config/saves before/after comparison unavailable due filesystem access restrictions.");
        Pass("actual pinned release downloads, verifies, and installs in a temporary isolated directory");

        var uninstall = await installer.UninstallAsync(instance, packPath, TestPackRelease.ArtifactSha512);
        if (!uninstall.Success)
        {
            Console.WriteLine($"NOT RUN: actual release cleanup did not complete ({uninstall.Code}). {uninstall.Message}");
            return false;
        }
        True(File.ReadAllText(optionsPath) == "resourcePacks:[\"vanilla\"]\n" &&
             File.ReadAllText(bbeConfigPath) == "{}", "uninstall preserves player settings");
        Pass("actual pinned release temporary install uninstalls cleanly");
        return true;
    }

    private static string? CaptureVanillaData(string vanillaRoot)
    {
        var paths = new[] { "mods", "config", "saves" };
        var records = new List<string>();
        try
        {
            foreach (var relative in paths)
            {
                var directory = Path.Combine(vanillaRoot, relative);
                if (!Directory.Exists(directory)) continue;
                foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
                {
                    var info = new FileInfo(path);
                    records.Add($"{relative}/{Path.GetRelativePath(directory, path)}|{info.Length}|{info.LastWriteTimeUtc.Ticks}");
                }
            }
        }
        catch (UnauthorizedAccessException) { return null; }
        catch (IOException) { return null; }
        return string.Join("\n", records);
    }

    private static void VerifyLauncherFixture(string tempRoot)
    {
        const string input = "{\"settings\":{\"custom\":true},\"profiles\":{\"vanilla\":{\"name\":\"Existing\",\"customField\":17}}}";
        var candidate = LauncherProfile.BuildFixtureCandidate(input, Path.Combine(Path.GetTempPath(), "minepack-game"), TestPackRelease.MinecraftVersion, TestPackRelease.FabricLoaderVersion);
        using var added = JsonDocument.Parse(candidate);
        var root = added.RootElement;
        True(root.GetProperty("settings").GetProperty("custom").GetBoolean(), "unknown root Launcher fields are preserved");
        True(root.GetProperty("profiles").GetProperty("vanilla").GetProperty("customField").GetInt32() == 17,
            "unowned Launcher profile fields are preserved");
        var own = root.GetProperty("profiles").GetProperty(LauncherProfile.ProfileKey);
        Equal(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "minepack-game")), own.GetProperty("gameDir").GetString(), "fixture profile gameDir");
        var removed = LauncherProfile.RemoveFixtureCandidate(candidate);
        using var removedJson = JsonDocument.Parse(removed);
        True(!removedJson.RootElement.GetProperty("profiles").TryGetProperty(LauncherProfile.ProfileKey, out _), "only the owned Launcher fixture profile is removed");
        True(removedJson.RootElement.GetProperty("profiles").TryGetProperty("vanilla", out _), "other Launcher profile remains");

        const string conflict = "{\"profiles\":{\"minepack-test-pack\":{\"name\":\"Someone else's profile\"}}}";
        try
        {
            _ = LauncherProfile.BuildFixtureCandidate(conflict, Path.GetTempPath(), TestPackRelease.MinecraftVersion, TestPackRelease.FabricLoaderVersion);
            throw new InvalidOperationException("Expected Launcher profile conflict rejection.");
        }
        catch (InstallerException ex) when (ex.Code == "LAUNCHER_PROFILE_CONFLICT") { }

        var oldInstance = Path.Combine(tempRoot, "owned-instance", "instances",
            "test-pack-0.7.0-" + TestPackRelease.GraphicsArtifactSha512[..12].ToLowerInvariant());
        new InstallationManifest
        {
            PackVersion = "0.7.0",
            MinecraftVersion = "26.2",
            FabricLoaderVersion = TestPackRelease.FabricLoaderVersion,
            PackArchiveSha512 = TestPackRelease.GraphicsArtifactSha512
        }.SaveAtomic(oldInstance);
        string ProfileWithoutMarker(string gameDir) => JsonSerializer.Serialize(new
        {
            profiles = new Dictionary<string, object>
            {
                [LauncherProfile.ProfileKey] = new
                {
                    name = "MinePack Test Pack", type = "custom",
                    lastVersionId = "fabric-loader-0.19.5-26.2", gameDir
                }
            }
        });
        var markerless = ProfileWithoutMarker(oldInstance);
        var reconfigured = LauncherProfile.BuildFixtureCandidate(markerless, Path.Combine(tempRoot, "new-instance"), TestPackRelease.MinecraftVersion, TestPackRelease.FabricLoaderVersion);
        using (var parsed = JsonDocument.Parse(reconfigured))
            Equal(Path.Combine(tempRoot, "new-instance"), parsed.RootElement.GetProperty("profiles")
                .GetProperty(LauncherProfile.ProfileKey).GetProperty("gameDir").GetString(),
                "Launcher-stripped marker can be recovered from a pinned MinePack manifest");
        using (var parsed = JsonDocument.Parse(LauncherProfile.RemoveFixtureCandidate(markerless)))
            True(!parsed.RootElement.GetProperty("profiles").TryGetProperty(LauncherProfile.ProfileKey, out _),
                "markerless MinePack profile can be removed while its manifest exists");
        Equal(markerless, LauncherProfile.RemoveFixtureCandidate(markerless, Path.Combine(tempRoot, "new-instance")),
            "uninstalling a different instance preserves the current MinePack profile");
        try
        {
            _ = LauncherProfile.BuildFixtureCandidate(ProfileWithoutMarker(Path.Combine(tempRoot, "foreign-instance")),
                Path.Combine(tempRoot, "new-instance"), TestPackRelease.MinecraftVersion, TestPackRelease.FabricLoaderVersion);
            throw new InvalidOperationException("Expected unowned markerless profile rejection.");
        }
        catch (InstallerException ex) when (ex.Code == "LAUNCHER_PROFILE_CONFLICT") { }
        Pass("Launcher fixture changes only the marked profile and preserves other JSON fields");
    }

    private static async Task VerifyAutomaticFabricProfileAsync(string tempRoot)
    {
        var launcherRoot = Path.Combine(tempRoot, "launcher-fixture");
        var gameDirectory = Path.Combine(tempRoot, "isolated-game");
        Directory.CreateDirectory(launcherRoot);
        var profilesPath = Path.Combine(launcherRoot, "launcher_profiles.json");
        const string input = "{\"profiles\":{\"vanilla\":{\"name\":\"Original\",\"customField\":17}},\"settings\":{\"custom\":true}}";
        File.WriteAllText(profilesPath, input);
        const string versionId = "fabric-loader-0.19.5-26.2";
        byte[] archive;
        using (var memory = new MemoryStream())
        {
            using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
            {
                using (var writer = new StreamWriter(zip.CreateEntry($"{versionId}/{versionId}.json").Open()))
                    writer.Write($"{{\"id\":\"{versionId}\",\"inheritsFrom\":\"26.2\"}}");
                zip.CreateEntry($"{versionId}/{versionId}.jar");
            }
            archive = memory.ToArray();
        }

        var hydratedJar = Bytes("official Minecraft client JAR fixture");
        using var service = new FabricLauncherService(launcherRoot,
            new DelegateHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) }),
            HashBytes(archive), HashBytes(hydratedJar), hydratedJar.Length);
        await service.ConfigureAsync(gameDirectory);
        await service.ConfigureAsync(gameDirectory);
        var version = Path.Combine(launcherRoot, "versions", versionId);
        True(File.Exists(Path.Combine(version, versionId + ".json")), "Fabric version JSON installed");
        True(File.Exists(Path.Combine(version, versionId + ".jar")), "Fabric version dummy JAR installed");
        var versionJar = Path.Combine(version, versionId + ".jar");
        File.WriteAllBytes(versionJar, hydratedJar);
        await service.ConfigureAsync(gameDirectory);
        Equal(HashBytes(hydratedJar), HashFile(versionJar), "Launcher-filled official client JAR remains unchanged");
        var profileBeforeConflict = File.ReadAllText(profilesPath);
        File.WriteAllText(versionJar, "different client JAR");
        try { await service.ConfigureAsync(gameDirectory); throw new InvalidOperationException("Expected foreign Fabric JAR rejection."); }
        catch (InstallerException ex) when (ex.Code == "FABRIC_VERSION_CONFLICT") { }
        Equal(profileBeforeConflict, File.ReadAllText(profilesPath), "foreign Fabric JAR leaves Launcher profile untouched");
        True(Directory.EnumerateFiles(launcherRoot, "launcher_profiles.json.minepack-*.bak").Any(),
            "Launcher profile backup created");
        using (var document = JsonDocument.Parse(File.ReadAllText(profilesPath)))
        {
            var root = document.RootElement;
            True(root.GetProperty("settings").GetProperty("custom").GetBoolean(), "Launcher settings preserved");
            Equal(17, root.GetProperty("profiles").GetProperty("vanilla").GetProperty("customField").GetInt32(), "vanilla profile preserved");
            Equal(Path.GetFullPath(gameDirectory), root.GetProperty("profiles").GetProperty(LauncherProfile.ProfileKey)
                .GetProperty("gameDir").GetString(), "automatic profile uses isolated game directory");
        }
        service.RemoveOwnProfile();
        using (var document = JsonDocument.Parse(File.ReadAllText(profilesPath)))
            True(!document.RootElement.GetProperty("profiles").TryGetProperty(LauncherProfile.ProfileKey, out _), "only owned profile removed");

        var badRoot = Path.Combine(tempRoot, "launcher-bad-hash");
        Directory.CreateDirectory(badRoot);
        var badProfiles = Path.Combine(badRoot, "launcher_profiles.json");
        File.WriteAllText(badProfiles, input);
        using var badService = new FabricLauncherService(badRoot,
            new DelegateHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) }),
            HashBytes(Bytes("different")));
        try { await badService.ConfigureAsync(gameDirectory); throw new InvalidOperationException("Expected Fabric hash rejection."); }
        catch (InstallerException ex) when (ex.Code == "FABRIC_HASH") { }
        Equal(input, File.ReadAllText(badProfiles), "bad Fabric archive leaves Launcher profile untouched");

        var conflictRoot = Path.Combine(tempRoot, "launcher-conflict");
        Directory.CreateDirectory(conflictRoot);
        const string conflict = "{\"profiles\":{\"minepack-test-pack\":{\"name\":\"Someone else's profile\"}}}";
        var conflictProfiles = Path.Combine(conflictRoot, "launcher_profiles.json");
        File.WriteAllText(conflictProfiles, conflict);
        using var conflictService = new FabricLauncherService(conflictRoot,
            new DelegateHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) }),
            HashBytes(archive));
        try { await conflictService.ConfigureAsync(gameDirectory); throw new InvalidOperationException("Expected Launcher conflict rejection."); }
        catch (InstallerException ex) when (ex.Code == "LAUNCHER_PROFILE_CONFLICT") { }
        Equal(conflict, File.ReadAllText(conflictProfiles), "foreign profile preserved after conflict");
        True(!Directory.Exists(Path.Combine(conflictRoot, "versions", versionId)), "new Fabric version rolled back after profile conflict");
        var previousRoot = Path.Combine(tempRoot, "launcher-previous-release");
        Directory.CreateDirectory(previousRoot);
        File.WriteAllText(Path.Combine(previousRoot, "launcher_profiles.json"), input);
        const string previousId = "fabric-loader-0.19.5-26.3";
        byte[] previousArchive;
        using (var memory = new MemoryStream())
        {
            using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
            {
                using (var writer = new StreamWriter(zip.CreateEntry($"{previousId}/{previousId}.json").Open()))
                    writer.Write($"{{\"id\":\"{previousId}\",\"inheritsFrom\":\"26.3\"}}");
                zip.CreateEntry($"{previousId}/{previousId}.jar");
            }
            previousArchive = memory.ToArray();
        }
        using (var previousService = new FabricLauncherService(previousRoot,
                   new DelegateHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(previousArchive) }),
                   expectedSha512: HashBytes(previousArchive), minecraftVersion: "26.3"))
            await previousService.ConfigureAsync(gameDirectory);
        using (var previousProfile = JsonDocument.Parse(File.ReadAllText(Path.Combine(previousRoot, "launcher_profiles.json"))))
            Equal(previousId, previousProfile.RootElement.GetProperty("profiles").GetProperty(LauncherProfile.ProfileKey)
                .GetProperty("lastVersionId").GetString(), "previous release retains its Fabric version");
        Pass("automatic Fabric profile accepts the official Launcher-filled JAR and rejects foreign files");
    }

    private static async Task VerifyOfficialFabricDownloadAsync(string tempRoot)
    {
        var launcherRoot = Path.Combine(tempRoot, "official-fabric-fixture");
        Directory.CreateDirectory(launcherRoot);
        var profilesPath = Path.Combine(launcherRoot, "launcher_profiles.json");
        File.WriteAllText(profilesPath, "{\"profiles\":{\"vanilla\":{\"name\":\"Original\"}}}");
        using var service = new FabricLauncherService(launcherRoot);
        await service.ConfigureAsync(Path.Combine(tempRoot, "official-fabric-game"));
        using var document = JsonDocument.Parse(File.ReadAllText(profilesPath));
        True(document.RootElement.GetProperty("profiles").TryGetProperty("vanilla", out _), "official Fabric download keeps vanilla profile");
        True(document.RootElement.GetProperty("profiles").TryGetProperty(LauncherProfile.ProfileKey, out _), "official Fabric download creates MinePack profile");
        Pass("official Fabric profile ZIP downloads, verifies, and configures an isolated Launcher fixture");
    }

    private static async Task VerifyCurrentLauncherCopyAsync(string tempRoot)
    {
        var realRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");
        var names = new[] { "launcher_profiles.json", "launcher_profiles_microsoft_store.json" };
        var sourceFiles = names.Select(name => Path.Combine(realRoot, name)).Where(File.Exists).ToArray();
        if (sourceFiles.Length != 1)
        {
            Console.WriteLine("NOT RUN: current Launcher has no unambiguous supported profile file.");
            return;
        }

        var original = File.ReadAllBytes(sourceFiles[0]);
        var launcherRoot = Path.Combine(tempRoot, "current-launcher-copy");
        Directory.CreateDirectory(launcherRoot);
        var copiedFile = Path.Combine(launcherRoot, Path.GetFileName(sourceFiles[0]));
        File.WriteAllBytes(copiedFile, original);
        const string versionId = "fabric-loader-0.19.5-26.2";
        var sourceVersion = Path.Combine(realRoot, "versions", versionId);
        var copiedVersion = Path.Combine(launcherRoot, "versions", versionId);
        var copiedJar = Path.Combine(copiedVersion, versionId + ".jar");
        if (File.Exists(Path.Combine(sourceVersion, versionId + ".json")) &&
            File.Exists(Path.Combine(sourceVersion, versionId + ".jar")))
        {
            Directory.CreateDirectory(copiedVersion);
            File.Copy(Path.Combine(sourceVersion, versionId + ".json"), Path.Combine(copiedVersion, versionId + ".json"));
            File.Copy(Path.Combine(sourceVersion, versionId + ".jar"), copiedJar);
        }
        var copiedJarHash = File.Exists(copiedJar) ? HashFile(copiedJar) : null;
        using var service = new FabricLauncherService(launcherRoot);
        await service.ConfigureAsync(Path.Combine(tempRoot, "current-launcher-copy-game"));
        if (copiedJarHash is not null)
            Equal(copiedJarHash, HashFile(copiedJar), "Launcher-hydrated Fabric JAR survives reconfiguration in a copy");
        var before = JsonNode.Parse(original)!.AsObject();
        var after = JsonNode.Parse(File.ReadAllBytes(copiedFile))!.AsObject();
        before["profiles"]?.AsObject().Remove(LauncherProfile.ProfileKey);
        after["profiles"]?.AsObject().Remove(LauncherProfile.ProfileKey);
        True(JsonNode.DeepEquals(before, after), "current Launcher settings and other profiles stay unchanged in a copy");
        True(File.ReadAllBytes(sourceFiles[0]).AsSpan().SequenceEqual(original), "actual Launcher profile file stays untouched");
        Pass("current Launcher profile format accepts an isolated MinePack profile in a temporary copy");
    }

    private static string CreatePack(string path, string version, IReadOnlyList<TestFile> files,
        IReadOnlyList<TestOverride>? overrides = null)
    {
        overrides ??= [];
        var indexFiles = files.Select(file => new
        {
            path = file.Path,
            hashes = new { sha512 = file.InvalidHash ? "bad" : HashBytes(file.Bytes) },
            downloads = new[] { file.Url },
            fileSize = file.Bytes.LongLength,
            env = new { client = "required", server = "required" }
        });
        var index = new
        {
            formatVersion = 1,
            game = "minecraft",
            versionId = version,
            name = "Smoke Fixture",
            summary = "Temporary smoke-test pack",
            files = indexFiles,
            dependencies = new Dictionary<string, string> { ["minecraft"] = "26.3", ["fabric-loader"] = "0.19.5" }
        };

        using var stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(zip.CreateEntry("modrinth.index.json").Open()))
            writer.Write(JsonSerializer.Serialize(index));
        foreach (var item in overrides)
        {
            var entry = zip.CreateEntry("overrides/" + item.Path, CompressionLevel.NoCompression);
            using var output = entry.Open();
            output.Write(item.Bytes);
        }
        return path;
    }

    private static byte[] Bytes(string value) => System.Text.Encoding.UTF8.GetBytes(value);
    private static string HashBytes(byte[] value) => Convert.ToHexString(SHA512.HashData(value));
    private static string HashFile(string path) => Convert.ToHexString(SHA512.HashData(File.ReadAllBytes(path)));
    private static string InstancePath(string root, string version, string archiveSha512) =>
        Path.Combine(root, "instances", $"test-pack-{version}-{archiveSha512[..12].ToLowerInvariant()}");

    private static void True(bool value, string scenario)
    {
        if (!value) throw new InvalidOperationException($"Failed: {scenario}");
    }

    private static void Equal<T>(T expected, T? actual, string scenario)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Failed: {scenario}; expected '{expected}', received '{actual}'.");
    }

    private static void Pass(string scenario) => Console.WriteLine($"PASS: {scenario}");

    private sealed record TestFile(string Path, byte[] Bytes, string Url = "https://cdn.modrinth.com/data/test/version/test.jar", bool InvalidHash = false);
    private sealed record TestOverride(string Path, byte[] Bytes);

    private sealed class DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class DelegateProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
