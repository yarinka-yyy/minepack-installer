using System.IO.Compression;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
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
    private static readonly string[] YungsJarNames =
    [
        "YungsApi-26.2-Fabric-6.1.3-minepack.1.jar",
        "YungsBetterDesertTemples-26.2-Fabric-5.1.1-minepack.1.jar",
        "YungsBetterDungeons-26.2-Fabric-6.1.1-minepack.1.jar",
        "YungsBetterJungleTemples-26.2-Fabric-4.1.1-minepack.1.jar",
        "YungsBetterMineshafts-26.2-Fabric-6.1.1-minepack.1.jar",
        "YungsBetterNetherFortresses-26.2-Fabric-4.1.1-minepack.1.jar",
        "YungsBetterStrongholds-26.2-Fabric-6.1.1-minepack.1.jar"
    ];
    private static readonly string[] NewForkJarNames =
    [
        "nyfsspiders-fabric-26.2-3.0.0-minepack.1.jar",
        "worldplaytime-1.2.5-minepack.1-26.2-FABRIC.jar"
    ];

    public static async Task RunAsync(string[] args)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "minepack-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            VerifyTempCleanupGuard();
            VerifyPinnedRelease();
            VerifyLocalization();
            VerifyArchiveRejections(tempRoot);
            await VerifyArchiveMutationRejectedAsync(tempRoot);
            await VerifyInstallRepairAndUninstallAsync(tempRoot);
            await VerifyVariantSwitchingAsync(tempRoot);
            VerifyLauncherFixture(tempRoot);
            await VerifyLauncherLifecycleAsync(tempRoot);
            await VerifyAutomaticFabricProfileAsync(tempRoot);
            var liveCompleted = false;
            if (args.Contains("--live-pack", StringComparer.Ordinal))
            {
                try
                {
                    liveCompleted = await VerifyActualReleaseAsync(tempRoot);
                    if (liveCompleted) liveCompleted = await VerifyActualVanilla2PlusAsync(tempRoot);
                }
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
        var newMods = new[]
        {
            "xaeroworldmap-fabric-26.2-1.46.1.jar", "AdvancementPlaques-26.2-fabric-1.7.2.jar",
            "cherishedworlds-fabric-17.0.0+26.2.jar", "leafmealone-1.2.0.jar",
            "InvMove-0.9.6+26.2-Fabric.jar", "Iceberg-26.2-fabric-1.4.2.2.jar",
            "modmenu-20.0.2.jar", "placeholder-api-3.1.0-beta.1+26.2.jar"
        };
        var sharedNewMods = new[]
        {
            "SubtleEffects-fabric-26.2-1.14.3.jar",
            "fzzy_config-0.7.6+26.2.jar", "fabric-language-kotlin-1.14.1+kotlin.2.4.20.jar"
        };
        True(pack.Files.Count == 46 && sharedNewMods.All(name => pack.Files.Any(file => file.Path == "mods/" + name)) &&
             !pack.Files.Any(file => file.Path.Contains("smoothswapping", StringComparison.OrdinalIgnoreCase)) &&
             !pack.Files.Any(file => file.Path.Contains("firstperson", StringComparison.OrdinalIgnoreCase) ||
                                          file.Path.Contains("notenoughanimations", StringComparison.OrdinalIgnoreCase)) &&
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
             addedMods.Concat(newMods).All(name => pack.Files.Any(file => file.Path == "mods/" + name)) &&
             TestPackRelease.InitialResourcePacks.All(name => pack.Files.Any(file => file.Path == "resourcepacks/" + name)) &&
             pack.Overrides.Any(file => file.Path == "config/iris.properties"),
            "pinned release includes the base pack, requested mods and resource packs, and required dependencies");
        True(pack.Files.All(file => file.Sha512.Length == 128 && file.Sha512.All(Uri.IsHexDigit) &&
                                    file.Downloads.All(uri => uri.Scheme == Uri.UriSchemeHttps && uri.Host == "cdn.modrinth.com")),
             "pinned release hashes and URLs are valid");
        var priorVanillaPlusPath = Path.Combine(AppContext.BaseDirectory, "releases", "test-pack", TestPackRelease.PriorArtifactFileName);
        var priorVanillaPlus = PackArchive.Open(priorVanillaPlusPath, TestPackRelease.PriorArtifactSha512);
        True(priorVanillaPlus.VersionId == "0.10.0" && priorVanillaPlus.Files.Count == 43 &&
             priorVanillaPlus.Files.All(oldFile => pack.Files.Any(file => file.Path == oldFile.Path &&
                 file.Sha512 == oldFile.Sha512 && file.Downloads.SequenceEqual(oldFile.Downloads))) &&
             pack.Files.Where(file => !priorVanillaPlus.Files.Any(oldFile => oldFile.Path == file.Path))
                 .Select(file => file.Path).ToHashSet(StringComparer.Ordinal)
                 .SetEquals(sharedNewMods.Select(name => "mods/" + name)),
            "Vanilla Plus keeps the other 0.15.0 additions while preserving 0.10.0");
        var smoothVanillaPlus = PackArchive.Open(
            Path.Combine(AppContext.BaseDirectory, "releases", "test-pack", TestPackRelease.SmoothArtifactFileName),
            TestPackRelease.SmoothArtifactSha512);
        True(smoothVanillaPlus.VersionId == "0.15.0" && smoothVanillaPlus.Files.Count == 47 &&
             smoothVanillaPlus.Files.Where(file => !file.Path.Contains("smoothswapping", StringComparison.OrdinalIgnoreCase))
                 .All(oldFile => pack.Files.Any(file => file.Path == oldFile.Path && file.Sha512 == oldFile.Sha512 &&
                     file.Downloads.SequenceEqual(oldFile.Downloads))),
            "Vanilla Plus removes only Smooth Swapping from its previous pinned release");
        var originalVanilla2PlusPath = Path.Combine(AppContext.BaseDirectory, "releases", "vanilla-2-plus", Vanilla2PlusRelease.OriginalArtifactFileName);
        var originalVanilla2Plus = PackArchive.Open(originalVanilla2PlusPath, Vanilla2PlusRelease.OriginalArtifactSha512);
        True(originalVanilla2Plus.VersionId == "0.11.0" && originalVanilla2Plus.Files.Count == 48 &&
             priorVanillaPlus.Files.All(baseFile => originalVanilla2Plus.Files.Any(file => file.Path == baseFile.Path &&
                 file.Sha512 == baseFile.Sha512 && file.Downloads.SequenceEqual(baseFile.Downloads))),
            "original Vanilla 2 Plus archive remains pinned and preserves Vanilla Plus 0.10.0");
        var legacyVanilla2PlusPath = Path.Combine(AppContext.BaseDirectory, "releases", "vanilla-2-plus", Vanilla2PlusRelease.LegacyArtifactFileName);
        var legacyVanilla2Plus = PackArchive.Open(legacyVanilla2PlusPath, Vanilla2PlusRelease.LegacyArtifactSha512);
        True(legacyVanilla2Plus.VersionId == "0.12.0" && legacyVanilla2Plus.Files.Count == 54 &&
             originalVanilla2Plus.Files.All(baseFile => legacyVanilla2Plus.Files.Any(file => file.Path == baseFile.Path &&
                 file.Sha512 == baseFile.Sha512 && file.Downloads.SequenceEqual(baseFile.Downloads))),
            "legacy Vanilla 2 Plus archive remains pinned and preserves the original release");
        var previousVanilla2PlusPath = Path.Combine(AppContext.BaseDirectory, "releases", "vanilla-2-plus", Vanilla2PlusRelease.PreviousArtifactFileName);
        var previousVanilla2Plus = PackArchive.Open(previousVanilla2PlusPath, Vanilla2PlusRelease.PreviousArtifactSha512);
        True(previousVanilla2Plus.VersionId == "0.13.0" && previousVanilla2Plus.Files.Count == 59 &&
             legacyVanilla2Plus.Files.All(baseFile => previousVanilla2Plus.Files.Any(file => file.Path == baseFile.Path &&
                 file.Sha512 == baseFile.Sha512 && file.Downloads.SequenceEqual(baseFile.Downloads))),
            "previous Vanilla 2 Plus archive remains pinned and preserves the legacy release");
        var priorVanilla2PlusPath = Path.Combine(AppContext.BaseDirectory, "releases", "vanilla-2-plus", Vanilla2PlusRelease.PriorArtifactFileName);
        var priorVanilla2Plus = PackArchive.Open(priorVanilla2PlusPath, Vanilla2PlusRelease.PriorArtifactSha512);
        True(priorVanilla2Plus.VersionId == "0.14.0" && priorVanilla2Plus.Files.Count == 61 &&
             previousVanilla2Plus.Files.All(baseFile => priorVanilla2Plus.Files.Any(file => file.Path == baseFile.Path &&
                 file.Sha512 == baseFile.Sha512 && file.Downloads.SequenceEqual(baseFile.Downloads))),
            "Vanilla 2 Plus 0.14.0 remains pinned with its guard animation packs");
        var guardVanilla2PlusPath = Path.Combine(AppContext.BaseDirectory, "releases", "vanilla-2-plus", Vanilla2PlusRelease.GuardArtifactFileName);
        var guardVanilla2Plus = PackArchive.Open(guardVanilla2PlusPath, Vanilla2PlusRelease.GuardArtifactSha512);
        True(guardVanilla2Plus.VersionId == "0.16.0" && guardVanilla2Plus.Files.Count == 61,
            "Vanilla 2 Plus 0.16.0 remains pinned for Repair");
        var worldgenVanilla2Plus = PackArchive.Open(
            Path.Combine(AppContext.BaseDirectory, "releases", "vanilla-2-plus", Vanilla2PlusRelease.WorldgenArtifactFileName),
            Vanilla2PlusRelease.WorldgenArtifactSha512);
        True(worldgenVanilla2Plus.VersionId == "0.17.0" && worldgenVanilla2Plus.Files.Count == 64,
            "Frontier 0.17.0 remains pinned for Repair");
        var untunedVanilla2Plus = PackArchive.Open(
            Path.Combine(AppContext.BaseDirectory, "releases", "vanilla-2-plus", Vanilla2PlusRelease.UntunedArtifactFileName),
            Vanilla2PlusRelease.UntunedArtifactSha512);
        True(untunedVanilla2Plus.VersionId == "0.19.0", "Frontier 0.19.0 remains pinned for Repair");
        var tunedVanilla2Plus = PackArchive.Open(
            Path.Combine(AppContext.BaseDirectory, "releases", "vanilla-2-plus", Vanilla2PlusRelease.TunedArtifactFileName),
            Vanilla2PlusRelease.TunedArtifactSha512);
        True(tunedVanilla2Plus.VersionId == "0.19.1", "Frontier 0.19.1 remains pinned for Repair");
        var yungsVanilla2Plus = PackArchive.Open(
            Path.Combine(AppContext.BaseDirectory, "releases", "vanilla-2-plus", Vanilla2PlusRelease.YungsArtifactFileName),
            Vanilla2PlusRelease.YungsArtifactSha512);
        True(yungsVanilla2Plus.VersionId == "0.19.2", "Frontier 0.19.2 remains pinned for Repair");
        var spidersVanilla2Plus = PackArchive.Open(
            Path.Combine(AppContext.BaseDirectory, "releases", "vanilla-2-plus", Vanilla2PlusRelease.SpidersArtifactFileName),
            Vanilla2PlusRelease.SpidersArtifactSha512);
        True(spidersVanilla2Plus.VersionId == "0.19.3", "Frontier 0.19.3 remains pinned for Repair");
        var vanilla2PlusPath = Path.Combine(AppContext.BaseDirectory,
            Vanilla2PlusRelease.ArtifactRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var vanilla2Plus = PackArchive.Open(vanilla2PlusPath, Vanilla2PlusRelease.ArtifactSha512);
        Equal(Vanilla2PlusRelease.PackVersion, vanilla2Plus.VersionId, "Vanilla 2 Plus release version");
        Equal("MinePack Vanilla 2 Plus", vanilla2Plus.Name, "Vanilla 2 Plus archive name");
        Equal(TestPackRelease.MinecraftVersion, vanilla2Plus.MinecraftVersion, "Vanilla 2 Plus Minecraft version");
        Equal(TestPackRelease.FabricLoaderVersion, vanilla2Plus.FabricLoaderVersion, "Vanilla 2 Plus Fabric Loader version");
        True(vanilla2Plus.Files.Count == 65 &&
             worldgenVanilla2Plus.Files.Where(file => !file.Path.Contains("smoothswapping", StringComparison.OrdinalIgnoreCase))
                 .All(baseFile => vanilla2Plus.Files.Any(file => file.Path == baseFile.Path &&
                 file.Sha512 == baseFile.Sha512 && file.Downloads.SequenceEqual(baseFile.Downloads))) &&
             !vanilla2Plus.Files.Any(file => file.Path.Contains("smoothswapping", StringComparison.OrdinalIgnoreCase)) &&
             pack.Files.All(baseFile => vanilla2Plus.Files.Any(file => file.Path == baseFile.Path &&
                 file.Sha512 == baseFile.Sha512 && file.Downloads.SequenceEqual(baseFile.Downloads))) &&
             vanilla2Plus.Overrides.Select(file => file.Path).ToHashSet(StringComparer.Ordinal)
                 .SetEquals(new[] { "config/iris.properties", "config/guardvillagers.json", "config/voxyworldgenv2.json" }
                     .Concat(YungsJarNames.Concat(NewForkJarNames).Select(name => "mods/" + name))
                     .Append("resourcepacks/xalis-enhanced-vanilla-26.2-minepack.1.zip")) &&
             vanilla2Plus.Files.Any(file => file.Path == "mods/continuity-3.0.1+26.2.jar" &&
                 file.Sha512.Equals("3436b39fcdddce87f8eda0f35095067477636df2667195df3cb8eae2d002d3ff8ac44de97332668ee50e13ad91be5c532cbc6121878f1e6c904c98c1c9c67c0b", StringComparison.OrdinalIgnoreCase)) &&
             vanilla2Plus.Files.Any(file => file.Path == "mods/citresewn-continuation-1.2.2-fork.13+26.2.jar" &&
                 file.Sha512.Equals("520f37c6c2c8dce4ad2648f8e7d8193c9f4796c83898ed0f9371232376362c54cddbe4c476ef3245162e9535c7d1e7799740f4417d051a6baf287daa0f685842", StringComparison.OrdinalIgnoreCase)),
            "Frontier preserves downloads and pins xali, Continuity, and CIT Resewn Continuation");
        using (var archive = ZipFile.OpenRead(vanilla2PlusPath))
        using (var config = JsonDocument.Parse(archive.GetEntry("overrides/config/guardvillagers.json")!.Open()))
            True(config.RootElement.GetProperty("followHero").GetBoolean() == false &&
                 config.RootElement.GetProperty("reputationRequirement").GetInt32() == int.MinValue &&
                 config.RootElement.GetProperty("giveGuardStuffHotv").GetBoolean() == false &&
                 config.RootElement.GetProperty("setGuardPatrolHotv").GetBoolean() == false,
                "pinned guard config allows inventory, follow, and patrol without Hero of the Village");
        using (var archive = ZipFile.OpenRead(vanilla2PlusPath))
        using (var config = JsonDocument.Parse(archive.GetEntry("overrides/config/voxyworldgenv2.json")!.Open()))
            True(config.RootElement.GetProperty("generationRadius").GetInt32() == 128 &&
                 config.RootElement.GetProperty("maxActiveTasks").GetInt32() == 3,
                "pinned Voxy WorldGen config keeps radius 128 and limits active tasks to three");

        var catalog = PackCatalog.VanillaPlusGroups.SelectMany(group => group.Items).ToArray();
        var vanilla2PlusCatalog = PackCatalog.Items;
        True(catalog.Length == 46 && catalog.Count(item => item.Kind == "mod") == 37 &&
             catalog.Count(item => item.Kind == "resourcepack") == 8 && catalog.Count(item => item.Kind == "shader") == 1 &&
             catalog.Select(item => item.FilePath).ToHashSet(StringComparer.Ordinal).SetEquals(pack.Files.Select(file => file.Path)) &&
             vanilla2PlusCatalog.Count == 75 && vanilla2PlusCatalog.Count(item => item.FilePath.StartsWith("mods/", StringComparison.Ordinal)) == 63 &&
             vanilla2PlusCatalog.Count(item => item.Kind == "resourcepack") == 11 &&
             vanilla2PlusCatalog.Count(item => item.Kind == "datapack") == 1 &&
             vanilla2PlusCatalog.Select(item => item.FilePath).ToHashSet(StringComparer.Ordinal)
                 .SetEquals(vanilla2Plus.Files.Select(file => file.Path).Concat(YungsJarNames.Concat(NewForkJarNames).Select(name => "mods/" + name)).Append("resourcepacks/xalis-enhanced-vanilla-26.2-minepack.1.zip")) &&
             vanilla2PlusCatalog.All(item => item.ModrinthUrl is null
                 ? YungsJarNames.Concat(NewForkJarNames).Contains(Path.GetFileName(item.FilePath)) || item.FilePath == "resourcepacks/xalis-enhanced-vanilla-26.2-minepack.1.zip"
                 : item.ModrinthUrl.Scheme == Uri.UriSchemeHttps && item.ModrinthUrl.Host == "modrinth.com" && !string.IsNullOrWhiteSpace(item.ProjectId)),
            "Vanilla Plus and Vanilla 2 Plus catalogs exactly match their pinned releases");
        var initialOptions = TestPackRelease.InitialOptions.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        True(initialOptions[0] == "version:4903" && new[]
        {
            "key_key.sprint:key.keyboard.left.shift", "key_key.sneak:key.keyboard.left.control",
            "fov:0.25", "fullscreen:true", "exclusiveFullscreen:true", "guiScale:4"
        }.All(initialOptions.Contains) && !initialOptions.Any(line => line.StartsWith("fullscreenResolution:", StringComparison.Ordinal)),
            "new profile defaults include requested controls, FOV, fullscreen and GUI scale without a fixed monitor mode");
        True(Vanilla2PlusRelease.InitialResourcePacks.Length == 11 &&
             Vanilla2PlusRelease.InitialResourcePacks.Take(8).SequenceEqual(TestPackRelease.InitialResourcePacks) &&
             Vanilla2PlusRelease.InitialOptions.Contains("file/Semos Animations Lib 2.0.4.zip", StringComparison.Ordinal) &&
             Vanilla2PlusRelease.InitialOptions.Contains("file/Freshly Modded 3.0.5.zip", StringComparison.Ordinal) &&
             Vanilla2PlusRelease.InitialOptions.Contains("file/xalis-enhanced-vanilla-26.2-minepack.1.zip", StringComparison.Ordinal) &&
             !TestPackRelease.InitialOptions.Contains("Freshly Modded", StringComparison.Ordinal),
            "guard animation packs are enabled only for new Vanilla 2 Plus installations");
        Pass("pinned .mrpack opens and matches its SHA-512");
    }

    private static void VerifyLocalization()
    {
        var originalUiCulture = CultureInfo.CurrentUICulture;
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            foreach (var (name, expected) in new[]
                     {
                         ("ru-RU", "ru"), ("ru", "ru"), ("en-US", "en"), ("en-GB", "en"),
                         ("zh-CN", "zh-CN"), ("zh-SG", "zh-CN"), ("zh-TW", "zh-CN"), ("zh-Hans", "zh-CN"),
                         ("de-DE", "en"), ("ja-JP", "en")
                     })
                Equal(expected, LocalizedText.SelectUiCulture(CultureInfo.GetCultureInfo(name)).Name, $"UI culture for {name}");

            var resourceDirectory = Path.Combine(Environment.CurrentDirectory, "src", "MinePack.Core", "Resources");
            var english = ReadResourceFile(Path.Combine(resourceDirectory, "Strings.resx"));
            var russian = ReadResourceFile(Path.Combine(resourceDirectory, "Strings.ru.resx"));
            var chinese = ReadResourceFile(Path.Combine(resourceDirectory, "Strings.zh-CN.resx"));
            True(english.Keys.Order().SequenceEqual(russian.Keys.Order()), "English and Russian resource keys match");
            True(english.Keys.Order().SequenceEqual(chinese.Keys.Order()), "English and Simplified Chinese resource keys match");
            foreach (var key in english.Keys)
            {
                True(Placeholders(english[key]).SequenceEqual(Placeholders(russian[key])), $"resource placeholders match for {key}");
                True(Placeholders(english[key]).SequenceEqual(Placeholders(chinese[key])), $"Chinese resource placeholders match for {key}");
                True(chinese[key] != english[key] && chinese[key].Any(character => character is >= '\u3400' and <= '\u9FFF'),
                    $"Chinese resource is translated and does not fall back to English for {key}");
            }

            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
            Equal("Ready to install", LocalizedText.Get("UiReady"), "English startup text");
            Equal("Preparing a separate game folder", LocalizedText.Get("PreparingInstance"), "English progress text");
            Equal("Pack files installed.", LocalizedText.Get("PackFilesInstalled"), "English success text");
            Equal("The pack file was not found.", LocalizedText.Get("PackFileMissing"), "English error text");
            Equal("Performance & Render Distance", LocalizedText.Get("CatalogPerformance"), "English catalog text");
            Equal("Building Blocks — 5", PackCatalog.Vanilla2PlusGroups[^2].Heading, "English Vanilla 2 Plus building category");
            Equal("World & Structures — 12", PackCatalog.Vanilla2PlusGroups[^1].Heading, "English Vanilla 2 Plus worldgen category");
            Equal("Technical Foundation — 13", PackCatalog.Vanilla2PlusGroups.Single(group => group.Key == "CatalogTechnical").Heading, "English Vanilla 2 Plus dependencies");
            Equal("Resource Packs — 11", PackCatalog.Vanilla2PlusGroups.Single(group => group.Key == "CatalogResourcePacks").Heading, "English Vanilla 2 Plus resource packs");
            Equal("Pack version 0.19.4", LocalizedText.Get("UiPackVersion", Vanilla2PlusRelease.PackVersion), "English selected pack version");
            Equal("Installer version 0.18.0", LocalizedText.Get("UiInstallerVersion", "0.18.0"), "English installer version");
            Equal("63 mods · 11 resource packs · 1 shader", LocalizedText.Get("UiPackCountsVanilla2Plus"), "English selected pack counts");
            Equal("37 mods · 8 resource packs · 1 shader", LocalizedText.Get("UiPackCountsVanillaPlus"), "English Vanilla Plus counts");
            Equal("Performance & Render Distance — 8|Graphics & Animations — 9|Tools & Quality of Life — 9|Sound — 2|Technical Foundation — 9|Resource Packs — 8|Shader — 1",
                string.Join('|', PackCatalog.Groups.Select(group => group.Heading)), "English catalog headings and counts");
            Equal("Copied worlds: 2. Skipped existing names: 1.", LocalizedText.Get("WorldImportSummary", 2, 1), "English formatted text");

            CultureInfo.CurrentUICulture = LocalizedText.SelectUiCulture(CultureInfo.GetCultureInfo("zh-SG"));
            Equal("zh-CN", CultureInfo.CurrentUICulture.Name, "Chinese UI culture canonicalized to Simplified Chinese");
            Equal("准备就绪，可以安装", LocalizedText.Get("UiReady"), "Chinese startup text");
            Equal("正在准备独立游戏文件夹", LocalizedText.Get("PreparingInstance"), "Chinese progress text");
            Equal("整合包文件已安装。", LocalizedText.Get("PackFilesInstalled"), "Chinese success text");
            Equal("未找到整合包文件。", LocalizedText.Get("PackFileMissing"), "Chinese error text");
            Equal("性能与区块渲染距离", LocalizedText.Get("CatalogPerformance"), "Chinese catalog text");
            Equal("整合包版本 0.19.4", LocalizedText.Get("UiPackVersion", Vanilla2PlusRelease.PackVersion), "Chinese selected pack version");
            Equal("安装程序版本 0.18.0", LocalizedText.Get("UiInstallerVersion", "0.18.0"), "Chinese installer version");
            Equal("63 个模组 · 11 个资源包 · 1 个光影包", LocalizedText.Get("UiPackCountsVanilla2Plus"), "Chinese selected pack counts");
            Equal("37 个模组 · 8 个资源包 · 1 个光影包", LocalizedText.Get("UiPackCountsVanillaPlus"), "Chinese Vanilla Plus counts");
            Equal("已复制存档：2。因名称已存在而跳过：1。", LocalizedText.Get("WorldImportSummary", 2, 1), "Chinese formatted text");
            Equal("建筑方块 — 5", PackCatalog.Vanilla2PlusGroups[^2].Heading, "Chinese Vanilla 2 Plus building category");
            Equal("世界与结构 — 12", PackCatalog.Vanilla2PlusGroups[^1].Heading, "Chinese Vanilla 2 Plus worldgen category");
            Equal("资源包 — 11", PackCatalog.Vanilla2PlusGroups.Single(group => group.Key == "CatalogResourcePacks").Heading,
                "Chinese Vanilla 2 Plus resource pack category");

            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ru");
            Equal("Готово к установке", LocalizedText.Get("UiReady"), "Russian startup text");
            Equal("Подготовка отдельного каталога", LocalizedText.Get("PreparingInstance"), "Russian progress text");
            Equal("Файлы сборки установлены.", LocalizedText.Get("PackFilesInstalled"), "Russian success text");
            Equal("Файл сборки не найден.", LocalizedText.Get("PackFileMissing"), "Russian error text");
            Equal("Производительность и дальность", LocalizedText.Get("CatalogPerformance"), "Russian catalog text");
            Equal("Строительные блоки — 5", PackCatalog.Vanilla2PlusGroups[^2].Heading, "Russian Vanilla 2 Plus building category");
            Equal("Мир и структуры — 12", PackCatalog.Vanilla2PlusGroups[^1].Heading, "Russian Vanilla 2 Plus worldgen category");
            Equal("Техническая основа — 13", PackCatalog.Vanilla2PlusGroups.Single(group => group.Key == "CatalogTechnical").Heading, "Russian Vanilla 2 Plus dependencies");
            Equal("Ресурспаки — 11", PackCatalog.Vanilla2PlusGroups.Single(group => group.Key == "CatalogResourcePacks").Heading, "Russian Vanilla 2 Plus resource packs");
            Equal("Версия сборки 0.19.4", LocalizedText.Get("UiPackVersion", Vanilla2PlusRelease.PackVersion), "Russian selected pack version");
            Equal("Версия установщика 0.18.0", LocalizedText.Get("UiInstallerVersion", "0.18.0"), "Russian installer version");
            Equal("63 мода · 11 ресурспаков · 1 шейдер", LocalizedText.Get("UiPackCountsVanilla2Plus"), "Russian selected pack counts");
            Equal("37 модов · 8 ресурспаков · 1 шейдер", LocalizedText.Get("UiPackCountsVanillaPlus"), "Russian Vanilla Plus counts");
            Equal("Производительность и дальность — 8|Графика и анимации — 9|Инструменты и удобство — 9|Звук — 2|Техническая основа — 9|Ресурспаки — 8|Шейдер — 1",
                string.Join('|', PackCatalog.Groups.Select(group => group.Heading)), "Russian catalog headings and counts");
            Equal("Скопировано миров: 2. Пропущено совпадений имён: 1.", LocalizedText.Get("WorldImportSummary", 2, 1), "Russian formatted text");

            try
            {
                _ = LocalizedText.Get("MissingLocalizationKey");
                throw new InvalidOperationException("A missing localization key was not rejected.");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Missing localization resource", StringComparison.Ordinal)) { }
            Equal(originalCulture.Name, CultureInfo.CurrentCulture.Name, "localization leaves formatting culture unchanged");
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
        Pass("UI culture selection, resource keys, placeholders, and localized messages");
    }

    private static Dictionary<string, string> ReadResourceFile(string path) => XDocument.Load(path).Root!
        .Elements("data")
        .ToDictionary(item => (string)item.Attribute("name")!, item => item.Element("value")!.Value, StringComparer.Ordinal);

    private static int[] Placeholders(string value) => Regex.Matches(value, @"\{(\d+)(?:,[^}:]*)?(?::[^}]*)?\}")
        .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
        .Order()
        .ToArray();

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

        failDownloads = false;
        currentDownloadBytes = packageBytes;
        var reinstall = await installer.InstallAsync(packPath, packHash, installRoot);
        True(reinstall.Success, "same release reinstalls after uninstall preserves user data without a manifest");
        var reinstalledInstance = reinstall.GameDirectory!;
        True(!reinstalledInstance.Equals(instance, StringComparison.OrdinalIgnoreCase) &&
             File.Exists(worldPath) && File.Exists(screenshotPath) && File.Exists(unknownPath),
            "reinstall leaves the uninstalled instance and its user data untouched");
        True(File.Exists(Path.Combine(reinstalledInstance, InstallationManifest.FileName)),
            "reinstall writes a new local manifest");
        Equal(reinstalledInstance, installer.GetActiveInstancePath(installRoot), "reinstalled instance becomes active");
        Pass("reinstall after uninstall uses a fresh instance without losing user data");

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

    private static async Task VerifyVariantSwitchingAsync(string tempRoot)
    {
        var installRoot = Path.Combine(tempRoot, "variant-switch-root");
        var plusBytes = Bytes("vanilla plus managed file");
        var twoPlusBytes = Bytes("vanilla 2 plus managed file");
        var currentDownloadBytes = plusBytes;
        var handler = new DelegateHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(currentDownloadBytes)
        });
        var plusPackPath = Path.Combine(tempRoot, "variant-plus.mrpack");
        var twoPlusPackPath = Path.Combine(tempRoot, "variant-two-plus.mrpack");
        CreatePack(plusPackPath, TestPackRelease.PackVersion, [new TestFile("mods/test.jar", plusBytes)]);
        CreatePack(twoPlusPackPath, Vanilla2PlusRelease.PackVersion, [new TestFile("mods/test.jar", twoPlusBytes)]);
        var plusHash = HashFile(plusPackPath);
        var twoPlusHash = HashFile(twoPlusPackPath);

        using var installer = new InstallService(new DownloadEngine(handler));
        var plusInstall = await installer.InstallAsync(plusPackPath, plusHash, installRoot);
        True(plusInstall.Success, "Vanilla Plus fixture installs first");
        var plusInstance = plusInstall.GameDirectory!;
        var plusWorld = Path.Combine(plusInstance, "saves", "plus-world", "level.dat");
        Directory.CreateDirectory(Path.GetDirectoryName(plusWorld)!);
        File.WriteAllText(plusWorld, "keep Vanilla Plus world");

        currentDownloadBytes = twoPlusBytes;
        var twoPlusInstall = await installer.InstallAsync(twoPlusPackPath, twoPlusHash, installRoot);
        True(twoPlusInstall.Success, "Vanilla 2 Plus installs as a separate active instance");
        var twoPlusInstance = twoPlusInstall.GameDirectory!;
        var twoPlusWorld = Path.Combine(twoPlusInstance, "saves", "two-plus-world", "level.dat");
        Directory.CreateDirectory(Path.GetDirectoryName(twoPlusWorld)!);
        File.WriteAllText(twoPlusWorld, "keep Vanilla 2 Plus world");
        True(!plusInstance.Equals(twoPlusInstance, StringComparison.OrdinalIgnoreCase) && File.Exists(plusWorld),
            "switching variants keeps the previous isolated instance and its world");

        File.WriteAllText(Path.Combine(plusInstance, "mods", "test.jar"), "corrupted inactive Vanilla Plus file");
        currentDownloadBytes = plusBytes;
        var returnToPlus = await installer.InstallAsync(plusPackPath, plusHash, installRoot);
        True(returnToPlus.Success, "selecting Vanilla Plus reactivates its preserved instance");
        Equal(plusInstance, installer.GetActiveInstancePath(installRoot), "active marker returns to Vanilla Plus");
        Equal(HashBytes(plusBytes), HashFile(Path.Combine(plusInstance, "mods", "test.jar")),
            "reactivating a preserved instance repairs damaged managed files first");
        True(File.Exists(plusWorld) && File.Exists(twoPlusWorld), "reactivating Vanilla Plus preserves both versions' worlds");

        currentDownloadBytes = twoPlusBytes;
        var returnToTwoPlus = await installer.InstallAsync(twoPlusPackPath, twoPlusHash, installRoot);
        True(returnToTwoPlus.Success, "selecting Vanilla 2 Plus reactivates its preserved instance");
        Equal(twoPlusInstance, installer.GetActiveInstancePath(installRoot), "active marker returns to Vanilla 2 Plus");
        var twoPlusManaged = Path.Combine(twoPlusInstance, "mods", "test.jar");
        File.WriteAllText(twoPlusManaged, "damaged active Vanilla 2 Plus file");
        var repair = await installer.RepairAsync(twoPlusInstance, twoPlusPackPath, twoPlusHash);
        True(repair.Success, "Repair uses the active Vanilla 2 Plus archive");
        Equal(HashBytes(twoPlusBytes), HashFile(twoPlusManaged), "Vanilla 2 Plus Repair restores its pinned file");
        var uninstall = await installer.UninstallAsync(twoPlusInstance, twoPlusPackPath, twoPlusHash);
        True(uninstall.Success && installer.GetActiveInstancePath(installRoot) is null,
            "Uninstall uses the active Vanilla 2 Plus archive and clears its marker");
        True(File.Exists(twoPlusWorld) && File.Exists(plusWorld) && Directory.Exists(plusInstance),
            "uninstalling active Vanilla 2 Plus preserves both worlds and the inactive Vanilla Plus instance");
        Pass("switching Vanilla Plus and Vanilla 2 Plus reactivates pinned instances and preserves worlds");
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
        True(manifest.Files.Count == 47 && new[]
        {
            "SubtleEffects-fabric-26.2-1.14.3.jar",
            "fzzy_config-0.7.6+26.2.jar", "fabric-language-kotlin-1.14.1+kotlin.2.4.20.jar"
        }.All(name => manifest.Files.Any(file => file.Path == "mods/" + name)) &&
            !manifest.Files.Any(file => file.Path.Contains("smoothswapping", StringComparison.OrdinalIgnoreCase)),
            "actual Vanilla Plus install excludes Smooth Swapping and manages the remaining effects mod and libraries");
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
        var options = File.ReadAllLines(optionsPath);
        True(new[]
        {
            "key_key.sprint:key.keyboard.left.shift", "key_key.sneak:key.keyboard.left.control",
            "fov:0.25", "fullscreen:true", "exclusiveFullscreen:true", "guiScale:4"
        }.All(options.Contains) && !options.Any(line => line.StartsWith("fullscreenResolution:", StringComparison.Ordinal)),
            "new instance starts with requested controls, FOV, fullscreen, and GUI scale without a fixed monitor mode");
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

    private static async Task<bool> VerifyActualVanilla2PlusAsync(string tempRoot)
    {
        var packPath = Path.Combine(AppContext.BaseDirectory,
            Vanilla2PlusRelease.ArtifactRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var installRoot = Path.Combine(tempRoot, "live-vanilla-2-plus-install");
        var vanilla = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");
        var vanillaBefore = CaptureVanillaData(vanilla);
        if (vanillaBefore is null)
            Console.WriteLine("NOT RUN: vanilla mods/config/saves could not be read for a before/after comparison.");
        using var installer = new InstallService();
        var install = await installer.InstallAsync(packPath, Vanilla2PlusRelease.ArtifactSha512, installRoot);
        if (!install.Success)
        {
            Console.WriteLine($"NOT RUN: Vanilla 2 Plus install did not complete ({install.Code}). {install.Message} Diagnostic log: {install.LogPath ?? "unavailable"}.");
            return false;
        }

        var instance = install.GameDirectory ?? throw new InvalidOperationException("Vanilla 2 Plus install did not return an instance directory.");
        var manifest = InstallationManifest.Load(instance);
        Equal(Vanilla2PlusRelease.PackVersion, manifest.PackVersion, "installed Vanilla 2 Plus manifest version");
        True(manifest.Files.Count == 76 && manifest.Files.Any(file => file.Path == "config/iris.properties") &&
             YungsJarNames.Concat(NewForkJarNames).All(name => manifest.Files.Any(file => file.Path == "mods/" + name)),
            "Frontier manifest records 65 downloads, nine embedded JARs, the xali ZIP, and the Iris config override");
        foreach (var file in manifest.Files)
        {
            var path = Path.Combine(instance, file.Path.Replace('/', Path.DirectorySeparatorChar));
            True(string.Equals(file.Sha512, HashFile(path), StringComparison.OrdinalIgnoreCase),
                $"Vanilla 2 Plus installed file hash {file.Path}");
        }
        var newModFiles = new[]
        {
            "mcw-windows-2.4.2-mc26.2fabric.jar", "mcw-fences-1.2.1-mc26.2fabric.jar",
            "mcw-bridges-3.1.2-mc26.2fabric.jar", "mcw-doors-1.1.5-mc26.2fabric.jar",
            "mcw-stairs-1.0.2-mc26.2fabric.jar", "bettervillage-fabric-26.2-4.0.0.jar",
            "libraryferret-fabric-26.2-5.0.0.jar", "MoogsNetherStructures-universal-1.21-3.1.1.jar",
            "MoogsStructureLib-fabric-26.2-3.3.0.jar", "MoogsVoyagerStructures-universal-1.21-5.1.3.jar",
            "Structory_26.2_v1.3.7.jar",
            "SubtleEffects-fabric-26.2-1.14.3.jar", "guardvillagers-2.1.3-26.2.jar",
            "fzzy_config-0.7.6+26.2.jar", "fabric-language-kotlin-1.14.1+kotlin.2.4.20.jar",
            "takesapillage-fabric-1.0.12+mc26.2.jar", "ResourcefulLib-5.0.4.jar",
            "Voxy World Gen V2-fabric-26.2-2.4.3.jar"
        };
        True(newModFiles.All(name => manifest.Files.Any(file => file.Path == "mods/" + name)) &&
             !manifest.Files.Any(file => file.Path.Contains("smoothswapping", StringComparison.OrdinalIgnoreCase)),
            "all Frontier additions are managed without Smooth Swapping");
        True(Vanilla2PlusRelease.InitialResourcePacks.Skip(8).All(name =>
                manifest.Files.Any(file => file.Path == "resourcepacks/" + name)),
            "both Guard Villagers animation resource packs are installed and managed");
        var optionsPath = Path.Combine(instance, "options.txt");
        var expectedPacks = new[] { "vanilla" }.Concat(Vanilla2PlusRelease.InitialResourcePacks.Select(name => "file/" + name)).Append("punchy:punchy");
        var options = File.ReadAllLines(optionsPath);
        True(options[0] == "version:4903" &&
             File.ReadAllText(optionsPath).Contains("resourcePacks:" + JsonSerializer.Serialize(expectedPacks), StringComparison.Ordinal),
            "Vanilla 2 Plus selects all ten resource packs plus Punchy on first launch");
        var bbeConfigPath = Path.Combine(instance, "config", "BBEConfig.json");
        True(File.Exists(bbeConfigPath), "Vanilla 2 Plus applies the existing Better Block Entities config");
        var guardConfigPath = Path.Combine(instance, "config", "guardvillagers.json");
        using (var guardConfig = JsonDocument.Parse(File.ReadAllText(guardConfigPath)))
            True(guardConfig.RootElement.GetProperty("followHero").GetBoolean() == false &&
                 guardConfig.RootElement.GetProperty("reputationRequirement").GetInt32() == int.MinValue &&
                 !manifest.Files.Any(file => file.Path == "config/guardvillagers.json"),
                "Vanilla 2 Plus preconfigures guards without managing later player changes");
        var voxyConfigPath = Path.Combine(instance, "config", "voxyworldgenv2.json");
        using (var voxyConfig = JsonDocument.Parse(File.ReadAllText(voxyConfigPath)))
            True(voxyConfig.RootElement.GetProperty("generationRadius").GetInt32() == 128 &&
                 voxyConfig.RootElement.GetProperty("maxActiveTasks").GetInt32() == 3 &&
                 !manifest.Files.Any(file => file.Path == "config/voxyworldgenv2.json"),
                "Vanilla 2 Plus preconfigures Voxy WorldGen without managing later player changes");
        var worldPath = Path.Combine(instance, "saves", "plan004-test-world", "level.dat");
        Directory.CreateDirectory(Path.GetDirectoryName(worldPath)!);
        File.WriteAllText(worldPath, "test world stays unmanaged");
        File.WriteAllText(optionsPath, "player options\n");
        File.WriteAllText(bbeConfigPath, "player BBE settings\n");
        File.WriteAllText(guardConfigPath, "player guard settings\n");
        File.WriteAllText(voxyConfigPath, "player Voxy WorldGen settings\n");
        var corruptMacaw = Path.Combine(instance, "mods", "mcw-stairs-1.0.2-mc26.2fabric.jar");
        File.WriteAllText(corruptMacaw, "corrupt managed mod");
        var repair = await installer.RepairAsync(instance, packPath, Vanilla2PlusRelease.ArtifactSha512);
        True(repair.Success && File.ReadAllText(optionsPath) == "player options\n" &&
             File.ReadAllText(bbeConfigPath) == "player BBE settings\n" &&
             File.ReadAllText(guardConfigPath) == "player guard settings\n" &&
             File.ReadAllText(voxyConfigPath) == "player Voxy WorldGen settings\n" && File.Exists(worldPath),
            "Vanilla 2 Plus Repair restores its pinned mod and preserves user settings and world");
        var vanillaAfter = CaptureVanillaData(vanilla);
        if (vanillaBefore is not null && vanillaAfter is not null)
            Equal(vanillaBefore, vanillaAfter, "Vanilla 2 Plus leaves vanilla mods/config/saves unchanged");
        else if (vanillaBefore is not null || vanillaAfter is not null)
            Console.WriteLine("NOT RUN: vanilla mods/config/saves comparison was incomplete because access changed.");

        var uninstall = await installer.UninstallAsync(instance, packPath, Vanilla2PlusRelease.ArtifactSha512);
        True(uninstall.Success && File.Exists(worldPath) && File.ReadAllText(optionsPath) == "player options\n" &&
             File.ReadAllText(bbeConfigPath) == "player BBE settings\n" &&
             File.ReadAllText(guardConfigPath) == "player guard settings\n" &&
             File.ReadAllText(voxyConfigPath) == "player Voxy WorldGen settings\n",
            "Vanilla 2 Plus Uninstall removes managed files and preserves user data");
        True(installer.GetActiveInstancePath(installRoot) is null, "Vanilla 2 Plus Uninstall clears the active marker");
        Pass("Vanilla 2 Plus actual downloads, SHA-512 checks, install, Repair, and Uninstall");
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
        Equal("MinePack", own.GetProperty("name").GetString(), "new Launcher profile has MinePack display name");
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
            "test-pack-0.8.0-" + TestPackRelease.AnimationArtifactSha512[..12].ToLowerInvariant());
        new InstallationManifest
        {
            PackVersion = "0.8.0",
            MinecraftVersion = "26.2",
            FabricLoaderVersion = TestPackRelease.FabricLoaderVersion,
            PackArchiveSha512 = TestPackRelease.AnimationArtifactSha512
        }.SaveAtomic(oldInstance);
        string ProfileWithoutMarker(string gameDir, string name = "MinePack Test Pack", string lastVersionId = "fabric-loader-0.19.5-26.2") => JsonSerializer.Serialize(new
        {
            profiles = new Dictionary<string, object>
            {
                [LauncherProfile.ProfileKey] = new
                {
                    name, type = "custom",
                    lastVersionId, gameDir
                }
            }
        });
        foreach (var (version, hash) in new[]
                 {
                     ("0.15.0", TestPackRelease.SmoothArtifactSha512),
                     ("0.17.0", Vanilla2PlusRelease.WorldgenArtifactSha512),
                     ("0.19.0", Vanilla2PlusRelease.UntunedArtifactSha512),
                     ("0.19.1", Vanilla2PlusRelease.TunedArtifactSha512)
                 })
        {
            var instance = Path.Combine(tempRoot, "previous-owned-instance", "instances",
                $"test-pack-{version}-{hash[..12].ToLowerInvariant()}");
            new InstallationManifest
            {
                PackVersion = version,
                MinecraftVersion = TestPackRelease.MinecraftVersion,
                FabricLoaderVersion = TestPackRelease.FabricLoaderVersion,
                PackArchiveSha512 = hash
            }.SaveAtomic(instance);
            using var removedPrevious = JsonDocument.Parse(LauncherProfile.RemoveFixtureCandidate(ProfileWithoutMarker(instance)));
            True(!removedPrevious.RootElement.GetProperty("profiles").TryGetProperty(LauncherProfile.ProfileKey, out _),
                $"previous {version} profile remains recognized for uninstall");
        }
        var reinstallInstances = Path.Combine(tempRoot, "reinstall-owned-instance", "instances");
        Directory.CreateDirectory(reinstallInstances);
        var reinstallBaseName = $"test-pack-{Vanilla2PlusRelease.PackVersion}-{Vanilla2PlusRelease.ArtifactSha512[..12].ToLowerInvariant()}-reinstall-";
        string CreateReinstallFixture(string suffix, bool saveManifest = true, string? manifestHash = null, string? manifestVersion = null, bool nested = false)
        {
            var parent = nested ? Path.Combine(reinstallInstances, "nested") : reinstallInstances;
            Directory.CreateDirectory(parent);
            var instance = Path.Combine(parent, reinstallBaseName + suffix);
            Directory.CreateDirectory(instance);
            if (saveManifest)
            {
                new InstallationManifest
                {
                    PackVersion = manifestVersion ?? Vanilla2PlusRelease.PackVersion,
                    MinecraftVersion = TestPackRelease.MinecraftVersion,
                    FabricLoaderVersion = TestPackRelease.FabricLoaderVersion,
                    PackArchiveSha512 = manifestHash ?? Vanilla2PlusRelease.ArtifactSha512
                }.SaveAtomic(instance);
            }
            return instance;
        }
        void RejectMarkerlessProfile(string profileJson, string label)
        {
            try
            {
                _ = LauncherProfile.BuildFixtureCandidate(profileJson, Path.Combine(tempRoot, "replacement-instance"),
                    TestPackRelease.MinecraftVersion, TestPackRelease.FabricLoaderVersion);
                throw new InvalidOperationException($"Expected {label} profile rejection during restore.");
            }
            catch (InstallerException ex) when (ex.Code == "LAUNCHER_PROFILE_CONFLICT") { }
            try
            {
                _ = LauncherProfile.RemoveFixtureCandidate(profileJson);
                throw new InvalidOperationException($"Expected {label} profile rejection during uninstall.");
            }
            catch (InstallerException ex) when (ex.Code == "LAUNCHER_PROFILE_CONFLICT") { }
        }
        var markerless = ProfileWithoutMarker(oldInstance);
        var reconfigured = LauncherProfile.BuildFixtureCandidate(markerless, Path.Combine(tempRoot, "new-instance"), TestPackRelease.MinecraftVersion, TestPackRelease.FabricLoaderVersion);
        using (var parsed = JsonDocument.Parse(reconfigured))
        {
            Equal(Path.Combine(tempRoot, "new-instance"), parsed.RootElement.GetProperty("profiles")
                .GetProperty(LauncherProfile.ProfileKey).GetProperty("gameDir").GetString(),
                "Launcher-stripped marker can be recovered from a pinned MinePack manifest");
            Equal("MinePack", parsed.RootElement.GetProperty("profiles").GetProperty(LauncherProfile.ProfileKey)
                .GetProperty("name").GetString(), "old profile is renamed in place");
            Equal(1, parsed.RootElement.GetProperty("profiles").EnumerateObject().Count(),
                "renaming does not create a second profile");
        }
        var newNamed = LauncherProfile.BuildFixtureCandidate(ProfileWithoutMarker(oldInstance, "MinePack"),
            Path.Combine(tempRoot, "new-instance"), TestPackRelease.MinecraftVersion, TestPackRelease.FabricLoaderVersion);
        using (var parsed = JsonDocument.Parse(newNamed))
            Equal("MinePack", parsed.RootElement.GetProperty("profiles").GetProperty(LauncherProfile.ProfileKey)
                .GetProperty("name").GetString(), "new markerless name is recognized with pinned manifest");
        using (var parsed = JsonDocument.Parse(LauncherProfile.RemoveFixtureCandidate(markerless)))
            True(!parsed.RootElement.GetProperty("profiles").TryGetProperty(LauncherProfile.ProfileKey, out _),
                "markerless MinePack profile can be removed while its manifest exists");

        var reinstallInstance = CreateReinstallFixture(Guid.NewGuid().ToString("N"));
        var reinstallRoot = new JsonObject
        {
            ["settings"] = new JsonObject { ["custom"] = true },
            ["profiles"] = JsonNode.Parse(ProfileWithoutMarker(reinstallInstance, "MinePack"))!["profiles"]!.DeepClone()
        };
        ((JsonObject)reinstallRoot["profiles"]!)["vanilla"] = new JsonObject
        {
            ["name"] = "Existing", ["type"] = "custom", ["customField"] = 17
        };
        var markerlessReinstall = reinstallRoot.ToJsonString();
        var restoredReinstall = LauncherProfile.BuildFixtureCandidate(markerlessReinstall,
            Path.Combine(tempRoot, "reinstall-replacement"), TestPackRelease.MinecraftVersion, TestPackRelease.FabricLoaderVersion);
        using (var parsed = JsonDocument.Parse(restoredReinstall))
        {
            var restoredProfile = parsed.RootElement.GetProperty("profiles").GetProperty(LauncherProfile.ProfileKey);
            Equal("MinePack", restoredProfile.GetProperty("name").GetString(),
                "markerless reinstall profile is restored in place");
            Equal(Path.GetFullPath(Path.Combine(tempRoot, "reinstall-replacement")), restoredProfile.GetProperty("gameDir").GetString(),
                "reinstall profile restore selects the requested instance");
            Equal(2, parsed.RootElement.GetProperty("profiles").EnumerateObject().Count(),
                "reinstall profile restore does not duplicate MinePack or drop the foreign profile");
            Equal(17, parsed.RootElement.GetProperty("profiles").GetProperty("vanilla").GetProperty("customField").GetInt32(),
                "reinstall profile restore preserves a foreign profile");
            True(parsed.RootElement.GetProperty("settings").GetProperty("custom").GetBoolean(),
                "reinstall profile restore preserves root settings");
        }
        var removedReinstall = LauncherProfile.RemoveFixtureCandidate(markerlessReinstall, reinstallInstance);
        using (var parsed = JsonDocument.Parse(removedReinstall))
        {
            True(!parsed.RootElement.GetProperty("profiles").TryGetProperty(LauncherProfile.ProfileKey, out _),
                "markerless reinstall profile can be removed for its exact instance");
            Equal(17, parsed.RootElement.GetProperty("profiles").GetProperty("vanilla").GetProperty("customField").GetInt32(),
                "reinstall profile uninstall preserves a foreign profile");
            True(parsed.RootElement.GetProperty("settings").GetProperty("custom").GetBoolean(),
                "reinstall profile uninstall preserves root settings");
        }
        Equal(markerlessReinstall,
            LauncherProfile.RemoveFixtureCandidate(markerlessReinstall, Path.Combine(tempRoot, "another-instance")),
            "reinstall profile is preserved when expected gameDir differs");

        foreach (var (suffix, label) in new[]
                 {
                     (new string('a', 31), "short reinstall suffix"),
                     (new string('a', 31) + "g", "non-hex reinstall suffix"),
                     (new string('A', 32), "uppercase reinstall suffix")
                 })
            RejectMarkerlessProfile(ProfileWithoutMarker(CreateReinstallFixture(suffix), "MinePack"), label);

        RejectMarkerlessProfile(ProfileWithoutMarker(CreateReinstallFixture(Guid.NewGuid().ToString("N"), saveManifest: false), "MinePack"),
            "missing-manifest reinstall");
        RejectMarkerlessProfile(ProfileWithoutMarker(CreateReinstallFixture(Guid.NewGuid().ToString("N"),
            manifestHash: TestPackRelease.ArtifactSha512), "MinePack"), "foreign-manifest reinstall");
        RejectMarkerlessProfile(ProfileWithoutMarker(CreateReinstallFixture(Guid.NewGuid().ToString("N"),
            manifestVersion: "9.9.9"), "MinePack"), "unknown-version reinstall");
        RejectMarkerlessProfile(ProfileWithoutMarker(reinstallInstance, "MinePack", "fabric-loader-0.19.5-26.3"),
            "wrong-lastVersionId reinstall");
        RejectMarkerlessProfile(ProfileWithoutMarker(reinstallInstance, "Someone else's profile"), "foreign-name reinstall");
        var foreignMarkerReinstall = JsonSerializer.Serialize(new
        {
            profiles = new Dictionary<string, object>
            {
                [LauncherProfile.ProfileKey] = new
                {
                    name = "MinePack", type = "custom", lastVersionId = "fabric-loader-0.19.5-26.2",
                    gameDir = reinstallInstance, minepackInstallerId = "foreign-installer"
                }
            }
        });
        RejectMarkerlessProfile(foreignMarkerReinstall, "foreign-marker reinstall");
        RejectMarkerlessProfile(ProfileWithoutMarker(CreateReinstallFixture(Guid.NewGuid().ToString("N"), nested: true), "MinePack"),
            "nested reinstall directory");
        Pass("markerless reinstall profiles restore and uninstall only with exact pinned manifest and directory ownership");

        var previousInstance = Path.Combine(tempRoot, "owned-instance", "instances",
            "test-pack-0.9.0-" + TestPackRelease.MapArtifactSha512[..12].ToLowerInvariant());
        new InstallationManifest
        {
            PackVersion = "0.9.0",
            MinecraftVersion = TestPackRelease.MinecraftVersion,
            FabricLoaderVersion = TestPackRelease.FabricLoaderVersion,
            PackArchiveSha512 = TestPackRelease.MapArtifactSha512
        }.SaveAtomic(previousInstance);
        _ = LauncherProfile.BuildFixtureCandidate(ProfileWithoutMarker(previousInstance, "MinePack"),
            Path.Combine(tempRoot, "new-instance"), TestPackRelease.MinecraftVersion, TestPackRelease.FabricLoaderVersion);
        var priorVanillaPlusInstance = Path.Combine(tempRoot, "owned-instance", "instances",
            "test-pack-0.10.0-" + TestPackRelease.PriorArtifactSha512[..12].ToLowerInvariant());
        new InstallationManifest
        {
            PackVersion = "0.10.0",
            MinecraftVersion = TestPackRelease.MinecraftVersion,
            FabricLoaderVersion = TestPackRelease.FabricLoaderVersion,
            PackArchiveSha512 = TestPackRelease.PriorArtifactSha512
        }.SaveAtomic(priorVanillaPlusInstance);
        _ = LauncherProfile.BuildFixtureCandidate(ProfileWithoutMarker(priorVanillaPlusInstance, "MinePack"),
            Path.Combine(tempRoot, "new-instance"), TestPackRelease.MinecraftVersion, TestPackRelease.FabricLoaderVersion);
        var previousVanilla2PlusInstance = Path.Combine(tempRoot, "owned-instance", "instances",
            "test-pack-0.11.0-" + Vanilla2PlusRelease.OriginalArtifactSha512[..12].ToLowerInvariant());
        new InstallationManifest
        {
            PackVersion = "0.11.0",
            MinecraftVersion = TestPackRelease.MinecraftVersion,
            FabricLoaderVersion = TestPackRelease.FabricLoaderVersion,
            PackArchiveSha512 = Vanilla2PlusRelease.OriginalArtifactSha512
        }.SaveAtomic(previousVanilla2PlusInstance);
        _ = LauncherProfile.BuildFixtureCandidate(ProfileWithoutMarker(previousVanilla2PlusInstance, "MinePack"),
            Path.Combine(tempRoot, "new-instance"), TestPackRelease.MinecraftVersion, TestPackRelease.FabricLoaderVersion);
        var previousVanilla2Plus12Instance = Path.Combine(tempRoot, "owned-instance", "instances",
            "test-pack-0.12.0-" + Vanilla2PlusRelease.LegacyArtifactSha512[..12].ToLowerInvariant());
        new InstallationManifest
        {
            PackVersion = "0.12.0",
            MinecraftVersion = TestPackRelease.MinecraftVersion,
            FabricLoaderVersion = TestPackRelease.FabricLoaderVersion,
            PackArchiveSha512 = Vanilla2PlusRelease.LegacyArtifactSha512
        }.SaveAtomic(previousVanilla2Plus12Instance);
        _ = LauncherProfile.BuildFixtureCandidate(ProfileWithoutMarker(previousVanilla2Plus12Instance, "MinePack"),
            Path.Combine(tempRoot, "new-instance"), TestPackRelease.MinecraftVersion, TestPackRelease.FabricLoaderVersion);
        var previousVanilla2Plus13Instance = Path.Combine(tempRoot, "owned-instance", "instances",
            "test-pack-0.13.0-" + Vanilla2PlusRelease.PreviousArtifactSha512[..12].ToLowerInvariant());
        new InstallationManifest
        {
            PackVersion = "0.13.0",
            MinecraftVersion = TestPackRelease.MinecraftVersion,
            FabricLoaderVersion = TestPackRelease.FabricLoaderVersion,
            PackArchiveSha512 = Vanilla2PlusRelease.PreviousArtifactSha512
        }.SaveAtomic(previousVanilla2Plus13Instance);
        _ = LauncherProfile.BuildFixtureCandidate(ProfileWithoutMarker(previousVanilla2Plus13Instance, "MinePack"),
            Path.Combine(tempRoot, "new-instance"), TestPackRelease.MinecraftVersion, TestPackRelease.FabricLoaderVersion);
        var priorVanilla2Plus14Instance = Path.Combine(tempRoot, "owned-instance", "instances",
            "test-pack-0.14.0-" + Vanilla2PlusRelease.PriorArtifactSha512[..12].ToLowerInvariant());
        new InstallationManifest
        {
            PackVersion = "0.14.0",
            MinecraftVersion = TestPackRelease.MinecraftVersion,
            FabricLoaderVersion = TestPackRelease.FabricLoaderVersion,
            PackArchiveSha512 = Vanilla2PlusRelease.PriorArtifactSha512
        }.SaveAtomic(priorVanilla2Plus14Instance);
        _ = LauncherProfile.BuildFixtureCandidate(ProfileWithoutMarker(priorVanilla2Plus14Instance, "MinePack"),
            Path.Combine(tempRoot, "new-instance"), TestPackRelease.MinecraftVersion, TestPackRelease.FabricLoaderVersion);
        var guardVanilla2Plus16Instance = Path.Combine(tempRoot, "owned-instance", "instances",
            "test-pack-0.16.0-" + Vanilla2PlusRelease.GuardArtifactSha512[..12].ToLowerInvariant());
        new InstallationManifest
        {
            PackVersion = "0.16.0",
            MinecraftVersion = TestPackRelease.MinecraftVersion,
            FabricLoaderVersion = TestPackRelease.FabricLoaderVersion,
            PackArchiveSha512 = Vanilla2PlusRelease.GuardArtifactSha512
        }.SaveAtomic(guardVanilla2Plus16Instance);
        _ = LauncherProfile.BuildFixtureCandidate(ProfileWithoutMarker(guardVanilla2Plus16Instance, "MinePack"),
            Path.Combine(tempRoot, "new-instance"), TestPackRelease.MinecraftVersion, TestPackRelease.FabricLoaderVersion);
        Equal(markerless, LauncherProfile.RemoveFixtureCandidate(markerless, Path.Combine(tempRoot, "new-instance")),
            "uninstalling a different instance preserves the current MinePack profile");
        var vanilla2PlusInstance = Path.Combine(tempRoot, "owned-instance", "instances",
            "test-pack-" + Vanilla2PlusRelease.PackVersion + "-" + Vanilla2PlusRelease.ArtifactSha512[..12].ToLowerInvariant());
        new InstallationManifest
        {
            PackVersion = Vanilla2PlusRelease.PackVersion,
            MinecraftVersion = TestPackRelease.MinecraftVersion,
            FabricLoaderVersion = TestPackRelease.FabricLoaderVersion,
            PackArchiveSha512 = Vanilla2PlusRelease.ArtifactSha512
        }.SaveAtomic(vanilla2PlusInstance);
        var vanilla2PlusProfile = LauncherProfile.BuildFixtureCandidate(ProfileWithoutMarker(vanilla2PlusInstance, "MinePack"),
            Path.Combine(tempRoot, "new-instance"), TestPackRelease.MinecraftVersion, TestPackRelease.FabricLoaderVersion);
        using (var parsed = JsonDocument.Parse(vanilla2PlusProfile))
            Equal(Path.GetFullPath(Path.Combine(tempRoot, "new-instance")),
                parsed.RootElement.GetProperty("profiles").GetProperty(LauncherProfile.ProfileKey).GetProperty("gameDir").GetString(),
                "markerless Launcher recognizes the exact Vanilla 2 Plus manifest");
        using (var parsed = JsonDocument.Parse(LauncherProfile.RemoveFixtureCandidate(ProfileWithoutMarker(vanilla2PlusInstance, "MinePack"), vanilla2PlusInstance)))
            True(!parsed.RootElement.GetProperty("profiles").TryGetProperty(LauncherProfile.ProfileKey, out _),
                "Vanilla 2 Plus profile can be removed only while its own manifest exists");
        try
        {
            _ = LauncherProfile.BuildFixtureCandidate(ProfileWithoutMarker(Path.Combine(tempRoot, "foreign-instance")),
                Path.Combine(tempRoot, "new-instance"), TestPackRelease.MinecraftVersion, TestPackRelease.FabricLoaderVersion);
            throw new InvalidOperationException("Expected unowned markerless profile rejection.");
        }
        catch (InstallerException ex) when (ex.Code == "LAUNCHER_PROFILE_CONFLICT") { }
        try
        {
            _ = LauncherProfile.BuildFixtureCandidate(ProfileWithoutMarker(oldInstance, "Someone else's profile"),
                Path.Combine(tempRoot, "new-instance"), TestPackRelease.MinecraftVersion, TestPackRelease.FabricLoaderVersion);
            throw new InvalidOperationException("Expected foreign-name profile rejection.");
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
        var profileHash = HashBytes(Bytes($"{{\"id\":\"{versionId}\",\"inheritsFrom\":\"26.2\"}}"));
        var archive = CreateFabricProfileArchive(versionId,
            $"{{\"id\":\"{versionId}\",\"inheritsFrom\":\"26.2\",\"releaseTime\":\"2026-09-24\",\"time\":\"2026-09-24\"}}");

        var hydratedJar = Bytes("official Minecraft client JAR fixture");
        using var service = new FabricLauncherService(launcherRoot,
            new DelegateHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) }),
            profileHash, HashBytes(hydratedJar), hydratedJar.Length, ensureLauncherClosed: static () => { });
        await service.ConfigureAsync(gameDirectory);
        await service.ConfigureAsync(gameDirectory);
        var version = Path.Combine(launcherRoot, "versions", versionId);
        True(File.Exists(Path.Combine(version, versionId + ".json")), "Fabric version JSON installed");
        True(File.Exists(Path.Combine(version, versionId + ".jar")), "Fabric version dummy JAR installed");
        var versionJar = Path.Combine(version, versionId + ".jar");
        File.WriteAllBytes(versionJar, hydratedJar);
        await service.ConfigureAsync(gameDirectory);
        Equal(HashBytes(hydratedJar), HashFile(versionJar), "Launcher-filled official client JAR remains unchanged");
        var retimedArchive = CreateFabricProfileArchive(versionId,
            $"{{\"time\":\"2026-09-26\",\"releaseTime\":\"2026-09-26\",\"inheritsFrom\":\"26.2\",\"id\":\"{versionId}\"}}");
        using (var retimedService = new FabricLauncherService(launcherRoot,
                   new DelegateHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(retimedArchive) }),
                   profileHash, HashBytes(hydratedJar), hydratedJar.Length, ensureLauncherClosed: static () => { }))
            await retimedService.ConfigureAsync(gameDirectory);
        Equal(HashBytes(hydratedJar), HashFile(versionJar), "changed Fabric timestamps leave the official client JAR intact");
        var alteredArchive = CreateFabricProfileArchive(versionId,
            $"{{\"id\":\"{versionId}\",\"inheritsFrom\":\"26.2\",\"releaseTime\":\"2026-09-26\",\"time\":\"2026-09-26\",\"libraries\":[{{\"name\":\"foreign:library:1\"}}]}}");
        var profileBeforeAlteredArchive = File.ReadAllText(profilesPath);
        using (var alteredService = new FabricLauncherService(launcherRoot,
                   new DelegateHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(alteredArchive) }),
                   profileHash, HashBytes(hydratedJar), hydratedJar.Length, ensureLauncherClosed: static () => { }))
        {
            try { await alteredService.ConfigureAsync(gameDirectory); throw new InvalidOperationException("Expected changed Fabric libraries to be rejected."); }
            catch (InstallerException ex) when (ex.Code == "FABRIC_HASH") { }
        }
        Equal(profileBeforeAlteredArchive, File.ReadAllText(profilesPath), "changed Fabric libraries leave Launcher profile untouched");
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
            HashBytes(Bytes("different")), ensureLauncherClosed: static () => { });
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
            profileHash, ensureLauncherClosed: static () => { });
        try { await conflictService.ConfigureAsync(gameDirectory); throw new InvalidOperationException("Expected Launcher conflict rejection."); }
        catch (InstallerException ex) when (ex.Code == "LAUNCHER_PROFILE_CONFLICT") { }
        Equal(conflict, File.ReadAllText(conflictProfiles), "foreign profile preserved after conflict");
        True(!Directory.Exists(Path.Combine(conflictRoot, "versions", versionId)), "new Fabric version rolled back after profile conflict");
        var previousRoot = Path.Combine(tempRoot, "launcher-previous-release");
        Directory.CreateDirectory(previousRoot);
        File.WriteAllText(Path.Combine(previousRoot, "launcher_profiles.json"), input);
        const string previousId = "fabric-loader-0.19.5-26.3";
        var previousProfileHash = HashBytes(Bytes($"{{\"id\":\"{previousId}\",\"inheritsFrom\":\"26.3\"}}"));
        var previousArchive = CreateFabricProfileArchive(previousId,
            $"{{\"id\":\"{previousId}\",\"inheritsFrom\":\"26.3\",\"releaseTime\":\"2026-09-24\",\"time\":\"2026-09-24\"}}");
        using (var previousService = new FabricLauncherService(previousRoot,
                   new DelegateHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(previousArchive) }),
                   expectedSha512: previousProfileHash, minecraftVersion: "26.3", ensureLauncherClosed: static () => { }))
            await previousService.ConfigureAsync(gameDirectory);
        using (var previousProfile = JsonDocument.Parse(File.ReadAllText(Path.Combine(previousRoot, "launcher_profiles.json"))))
            Equal(previousId, previousProfile.RootElement.GetProperty("profiles").GetProperty(LauncherProfile.ProfileKey)
                .GetProperty("lastVersionId").GetString(), "previous release retains its Fabric version");
        Pass("automatic Fabric profile accepts the official Launcher-filled JAR and rejects foreign files");

        var reopenedRoot = Path.Combine(tempRoot, "launcher-reopened");
        Directory.CreateDirectory(reopenedRoot);
        var reopenedProfile = Path.Combine(reopenedRoot, "launcher_profiles.json");
        File.WriteAllText(reopenedProfile, input);
        var guardCalls = 0;
        using var reopenedService = new FabricLauncherService(reopenedRoot,
            new DelegateHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) }),
            profileHash, ensureLauncherClosed: () =>
            {
                if (++guardCalls == 2)
                    throw new InstallerException("LAUNCHER_RUNNING", "fixture: Launcher reopened");
            });
        try { await reopenedService.ConfigureAsync(gameDirectory); throw new InvalidOperationException("Expected reopened Launcher rejection."); }
        catch (InstallerException ex) when (ex.Code == "LAUNCHER_RUNNING") { }
        Equal(2, guardCalls, "Launcher is rechecked immediately before profile write");
        Equal(input, File.ReadAllText(reopenedProfile), "reopened Launcher leaves profile untouched");
        True(!Directory.Exists(Path.Combine(reopenedRoot, "versions", versionId)), "Fabric staging is rolled back when Launcher reopens");

        var ambiguousRoot = Path.Combine(tempRoot, "launcher-ambiguous-profiles");
        Directory.CreateDirectory(ambiguousRoot);
        File.WriteAllText(Path.Combine(ambiguousRoot, "launcher_profiles.json"), input);
        File.WriteAllText(Path.Combine(ambiguousRoot, "launcher_profiles_microsoft_store.json"), input);
        var guardUnexpected = false;
        using var ambiguousService = new FabricLauncherService(ambiguousRoot,
            ensureLauncherClosed: () => guardUnexpected = true);
        try { ambiguousService.CheckReady(); throw new InvalidOperationException("Expected ambiguous Launcher profiles rejection."); }
        catch (InstallerException ex) when (ex.Code == "LAUNCHER_PROFILE_UNKNOWN") { }
        True(!guardUnexpected, "ambiguous profile files stop before process checks or mutation");
        Pass("Launcher lifecycle rechecks before profile writes and rejects ambiguous profile files");
    }

    private static async Task VerifyLauncherLifecycleAsync(string tempRoot)
    {
        var storeTarget = new MinecraftLauncherTarget(MinecraftLauncherKind.Store,
            "Microsoft.4297127D64EC6_8wekyb3d8bbwe!Minecraft");
        var storePlatform = new FakeLauncherPlatform([storeTarget]);
        var storeController = new MinecraftLauncherController(storePlatform, TimeSpan.FromMilliseconds(100));
        var discoveredStore = await storeController.CloseBeforeInstallAsync();
        Equal(storeTarget, discoveredStore, "registered Store AUMID is selected when already closed");
        True(storePlatform.Events.Count == 0, "already closed Launcher receives no close request");
        var profileConfigured = false;
        var storeStart = await storeController.ConfigureAndStartAsync(storeTarget, () =>
        {
            storePlatform.Events.Add("profile");
            profileConfigured = true;
            return Task.CompletedTask;
        });
        Equal(MinecraftLauncherStartStatus.Requested, storeStart.Status, "Store Launcher start request succeeds after profile setup");
        True(profileConfigured && storePlatform.Events.IndexOf("profile") < storePlatform.Events.IndexOf("start"),
            "Store Launcher starts only after profile configuration");

        var win32Target = new MinecraftLauncherTarget(MinecraftLauncherKind.Win32, Path.Combine(tempRoot, "MinecraftLauncher.exe"));
        var win32Platform = new FakeLauncherPlatform([win32Target]);
        win32Platform.SetRunning(win32Target, allowClose: true);
        var win32Controller = new MinecraftLauncherController(win32Platform, TimeSpan.FromMilliseconds(100));
        var selectedWin32 = await win32Controller.CloseBeforeInstallAsync();
        Equal(win32Target, selectedWin32, "registered Win32 path is selected");
        win32Platform.Events.Add("install");
        True(win32Platform.Events.IndexOf("close") < win32Platform.Events.IndexOf("exit") &&
            win32Platform.Events.IndexOf("exit") < win32Platform.Events.IndexOf("install"),
            "installation begins only after graceful exit is confirmed");

        var storeProfileRoot = Path.Combine(tempRoot, "store-with-standard-profile");
        Directory.CreateDirectory(storeProfileRoot);
        File.WriteAllText(Path.Combine(storeProfileRoot, "launcher_profiles.json"), "{}");
        using var storeProfileService = new FabricLauncherService(storeProfileRoot, ensureLauncherClosed: static () => { });
        storeProfileService.CheckProfileReady();
        Equal(storeTarget, await storeController.CloseBeforeInstallAsync(),
            "registered Store Launcher may use launcher_profiles.json");

        var timeoutPlatform = new FakeLauncherPlatform([win32Target]);
        timeoutPlatform.SetRunning(win32Target, allowClose: false);
        var timeoutController = new MinecraftLauncherController(timeoutPlatform, TimeSpan.FromMilliseconds(30));
        var installStarted = false;
        try
        {
            await timeoutController.CloseBeforeInstallAsync();
            installStarted = true;
            throw new InvalidOperationException("Expected Launcher close timeout.");
        }
        catch (InstallerException ex) when (ex.Code == "LAUNCHER_CLOSE_TIMEOUT") { }
        True(!installStarted, "Launcher timeout or refusal prevents install callback");

        var bothVariantsPlatform = new FakeLauncherPlatform([storeTarget, win32Target]);
        try
        {
            await new MinecraftLauncherController(bothVariantsPlatform).CloseBeforeInstallAsync();
            throw new InvalidOperationException("Expected ambiguous Launcher target rejection.");
        }
        catch (InstallerException ex) when (ex.Code == "LAUNCHER_TARGET_AMBIGUOUS") { }
        bothVariantsPlatform.SetRunning(storeTarget, allowClose: true);
        Equal(storeTarget, await new MinecraftLauncherController(bothVariantsPlatform).CloseBeforeInstallAsync(),
            "one active official Launcher resolves registered variants");

        var secondWin32Target = new MinecraftLauncherTarget(MinecraftLauncherKind.Win32,
            Path.Combine(tempRoot, "other", "MinecraftLauncher.exe"));
        var ambiguousPlatform = new FakeLauncherPlatform([win32Target, secondWin32Target]);
        try
        {
            await new MinecraftLauncherController(ambiguousPlatform).CloseBeforeInstallAsync();
            throw new InvalidOperationException("Expected ambiguous Launcher target rejection.");
        }
        catch (InstallerException ex) when (ex.Code == "LAUNCHER_TARGET_AMBIGUOUS") { }

        // Simulates a matching process whose MainModule path cannot be read.
        var unknownPlatform = new FakeLauncherPlatform([win32Target]) { HasUnknownProcess = true };
        try
        {
            await new MinecraftLauncherController(unknownPlatform).CloseBeforeInstallAsync();
            throw new InvalidOperationException("Expected unknown process rejection.");
        }
        catch (InstallerException ex) when (ex.Code == "LAUNCHER_IDENTITY_UNKNOWN") { }
        True(unknownPlatform.Events.Count == 0, "unidentified process with an unavailable path blocks changes without being closed");

        try
        {
            await storeController.ConfigureAndStartAsync(storeTarget,
                () => Task.FromException(new InvalidOperationException("profile failed")));
            throw new InvalidOperationException("Expected profile setup failure.");
        }
        catch (InvalidOperationException ex) when (ex.Message == "profile failed") { }
        Equal(1, storePlatform.Events.Count(eventName => eventName == "start"),
            "profile setup failure does not issue another Launcher start request");

        var failedStartPlatform = new FakeLauncherPlatform([win32Target]) { FailStart = true };
        var failedStart = await new MinecraftLauncherController(failedStartPlatform).ConfigureAndStartAsync(
            win32Target, static () => Task.CompletedTask);
        Equal(MinecraftLauncherStartStatus.Failed, failedStart.Status, "start failure is returned separately from install success");
        True(failedStart.Diagnostic?.StartsWith("LAUNCHER_START_FAILED", StringComparison.Ordinal) == true,
            "start failure has a diagnostic code");

        var unavailablePlatform = new FakeLauncherPlatform([]);
        var unavailable = await new MinecraftLauncherController(unavailablePlatform).ConfigureAndStartAsync(
            null, static () => Task.CompletedTask);
        Equal(MinecraftLauncherStartStatus.TargetUnavailable, unavailable.Status,
            "an unregistered target keeps installation successful and requests manual opening");
        True(unavailablePlatform.Events.Count == 0, "no unverified target is launched");
        Pass("Store and Win32 Launcher targets close, confirm exit, and report start outcomes using isolated fixtures");
    }

    private static async Task VerifyOfficialFabricDownloadAsync(string tempRoot)
    {
        var launcherRoot = Path.Combine(tempRoot, "official-fabric-fixture");
        Directory.CreateDirectory(launcherRoot);
        var profilesPath = Path.Combine(launcherRoot, "launcher_profiles.json");
        File.WriteAllText(profilesPath, "{\"profiles\":{\"vanilla\":{\"name\":\"Original\"}}}");
        using var service = new FabricLauncherService(launcherRoot, ensureLauncherClosed: static () => { });
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
        using var service = new FabricLauncherService(launcherRoot, ensureLauncherClosed: static () => { });
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

    private static byte[] CreateFabricProfileArchive(string versionId, string json)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var writer = new StreamWriter(zip.CreateEntry($"{versionId}/{versionId}.json").Open()))
                writer.Write(json);
            zip.CreateEntry($"{versionId}/{versionId}.jar");
        }
        return memory.ToArray();
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

    private sealed class FakeLauncherPlatform(IReadOnlyList<MinecraftLauncherTarget> targets) : IMinecraftLauncherPlatform
    {
        private readonly Dictionary<MinecraftLauncherTarget, FakeLauncherProcessState> _running = [];
        public List<string> Events { get; } = [];
        public bool HasUnknownProcess { get; set; }
        public bool FailStart { get; set; }

        public IReadOnlyList<MinecraftLauncherTarget> FindTargets() => targets;

        public IReadOnlyList<IMinecraftLauncherProcess> FindRunningProcesses(MinecraftLauncherTarget target) =>
            _running.TryGetValue(target, out var state) && !state.HasExited
                ? [new FakeLauncherProcess(state)]
                : [];

        public bool HasUnidentifiedLauncherProcess() => HasUnknownProcess;

        public void Start(MinecraftLauncherTarget target)
        {
            Events.Add("start");
            if (FailStart) throw new InvalidOperationException("fixture launch failure");
        }

        public void SetRunning(MinecraftLauncherTarget target, bool allowClose) =>
            _running[target] = new FakeLauncherProcessState(Events, allowClose);
    }

    private sealed class FakeLauncherProcessState(List<string> events, bool allowClose)
    {
        public bool HasExited { get; set; }
        public bool CloseRequested { get; private set; }

        public bool RequestClose()
        {
            events.Add("close");
            CloseRequested = allowClose;
            return allowClose;
        }

        public Task WaitForExitAsync(CancellationToken cancellationToken)
        {
            events.Add("wait");
            if (!CloseRequested) return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            HasExited = true;
            events.Add("exit");
            return Task.CompletedTask;
        }
    }

    private sealed class FakeLauncherProcess(FakeLauncherProcessState state) : IMinecraftLauncherProcess
    {
        public bool HasExited => state.HasExited;
        public bool RequestClose() => state.RequestClose();
        public Task WaitForExitAsync(CancellationToken cancellationToken) => state.WaitForExitAsync(cancellationToken);
        public void Dispose() { }
    }

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
