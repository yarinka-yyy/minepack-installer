using MinePack.Core;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class PackRebalanceChecks
{
    public static void Run(Action<bool, string> check)
    {
        var vanilla = Open(TestPackRelease.ArtifactRelativePath, TestPackRelease.ArtifactSha512);
        var frontier = Open(Vanilla2PlusRelease.ArtifactRelativePath, Vanilla2PlusRelease.ArtifactSha512);
        var previousVanilla = Open("releases/test-pack/" + TestPackRelease.PreviousReleaseArtifactFileName,
            TestPackRelease.PreviousReleaseArtifactSha512);
        var mouseVanilla = Open("releases/test-pack/" + TestPackRelease.MouseArtifactFileName,
            TestPackRelease.MouseArtifactSha512);
        var previousFrontier = Open("releases/vanilla-2-plus/" + Vanilla2PlusRelease.PreviousReleaseArtifactFileName,
            Vanilla2PlusRelease.PreviousReleaseArtifactSha512);
        var formerVanilla = Open("releases/test-pack/" + TestPackRelease.FormerCurrentArtifactFileName,
            TestPackRelease.FormerCurrentArtifactSha512);
        var formerFrontier = Open("releases/vanilla-2-plus/" + Vanilla2PlusRelease.FormerCurrentArtifactFileName,
            Vanilla2PlusRelease.FormerCurrentArtifactSha512);
        var formerOptimizedVanilla = Open("releases/test-pack/" + TestPackRelease.FormerOptimizedArtifactFileName,
            TestPackRelease.FormerOptimizedArtifactSha512);
        var formerOptimizedFrontier = Open("releases/vanilla-2-plus/" + Vanilla2PlusRelease.FormerOptimizedArtifactFileName,
            Vanilla2PlusRelease.FormerOptimizedArtifactSha512);
        check(vanilla.VersionId == "0.18.6" && previousVanilla.VersionId == "0.18.4" && frontier.VersionId == "0.19.11" &&
              formerOptimizedVanilla.VersionId == "0.18.3" && formerOptimizedFrontier.VersionId == "0.19.9" &&
              vanilla.MinecraftVersion == "26.2" && frontier.MinecraftVersion == vanilla.MinecraftVersion &&
              vanilla.FabricLoaderVersion == "0.19.5" && frontier.FabricLoaderVersion == vanilla.FabricLoaderVersion,
            "new pack pins preserve Minecraft 26.2 and Fabric 0.19.5");
        check(SameVersionOnlyExport(formerOptimizedVanilla, previousVanilla,
                  "releases/test-pack/" + TestPackRelease.FormerOptimizedArtifactFileName,
                  "releases/test-pack/" + TestPackRelease.PreviousReleaseArtifactFileName) &&
              SameVersionOnlyExport(formerOptimizedFrontier, previousFrontier,
                  "releases/vanilla-2-plus/" + Vanilla2PlusRelease.FormerOptimizedArtifactFileName,
                  "releases/vanilla-2-plus/" + Vanilla2PlusRelease.PreviousReleaseArtifactFileName),
            "new Vanilla Plus and Frontier exports differ from their 1.6.3 pins only by pack version");

        foreach (var release in new[] { vanilla, mouseVanilla, previousVanilla, formerOptimizedVanilla,
                     frontier, previousFrontier, formerOptimizedFrontier })
            check(InstalledInstanceCatalog.UsesCurrentPerformanceDefaults(release.VersionId, release.MinecraftVersion,
                    release.FabricLoaderVersion, release.ArchiveSha512),
                $"{release.VersionId} retains its pinned graphics and memory defaults");

        var vanillaFiles = vanilla.Files.ToDictionary(file => file.Path, StringComparer.Ordinal);
        var frontierFiles = frontier.Files.ToDictionary(file => file.Path, StringComparer.Ordinal);
        var macawPaths = new HashSet<string>(StringComparer.Ordinal)
        {
            "mods/mcw-windows-2.4.2-mc26.2fabric.jar",
            "mods/mcw-fences-1.2.1-mc26.2fabric.jar",
            "mods/mcw-bridges-3.1.2-mc26.2fabric.jar",
            "mods/mcw-doors-1.1.5-mc26.2fabric.jar",
            "mods/mcw-stairs-1.0.2-mc26.2fabric.jar"
        };
        check(vanilla.Files.Count == 74 && frontier.Files.Count == 78 &&
              vanilla.Overrides.Count == 15 && frontier.Overrides.Count == 15 &&
              !vanillaFiles.Keys.Any(path => path.Contains("mcw-", StringComparison.OrdinalIgnoreCase)) &&
              frontierFiles.Keys.Except(vanillaFiles.Keys, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal).SetEquals(macawPaths),
            "Frontier adds exactly the five Macaw downloads to the shared Vanilla Plus base");
        var optimizedPaths = new HashSet<string>(StringComparer.Ordinal)
        {
            "mods/BadOptimizations-2.4.1-26.2-fabric.jar",
            "mods/moreculling-fabric-26.2-1.8.1.jar",
            "mods/lithium-fabric-0.25.3+mc26.2.jar"
        };
        check(AddedPaths(formerVanilla, previousVanilla).SetEquals(optimizedPaths) &&
              AddedPaths(formerFrontier, previousFrontier).SetEquals(optimizedPaths) &&
              RemovedPaths(formerVanilla, previousVanilla).Count == 0 && RemovedPaths(formerFrontier, previousFrontier).Count == 0 &&
              SameFiles(formerVanilla, previousVanilla, optimizedPaths) && SameFiles(formerFrontier, previousFrontier, optimizedPaths),
            "each new current archive adds only the three pinned performance downloads and retains every former-current pin");
        VerifyPerformancePins(check, vanilla, TestPackRelease.ArtifactRelativePath);
        VerifyPerformancePins(check, frontier, Vanilla2PlusRelease.ArtifactRelativePath);
        const string mousePath = "mods/MouseTweaks-fabric-mc26.2-2.31.jar";
        check(AddedPaths(previousVanilla, mouseVanilla).SetEquals([mousePath]) &&
              RemovedPaths(previousVanilla, mouseVanilla).Count == 0 &&
              SameFiles(previousVanilla, mouseVanilla, new HashSet<string>(StringComparer.Ordinal)) &&
              mouseVanilla.Overrides.Count == previousVanilla.Overrides.Count && !frontierFiles.ContainsKey(mousePath),
            "Vanilla Plus adds only Mouse Tweaks and preserves every prior download and override; Frontier is unchanged");
        var mouse = vanillaFiles[mousePath];
        using var mouseArchive = ZipFile.OpenRead(Path.Combine(FindRepositoryRoot(), TestPackRelease.ArtifactRelativePath));
        using var mouseIndexStream = mouseArchive.GetEntry("modrinth.index.json")!.Open();
        using var mouseIndex = JsonDocument.Parse(mouseIndexStream);
        var mouseEnvironment = mouseIndex.RootElement.GetProperty("files").EnumerateArray()
            .Single(item => item.GetProperty("path").GetString() == mousePath).GetProperty("env");
        check(mouse.Size == 75872 && mouse.Sha512.Equals("bc77723465dc4aef7a3a8fa2fea3641df1e6ba7cc8a2d86cd2659ef65ca661f6a763d3997fdaed1f8ff6c78a4c6061f8074160ca3162a29021735779a6c55e46", StringComparison.OrdinalIgnoreCase) &&
              mouseEnvironment.GetProperty("client").GetString() == "required" &&
              mouseEnvironment.GetProperty("server").GetString() == "unsupported" &&
              mouse.Downloads.Select(uri => uri.AbsoluteUri).SequenceEqual(["https://cdn.modrinth.com/data/aC3cM3Vq/versions/jOiTBIaB/MouseTweaks-fabric-mc26.2-2.31.jar"]),
            "Mouse Tweaks 2.31 is pinned to the exact Fabric 26.2 release URL, hash, size and client-only environment");
        check(vanillaFiles.Where(pair => pair.Key != mousePath).All(pair => frontierFiles.TryGetValue(pair.Key, out var other) &&
              pair.Value.Sha512 == other.Sha512 && pair.Value.Size == other.Size &&
              pair.Value.Downloads.Select(uri => uri.AbsoluteUri).SequenceEqual(other.Downloads.Select(uri => uri.AbsoluteUri))),
            "download metadata and hashes are identical for every shared file");
        check(vanilla.Overrides.Select(file => file.Path).ToHashSet(StringComparer.Ordinal)
              .SetEquals(frontier.Overrides.Select(file => file.Path)) &&
              vanilla.Overrides.All(item => frontier.Overrides.Any(other => other.Path == item.Path && other.Sha512 == item.Sha512 && other.Size == item.Size)),
            "Vanilla Plus and Frontier preserve the same override bytes and paths");

        var vanillaCatalog = PackCatalog.VanillaPlusGroups.SelectMany(group => group.Items).ToArray();
        var frontierCatalog = PackCatalog.Vanilla2PlusGroups.SelectMany(group => group.Items).ToArray();
        check(PackCatalog.Items.Count(item => item.FilePath == mousePath &&
                  item.ModrinthUrl?.AbsoluteUri == "https://modrinth.com/mod/aC3cM3Vq") == 1,
            "Mouse Tweaks is present once in the shared Modrinth link allowlist");
        check(Counts(vanillaCatalog) == (72, 13, 1) && Counts(frontierCatalog) == (76, 13, 1),
            "localized pack catalogs represent 72/13/1 and 76/13/1 files");
        check(CatalogPaths(vanillaCatalog).SetEquals(ManagedCatalogPaths(vanilla)) &&
              CatalogPaths(frontierCatalog).SetEquals(ManagedCatalogPaths(frontier)),
            "both visible catalogs match their archive files and local pack overrides");
        check(frontierCatalog.Where(item => item.Name.StartsWith("Macaw's ", StringComparison.Ordinal)).Count() == 5 &&
              vanillaCatalog.All(item => !item.Name.StartsWith("Macaw's ", StringComparison.Ordinal)),
            "only Frontier lists the five Macaw add-ons");

        var pinnedDownloads = new[]
        {
            (Path: "mods/betterstats-5.5.6+fn-26.2.jar", Hash: "aa57b6f8893ff0f1dda8b79ce08e9cb369d245fad0ba26e65eb5a67f299172b3073f0e23080f8e648b32d27a0d68566516efb83498138754eef7544967f159a1", Version: "ga3HGfK0"),
            (Path: "mods/tcdcommons-5.5.6+fn-26.2.jar", Hash: "0d1dc4b14b42d516a35ddb5b458c708981cfb2bf8c0bf89e4a5ef79d9e3fc8ee0d2787b9b82ad5ce3b5facf826fdf0e716880a4533d192941ef5e8c3e136c5e2", Version: "NM3aWYRR"),
            (Path: "resourcepacks/3D Default 1.21.2+ v1.16.0.zip", Hash: "379c81f61bc8dc603ed86f2f54c83576382df46908baff4ba1f5fab8755ee861475dfa7d8e99874f50d02569dc8a1268be39434a4da182738e210038f8b4d3ac", Version: "3Z2r4oP7")
        };
        foreach (var pin in pinnedDownloads)
            check(vanillaFiles.TryGetValue(pin.Path, out var file) && file.Sha512.Equals(pin.Hash, StringComparison.OrdinalIgnoreCase) &&
                  file.Downloads.Any(uri => uri.Host == "cdn.modrinth.com" && uri.AbsoluteUri.Contains(pin.Version, StringComparison.Ordinal)),
                $"the exact Modrinth download pin is present: {pin.Path}");

        check(TestPackRelease.InitialResourcePacks.Length == 13 &&
              Vanilla2PlusRelease.InitialResourcePacks.SequenceEqual(TestPackRelease.InitialResourcePacks) &&
              TestPackRelease.InitialResourcePacks[0] == "3D Default 1.21.2+ v1.16.0.zip",
            "both new releases share the same 13-entry initial resource-pack order with 3D Default first");

        VerifyVisualAdditions(check, mouseVanilla, vanilla, TestPackRelease.ArtifactRelativePath);
        VerifyVisualAdditions(check, previousFrontier, frontier, Vanilla2PlusRelease.ArtifactRelativePath);
        VerifyDoorResourceLayers(check);
    }

    private static void VerifyVisualAdditions(Action<bool, string> check, PackArchive before, PackArchive after, string relativePath)
    {
        var pins = new[]
        {
            (Path: "mods/smoothloadingscreen-1.0.6+26.2.jar", Hash: "ed59dabb5400ff78422f99314cbfca126592600a94c70620f83f6cfea6aade45cdbcec26863a136f19e47df307878b02458a5f342620c646db4ff7e4085ab8ec", Version: "tUscvsSM"),
            (Path: "mods/last-world-panorama-26.2.jar", Hash: "91e0cda63957f5663e4e1dba72b905b3be284d13db0f5895e5d58e682459049440aeae64cbfd003b9a9c72b0b1d79ddc4312d48773923edbae4c85bbb3988155", Version: "LFWsTg89"),
            (Path: "mods/bettersmoke-0.1.2+26.2.jar", Hash: "4fbdaf85f3da2c6acbb09cb099410dc03cc36f003b0d8929046a092dc8027ff52b3f5109f7911b2ae4c607c8a15c7a33a061a1b2ab82dab4be7c9b60ae472045", Version: "8HF8DC7x"),
            (Path: "mods/fwa+26.2-1.3.4.jar", Hash: "3c65f24eb900cecd60b7684268d7764fc8820a5db93685a36b713c34546feb7ca4c71019af9a61539260dee3250b5cb72415a651c840b94016543a8d9c5409c9", Version: "cSXDNo3Z"),
            (Path: "mods/yet_another_config_lib_v3-3.9.7+26.2-fabric.jar", Hash: "f36d91a9506ad9addbaaeecaea01d2b0cb5b060f52486b17688746cb597f0f3b23a80f8441f7a73a9cb8cde85af8809fe54825984b56345325359d505a80e7f2", Version: "DoR7RYgi")
        };
        check(AddedPaths(before, after).SetEquals(pins.Select(pin => pin.Path)) &&
              RemovedPaths(before, after).Count == 0 && SameFiles(before, after, new HashSet<string>(StringComparer.Ordinal)),
            "visual update adds exactly the four requested downloads plus YACL and retains all previous file bytes");
        using var archive = ZipFile.OpenRead(Path.Combine(FindRepositoryRoot(), relativePath));
        using var indexStream = archive.GetEntry("modrinth.index.json")!.Open();
        using var index = JsonDocument.Parse(indexStream);
        foreach (var pin in pins)
        {
            var file = after.Files.Single(file => file.Path == pin.Path);
            var environment = index.RootElement.GetProperty("files").EnumerateArray()
                .Single(item => item.GetProperty("path").GetString() == pin.Path).GetProperty("env");
            check(file.Sha512.Equals(pin.Hash, StringComparison.OrdinalIgnoreCase) &&
                  file.Downloads.Count == 1 && file.Downloads[0].Host == "cdn.modrinth.com" &&
                  file.Downloads[0].AbsoluteUri.Contains("/versions/" + pin.Version + "/", StringComparison.Ordinal) &&
                  environment.GetProperty("client").GetString() == "required" && environment.GetProperty("server").GetString() == "unsupported",
                "exact client-only visual/dependency pin: " + pin.Path);
        }
        const string spatialPath = "mods/spatial-gui-1.9.1+minepack.1+26.2-fabric.jar";
        var addedOverrides = after.Overrides.Select(file => file.Path).Except(before.Overrides.Select(file => file.Path)).ToArray();
        check(addedOverrides.SequenceEqual([spatialPath]) && after.Overrides.Count == before.Overrides.Count + 1 &&
              after.Overrides.Single(file => file.Path == spatialPath).Sha512.Equals(
                  "3F045187D8EE0FDEC49F8A7E5DC6E18F0C1B471D05540C0CF0DB4A883B389ECEC9F3A016A6AA38749E55584DA13C11F399514EF42D7B1A237E4B7A668B28E114", StringComparison.OrdinalIgnoreCase),
            "only the fixed, hash-pinned Spatial GUI JAR is added to embedded overrides");
        check(after.Files.Select(file => file.Path).Concat(after.Overrides.Select(file => file.Path)).All(path =>
                  !path.Contains("do_a_barrel_roll", StringComparison.OrdinalIgnoreCase) && !path.Contains("cicada", StringComparison.OrdinalIgnoreCase)),
            "Do a Barrel Roll and its exclusive CICADA dependency are excluded");
    }

    private static void VerifyPerformancePins(Action<bool, string> check, PackArchive pack, string relativePath)
    {
        var expected = new[]
        {
            (Path: "mods/BadOptimizations-2.4.1-26.2-fabric.jar", Hash: "f91c409d4ce68d027ac0a11b6d2828c5f6d057e749ff0ce596f3a84a65fc1e3905b08f4d97a21a7302e2bfaf4ff865907272bd9c7ffa8487701b22e40dfe2f97", Url: "https://cdn.modrinth.com/data/g96Z4WVZ/versions/JmPs4Wie/BadOptimizations-2.4.1-26.2-fabric.jar", Server: "unsupported"),
            (Path: "mods/moreculling-fabric-26.2-1.8.1.jar", Hash: "9c331ba5f2ce9c322c41aa577f1029f3420d5507d0d942aadbb43fb9bd82e7da6f72e3a0b26c4ad96394669a99a7feb512b5119b3095fde2e0a437bf68c08b40", Url: "https://cdn.modrinth.com/data/51shyZVL/versions/D5oVCouK/moreculling-fabric-26.2-1.8.1.jar", Server: "unsupported"),
            (Path: "mods/lithium-fabric-0.25.3+mc26.2.jar", Hash: "148b638f3c6229fbaf487120a2344a0af5e411a5aa6533d5db9d75da0a8c0d8304f63eb4cca13f4d03b2c9b4c23d559dd74c1d832422ef8a3087bd005e62a8bd", Url: "https://cdn.modrinth.com/data/gvQqBUqZ/versions/f7vZ0VWU/lithium-fabric-0.25.3%2Bmc26.2.jar", Server: "required")
        };
        var files = pack.Files.ToDictionary(file => file.Path, StringComparer.Ordinal);
        var path = Path.Combine(AppContext.BaseDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
        using var archive = ZipFile.OpenRead(path);
        using var indexStream = archive.GetEntry("modrinth.index.json")!.Open();
        using var index = JsonDocument.Parse(indexStream);
        var indexed = index.RootElement.GetProperty("files").EnumerateArray()
            .ToDictionary(item => item.GetProperty("path").GetString()!, StringComparer.Ordinal);
        foreach (var pin in expected)
        {
            var exactDownload = files.TryGetValue(pin.Path, out var file) && file.Sha512.Equals(pin.Hash, StringComparison.OrdinalIgnoreCase) &&
                                file.Downloads.Select(uri => uri.AbsoluteUri).SequenceEqual([pin.Url], StringComparer.Ordinal);
            var exactEnvironment = indexed.TryGetValue(pin.Path, out var item) &&
                                   item.GetProperty("env").GetProperty("client").GetString() == "required" &&
                                   item.GetProperty("env").GetProperty("server").GetString() == pin.Server;
            check(exactDownload && exactEnvironment, $"{pack.VersionId} has the exact pinned {pin.Path} hash, URL, and client/server side");
        }
    }

    private static HashSet<string> AddedPaths(PackArchive before, PackArchive after) =>
        after.Files.Select(file => file.Path).Except(before.Files.Select(file => file.Path), StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);

    private static HashSet<string> RemovedPaths(PackArchive before, PackArchive after) =>
        before.Files.Select(file => file.Path).Except(after.Files.Select(file => file.Path), StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);

    private static bool SameFiles(PackArchive before, PackArchive after, IReadOnlySet<string> excluded) =>
        before.Files.Where(file => !excluded.Contains(file.Path)).All(file => after.Files.Any(other => other.Path == file.Path &&
            other.Sha512 == file.Sha512 && other.Size == file.Size &&
            other.Downloads.Select(uri => uri.AbsoluteUri).SequenceEqual(file.Downloads.Select(uri => uri.AbsoluteUri)))) &&
        before.Overrides.All(file => after.Overrides.Any(other => other.Path == file.Path && other.Sha512 == file.Sha512 && other.Size == file.Size));

    private static bool SameVersionOnlyExport(PackArchive before, PackArchive after,
        string beforeRelativePath, string afterRelativePath)
    {
        if (before.Files.Count != after.Files.Count || before.Overrides.Count != after.Overrides.Count ||
            !SameFiles(before, after, new HashSet<string>(StringComparer.Ordinal))) return false;

        var root = FindRepositoryRoot();
        using var beforeArchive = ZipFile.OpenRead(Path.Combine(root, beforeRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        using var afterArchive = ZipFile.OpenRead(Path.Combine(root, afterRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        var beforeNames = beforeArchive.Entries.Select(entry => entry.FullName).Order(StringComparer.Ordinal).ToArray();
        var afterNames = afterArchive.Entries.Select(entry => entry.FullName).Order(StringComparer.Ordinal).ToArray();
        if (!beforeNames.SequenceEqual(afterNames, StringComparer.Ordinal)) return false;
        foreach (var name in beforeNames.Where(name => name != "modrinth.index.json"))
            if (!ReadEntry(beforeArchive, name).AsSpan().SequenceEqual(ReadEntry(afterArchive, name))) return false;

        var beforeIndex = JsonNode.Parse(ReadEntry(beforeArchive, "modrinth.index.json"))!.AsObject();
        var afterIndex = JsonNode.Parse(ReadEntry(afterArchive, "modrinth.index.json"))!.AsObject();
        if (beforeIndex["versionId"]?.GetValue<string>() != before.VersionId ||
            afterIndex["versionId"]?.GetValue<string>() != after.VersionId) return false;
        afterIndex["versionId"] = beforeIndex["versionId"]?.DeepClone();
        return JsonNode.DeepEquals(beforeIndex, afterIndex);
    }

    private static void VerifyDoorResourceLayers(Action<bool, string> check)
    {
        var root = FindRepositoryRoot();
        var validationRoot = Path.Combine(root, "artifacts", "build-1.6.2", "resource-validation");
        var paths = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["official Minecraft 26.2 client"] = Path.Combine(validationRoot, "client-26.2.jar"),
            ["pinned 3D Default archive"] = Path.Combine(validationRoot, "3d-default-1.16.0.zip"),
            ["pinned Remodeled Doors archive"] = Path.Combine(validationRoot, "remodeled-doors-2.2.1.zip"),
            ["xali 26.2 resource pack"] = Path.Combine(root, "pack", "test-pack", "resourcepacks", "xalis-enhanced-vanilla-26.2-minepack.2.zip"),
            ["original door compatibility archive"] = Path.Combine(root, "pack", "test-pack", "resourcepacks", "Remodeled-Doors-26.2-xalis-blockstates.1.zip"),
            ["Frontier original door compatibility archive"] = Path.Combine(root, "pack", "vanilla-2-plus", "resourcepacks", "Remodeled-Doors-26.2-xalis-blockstates.1.zip"),
            ["door compatibility resource pack"] = Path.Combine(root, "pack", "test-pack", "resourcepacks", "Remodeled-Doors-26.2-xalis-blockstates.2.zip"),
            ["Frontier door compatibility resource pack"] = Path.Combine(root, "pack", "vanilla-2-plus", "resourcepacks", "Remodeled-Doors-26.2-xalis-blockstates.2.zip"),
            ["Vanilla Plus current archive"] = Path.Combine(root, TestPackRelease.ArtifactRelativePath.Replace('/', Path.DirectorySeparatorChar)),
            ["Frontier current archive"] = Path.Combine(root, Vanilla2PlusRelease.ArtifactRelativePath.Replace('/', Path.DirectorySeparatorChar))
        };
        var missing = paths.Where(pair => !File.Exists(pair.Value)).Select(pair => $"{pair.Key}: {pair.Value}").ToArray();
        if (missing.Length > 0)
        {
            Console.WriteLine("RESOURCE CHECK PENDING: missing exact local inputs: " + string.Join("; ", missing));
            return;
        }

        check(Hash(paths["official Minecraft 26.2 client"], SHA1.HashData) == "2DC72797ACBC1B63FC16A11C4AC393605F453754",
            "resource check uses the exact official Minecraft 26.2 client");
        check(Hash(paths["pinned 3D Default archive"], SHA512.HashData) ==
              "379C81F61BC8DC603ED86F2F54C83576382DF46908BAFF4BA1F5FAB8755EE861475DFA7D8E99874F50D02569DC8A1268BE39434A4DA182738E210038F8B4D3AC",
            "resource check uses the exact pinned 3D Default archive");
        check(Hash(paths["pinned Remodeled Doors archive"], SHA512.HashData) ==
              "EFE81850001EF80BED6DC880DF1084F5E607C6ED295C88E6FBD2834E273101E275B1275C53CF2B2C40A1E6A98EAD268D57873ACDFC782AE4773E2151DF34C6CE",
            "resource check uses the exact pinned Remodeled Doors archive");
        check(Hash(paths["xali 26.2 resource pack"], SHA256.HashData) ==
              "834BD7A0B37E5C241ED3CB34FED7DEC15BE3172C5B0166A3FE8FA15BC3AA6034" &&
              Hash(paths["original door compatibility archive"], SHA256.HashData) ==
              "AD8B48DD7CC7BD872EE8213B95ACD791D33A6D991D52FF0CB827CC13C9F6C090" &&
              Hash(paths["Frontier original door compatibility archive"], SHA256.HashData) ==
              "AD8B48DD7CC7BD872EE8213B95ACD791D33A6D991D52FF0CB827CC13C9F6C090",
            "the original xali and door-compatibility archives retain their pinned bytes");
        VerifyDoorCompatibilityArchive(check, paths);

        using var client = ZipFile.OpenRead(paths["official Minecraft 26.2 client"]);
        using var versionJson = JsonDocument.Parse(ReadEntry(client, "version.json"));
        var version = versionJson.RootElement;
        var packVersion = version.GetProperty("pack_version");
        var resourceMajor = packVersion.GetProperty("resource_major").GetInt32();
        var resourceMinor = packVersion.GetProperty("resource_minor").GetInt32();
        check(version.GetProperty("id").GetString() == "26.2" && resourceMajor == 88 && resourceMinor == 0,
            "official client identifies Minecraft 26.2 resource format 88.0");

        using var threeD = ZipFile.OpenRead(paths["pinned 3D Default archive"]);
        using var meta = JsonDocument.Parse(ReadEntry(threeD, "pack.mcmeta"));
        var overlays = meta.RootElement.GetProperty("overlays").GetProperty("entries").EnumerateArray().ToArray();
        var active = overlays.Where(item => OverlaySupports(item, resourceMajor, resourceMinor)).ToArray();
        var activeDirectories = active.Select(item => item.GetProperty("directory").GetString()!).ToArray();
        check(activeDirectories.Contains("26_2", StringComparer.Ordinal) &&
              !active.Any(item => item.GetProperty("directory").GetString() == "26_3"),
            "3D Default activates its 26_2 overlay and leaves 26_3 inactive at format 88.0");

        var vanillaAssets = LoadAssets(client, "Minecraft 26.2", null);
        var threeDAssets = LoadAssets(threeD, "3D Default", activeDirectories);
        using var xali = ZipFile.OpenRead(paths["xali 26.2 resource pack"]);
        using var doors = ZipFile.OpenRead(paths["pinned Remodeled Doors archive"]);
        using var compatibility = ZipFile.OpenRead(paths["door compatibility resource pack"]);
        var layers = new[]
        {
            vanillaAssets,
            threeDAssets,
            LoadAssets(xali, "xali's Enhanced Vanilla", null),
            LoadAssets(doors, "Remodeled Doors 3D", null),
            LoadAssets(compatibility, "door compatibility", null)
        };
        var compatibilityBlockstates = layers[^1].Assets.Keys
            .Where(path => path.StartsWith("assets/minecraft/blockstates/", StringComparison.Ordinal) && IsDoorState(path))
            .ToHashSet(StringComparer.Ordinal);
        var expected = Enumerable.Range(0, 9)
            .SelectMany(index => new[] { "assets/minecraft/blockstates/" + DoorWood(index) + "_door.json", "assets/minecraft/blockstates/" + DoorWood(index) + "_trapdoor.json" })
            .ToHashSet(StringComparer.Ordinal);
        check(compatibilityBlockstates.SetEquals(expected),
            "door compatibility archive supplies exactly the 18 vanilla door/trapdoor blockstates");
        check(TestPackRelease.InitialResourcePacks.SequenceEqual(Vanilla2PlusRelease.InitialResourcePacks) &&
              Array.IndexOf(TestPackRelease.InitialResourcePacks, "3D Default 1.21.2+ v1.16.0.zip") == 0 &&
              Array.IndexOf(TestPackRelease.InitialResourcePacks, "xalis-enhanced-vanilla-26.2-minepack.2.zip") <
              Array.IndexOf(TestPackRelease.InitialResourcePacks, "§aRemodeled-Doors§8_§62.2.1.zip") &&
              Array.IndexOf(TestPackRelease.InitialResourcePacks, "§aRemodeled-Doors§8_§62.2.1.zip") <
              Array.IndexOf(TestPackRelease.InitialResourcePacks, "Remodeled-Doors-26.2-xalis-blockstates.2.zip"),
            "3D Default stays below the existing xali → Remodeled Doors → compatibility priority order");

        var missingParents = new HashSet<string>(StringComparer.Ordinal)
        {
            "minecraft:block/door_bottom", "minecraft:block/door_top", "minecraft:block/door_bottom_rh"
        };
        var affectedModels = new List<string>();
        var affectedBlockModels = new List<string>();
        var affectedItemModels = new List<string>();
        foreach (var asset in layers[^2].Assets.Where(item => item.Key.StartsWith("assets/minecraft/models/", StringComparison.Ordinal)))
        {
            using var document = JsonDocument.Parse(asset.Value);
            if (document.RootElement.TryGetProperty("parent", out var parent) && parent.ValueKind == JsonValueKind.String &&
                missingParents.Contains(parent.GetString()!))
            {
                var model = "minecraft:" + asset.Key["assets/minecraft/models/".Length..][..^".json".Length];
                affectedModels.Add(model);
                if (asset.Key.StartsWith("assets/minecraft/models/block/", StringComparison.Ordinal)) affectedBlockModels.Add(model);
                else if (asset.Key.StartsWith("assets/minecraft/models/item/", StringComparison.Ordinal)) affectedItemModels.Add(model);
            }
        }
        var affectedModelErrors = new List<string>();
        foreach (var model in affectedModels)
            if (!ResolveModel(model, layers, new HashSet<string>(StringComparer.Ordinal), out var error))
                affectedModelErrors.Add($"{model}: {error}");
        check(affectedBlockModels.Count == 52 && affectedItemModels.Count == 2 && affectedModels.Count == 54 && affectedModelErrors.Count == 0,
            affectedBlockModels.Count != 52 || affectedItemModels.Count != 2 || affectedModels.Count != 54
                ? $"the pinned Remodeled Doors archive contains 52 affected block models and 2 affected item models (found {affectedBlockModels.Count} block, {affectedItemModels.Count} item, {affectedModels.Count} total)"
                : affectedModelErrors.Count == 0
                    ? "all 54 affected Remodeled Doors models (52 block + 2 item) resolve through the new aliases"
                    : "all affected Remodeled Doors model/texture references resolve: " + string.Join(", ", affectedModelErrors));

        var resolvedModels = 0;
        var modelErrors = new List<string>();
        foreach (var blockstatePath in expected)
        {
            var winner = layers.LastOrDefault(layer => layer.Assets.ContainsKey(blockstatePath));
            if (winner is null || !winner.Name.Equals("door compatibility", StringComparison.Ordinal))
            {
                modelErrors.Add($"unexpected effective blockstate winner for {blockstatePath}: {winner?.Name ?? "missing"}");
                continue;
            }
            foreach (var model in ReadModelIds(winner.Assets[blockstatePath]))
            {
                if (ResolveModel(model, layers, new HashSet<string>(StringComparer.Ordinal), out var error)) resolvedModels++;
                else modelErrors.Add($"{blockstatePath} → {model}: {error}");
            }
        }
        var distinctModelErrors = modelErrors.Select(error => error[(error.IndexOf(": ", StringComparison.Ordinal) + 2)..])
            .Distinct(StringComparer.Ordinal).OrderBy(error => error, StringComparer.Ordinal).ToArray();
        check(distinctModelErrors.Length == 0 && resolvedModels > 0,
            modelErrors.Count == 0
                ? $"the effective compatibility layer wins and {resolvedModels} door models resolve to available textures"
                : "effective door model/texture references resolve: " + string.Join("; ", distinctModelErrors));
        Console.WriteLine($"RESOURCE CHECK PASS: Minecraft 26.2 format {resourceMajor}.{resourceMinor}; active overlays {string.Join(", ", activeDirectories)}; 18 compatibility blockstates; all {affectedBlockModels.Count} block + {affectedItemModels.Count} item Remodeled Doors models resolve; {resolvedModels} effective model references resolve; 3D Default priority is below xali/doors/compatibility.");
    }

    private static void VerifyDoorCompatibilityArchive(Action<bool, string> check, IReadOnlyDictionary<string, string> paths)
    {
        const string newHash = "791B1A98A8B973DE01EC9D1EBC10CBBB1D1D1D94782D3AE8CEC9F23F5227E0D7";
        var oldPath = paths["original door compatibility archive"];
        var vanillaPath = paths["door compatibility resource pack"];
        var frontierPath = paths["Frontier door compatibility resource pack"];
        check(Hash(vanillaPath, SHA256.HashData) == newHash && Hash(frontierPath, SHA256.HashData) == newHash,
            "the new door-compatibility ZIP has the pinned SHA-256 in both current pack trees");

        using var original = ZipFile.OpenRead(oldPath);
        using var rebuilt = ZipFile.OpenRead(vanillaPath);
        using var frontier = ZipFile.OpenRead(frontierPath);
        var originalEntries = original.Entries.ToDictionary(entry => entry.FullName, entry => ReadEntry(original, entry.FullName), StringComparer.Ordinal);
        var rebuiltEntries = rebuilt.Entries.ToDictionary(entry => entry.FullName, entry => ReadEntry(rebuilt, entry.FullName), StringComparer.Ordinal);
        var expectedAliases = new HashSet<string>(StringComparer.Ordinal)
        {
            "assets/minecraft/models/block/door_bottom.json",
            "assets/minecraft/models/block/door_top.json",
            "assets/minecraft/models/block/door_bottom_rh.json"
        };
        var added = rebuiltEntries.Keys.Except(originalEntries.Keys, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        check(added.SetEquals(expectedAliases) && originalEntries.All(pair => rebuiltEntries.TryGetValue(pair.Key, out var contents) &&
              pair.Value.AsSpan().SequenceEqual(contents)) && rebuiltEntries.Count == originalEntries.Count + expectedAliases.Count,
            "the .2 compatibility ZIP preserves every .1 entry and adds exactly the three approved alias paths");

        var aliasesValid = expectedAliases.All(path =>
        {
            if (!rebuiltEntries.TryGetValue(path, out var payload)) return false;
            using var document = JsonDocument.Parse(payload);
            var model = document.RootElement;
            return model.EnumerateObject().Count() == 2 && model.GetProperty("parent").GetString() == "minecraft:block/block" &&
                   model.GetProperty("ambientocclusion").ValueKind == JsonValueKind.False;
        });
        check(aliasesValid, "all three compatibility aliases use only the approved vanilla parent with ambient occlusion disabled");

        var frontierEntries = frontier.Entries.Select(entry => entry.FullName).ToHashSet(StringComparer.Ordinal);
        var sameFrontierPayload = rebuiltEntries.Count == frontierEntries.Count && rebuiltEntries.All(pair =>
        {
            var entry = frontier.GetEntry(pair.Key);
            return entry is not null && pair.Value.AsSpan().SequenceEqual(ReadEntry(frontier, pair.Key));
        });
        check(sameFrontierPayload, "the Vanilla Plus and Frontier compatibility ZIPs have identical entry bytes");

        foreach (var archivePath in new[] { paths["Vanilla Plus current archive"], paths["Frontier current archive"] })
        {
            using var pack = ZipFile.OpenRead(archivePath);
            var archiveEntryNames = pack.Entries.Select(entry => entry.FullName).ToHashSet(StringComparer.Ordinal);
            var compatibilityPath = "overrides/resourcepacks/Remodeled-Doors-26.2-xalis-blockstates.2.zip";
            check(archiveEntryNames.Contains(compatibilityPath) &&
                  !archiveEntryNames.Contains("overrides/resourcepacks/Remodeled-Doors-26.2-xalis-blockstates.1.zip") &&
                  ReadEntry(pack, compatibilityPath).AsSpan().SequenceEqual(File.ReadAllBytes(vanillaPath)),
                $"current archive {Path.GetFileName(archivePath)} embeds only the exact .2 compatibility ZIP");
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (Directory.Exists(Path.Combine(current.FullName, "pack", "test-pack"))) return current.FullName;
        throw new DirectoryNotFoundException("Could not find the MinePack repository root from the smoke-test output directory.");
    }

    private static string Hash(string path, Func<Stream, byte[]> hash)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(hash(stream));
    }

    private static byte[] ReadEntry(ZipArchive archive, string path)
    {
        using var stream = archive.GetEntry(path)?.Open() ?? throw new InvalidDataException($"Missing ZIP entry: {path}");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static ResourceLayer LoadAssets(ZipArchive archive, string name, IEnumerable<string>? overlayDirectories)
    {
        var assets = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.StartsWith("assets/", StringComparison.Ordinal) && IsResourceAsset(entry.FullName))
                assets[entry.FullName] = ReadEntry(archive, entry.FullName);
        }
        foreach (var overlayDirectory in overlayDirectories ?? Array.Empty<string>())
        {
            var prefix = overlayDirectory + "/assets/";
            foreach (var entry in archive.Entries.Where(item => item.FullName.StartsWith(prefix, StringComparison.Ordinal) && IsResourceAsset(item.FullName[(overlayDirectory.Length + 1)..])))
                assets[entry.FullName[(overlayDirectory.Length + 1)..]] = ReadEntry(archive, entry.FullName);
        }
        return new ResourceLayer(name, assets);
    }

    private static bool IsResourceAsset(string path) =>
        (path.Contains("/blockstates/", StringComparison.Ordinal) || path.Contains("/models/", StringComparison.Ordinal)) && path.EndsWith(".json", StringComparison.Ordinal) ||
        path.Contains("/textures/", StringComparison.Ordinal) && path.EndsWith(".png", StringComparison.Ordinal);

    private static bool OverlaySupports(JsonElement overlay, int major, int minor)
    {
        var min = overlay.GetProperty("min_format").EnumerateArray().Select(value => value.GetInt32()).ToArray();
        var max = overlay.GetProperty("max_format").EnumerateArray().Select(value => value.GetInt32()).ToArray();
        return min.Length == 2 && max.Length == 2 &&
               Compare(major, minor, min[0], min[1]) >= 0 && Compare(major, minor, max[0], max[1]) <= 0;
    }

    private static int Compare(int major, int minor, int otherMajor, int otherMinor) =>
        major != otherMajor ? major.CompareTo(otherMajor) : minor.CompareTo(otherMinor);

    private static bool IsDoorState(string path) =>
        path.EndsWith("_door.json", StringComparison.Ordinal) || path.EndsWith("_trapdoor.json", StringComparison.Ordinal);

    private static string DoorWood(int index) => index switch
    {
        0 => "acacia", 1 => "birch", 2 => "crimson", 3 => "dark_oak", 4 => "iron",
        5 => "jungle", 6 => "oak", 7 => "spruce", 8 => "warped", _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    private static IEnumerable<string> ReadModelIds(byte[] blockstate)
    {
        using var document = JsonDocument.Parse(blockstate);
        var result = new HashSet<string>(StringComparer.Ordinal);
        void Visit(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (property.NameEquals("model") && property.Value.ValueKind == JsonValueKind.String)
                        result.Add(property.Value.GetString()!);
                    else Visit(property.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
                foreach (var item in element.EnumerateArray()) Visit(item);
        }
        Visit(document.RootElement);
        return result;
    }

    private static bool ResolveModel(string modelId, ResourceLayer[] layers, HashSet<string> visiting, out string error)
    {
        if (!TryResolveModelTextures(modelId, layers, visiting, out var textures, out error)) return false;
        foreach (var value in textures.Values)
        {
            if (!ResolveTexture(value, textures, new HashSet<string>(StringComparer.Ordinal), out var texture, out error)) return false;
            if (texture is null) continue;
            var path = ResourcePath(texture, "textures", ".png");
            if (path is null || FindAsset(path, layers) is null)
            {
                error = $"missing texture {path}";
                return false;
            }
        }
        error = string.Empty;
        return true;
    }

    private static bool TryResolveModelTextures(string modelId, ResourceLayer[] layers, HashSet<string> visiting,
        out Dictionary<string, string> textures, out string error)
    {
        textures = new Dictionary<string, string>(StringComparer.Ordinal);
        var modelPath = ResourcePath(modelId, "models", ".json");
        if (modelPath is null)
        {
            error = $"invalid model id {modelId}";
            return false;
        }
        if (!visiting.Add(modelPath))
        {
            error = $"cyclic model parent at {modelPath}";
            return false;
        }
        try
        {
            var bytes = FindAsset(modelPath, layers);
            if (bytes is null)
            {
                error = $"missing model {modelPath}";
                return false;
            }
            using var document = JsonDocument.Parse(bytes);
            var model = document.RootElement;
            if (model.TryGetProperty("parent", out var parentElement) && parentElement.ValueKind == JsonValueKind.String)
            {
                var parent = parentElement.GetString()!;
                if (!parent.StartsWith("builtin/", StringComparison.Ordinal) &&
                    !TryResolveModelTextures(parent, layers, visiting, out textures, out error)) return false;
            }
            if (model.TryGetProperty("textures", out var texturesElement) && texturesElement.ValueKind == JsonValueKind.Object)
                foreach (var property in texturesElement.EnumerateObject())
                    if (property.Value.ValueKind == JsonValueKind.String) textures[property.Name] = property.Value.GetString()!;
            error = string.Empty;
            return true;
        }
        finally { visiting.Remove(modelPath); }
    }

    private static bool ResolveTexture(string name, Dictionary<string, string> textures, HashSet<string> visiting,
        out string? texture, out string error)
    {
        texture = name;
        if (!name.StartsWith('#')) { error = string.Empty; return true; }
        var key = name[1..];
        if (!visiting.Add(key) || !textures.TryGetValue(key, out var value))
        {
            error = $"unresolved texture variable {name}";
            return false;
        }
        try { return ResolveTexture(value, textures, visiting, out texture, out error); }
        finally { visiting.Remove(key); }
    }

    private static string? ResourcePath(string id, string kind, string extension)
    {
        var colon = id.IndexOf(':');
        var ns = colon < 0 ? "minecraft" : id[..colon];
        var value = colon < 0 ? id : id[(colon + 1)..];
        if (string.IsNullOrWhiteSpace(ns) || string.IsNullOrWhiteSpace(value) || value.StartsWith('/') || value.Contains("..", StringComparison.Ordinal)) return null;
        if (value.EndsWith(extension, StringComparison.Ordinal)) value = value[..^extension.Length];
        return $"assets/{ns}/{kind}/{value}{extension}";
    }

    private static byte[]? FindAsset(string path, ResourceLayer[] layers)
    {
        for (var index = layers.Length - 1; index >= 0; index--)
            if (layers[index].Assets.TryGetValue(path, out var bytes)) return bytes;
        return null;
    }

    private sealed record ResourceLayer(string Name, Dictionary<string, byte[]> Assets);

    private static PackArchive Open(string relativePath, string sha512) =>
        PackArchive.Open(Path.Combine(AppContext.BaseDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar)), sha512);

    private static (int Mods, int ResourcePacks, int Shaders) Counts(IEnumerable<CatalogItem> items)
    {
        var paths = items.Select(item => item.FilePath).ToArray();
        return (paths.Count(path => path.StartsWith("mods/", StringComparison.Ordinal)),
            paths.Count(path => path.StartsWith("resourcepacks/", StringComparison.Ordinal)),
            paths.Count(path => path.StartsWith("shaderpacks/", StringComparison.Ordinal)));
    }

    private static HashSet<string> CatalogPaths(IEnumerable<CatalogItem> items) =>
        items.Select(item => item.FilePath).ToHashSet(StringComparer.Ordinal);

    private static HashSet<string> ManagedCatalogPaths(PackArchive pack) => pack.Files.Select(file => file.Path)
        .Concat(pack.Overrides.Select(file => file.Path))
        .Where(path => path.StartsWith("mods/", StringComparison.Ordinal) ||
                       path.StartsWith("resourcepacks/", StringComparison.Ordinal) ||
                       path.StartsWith("shaderpacks/", StringComparison.Ordinal))
        .ToHashSet(StringComparer.Ordinal);
}
