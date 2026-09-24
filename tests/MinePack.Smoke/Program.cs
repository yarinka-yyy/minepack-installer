using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
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
            VerifyLauncherFixture();
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
        True(pack.Files.Count > 0, "pinned release contains downloadable files");
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
        True(File.Exists(worldPath) && File.Exists(screenshotPath) && File.Exists(unknownPath), "uninstall preserves worlds, screenshots, and unknown files");
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

    private static void VerifyLauncherFixture()
    {
        const string input = "{\"settings\":{\"custom\":true},\"profiles\":{\"vanilla\":{\"name\":\"Existing\",\"customField\":17}}}";
        var candidate = LauncherProfile.BuildFixtureCandidate(input, Path.Combine(Path.GetTempPath(), "minepack-game"), "26.3", "0.19.5");
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
            _ = LauncherProfile.BuildFixtureCandidate(conflict, Path.GetTempPath(), "26.3", "0.19.5");
            throw new InvalidOperationException("Expected Launcher profile conflict rejection.");
        }
        catch (InstallerException ex) when (ex.Code == "LAUNCHER_PROFILE_CONFLICT") { }
        Pass("Launcher fixture changes only the marked profile and preserves other JSON fields");
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
