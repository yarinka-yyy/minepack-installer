using System.Text;

namespace MinePack.Core;

public sealed record InitialConfigurationFile(string Path, byte[] Contents);

public static class InitialConfiguration
{
    private const string IrisPath = "config/iris.properties";
    private const string GuardPath = "config/guardvillagers.json";
    private const string VoxyPath = "config/voxyworldgenv2.json";
    private const string BbePath = "config/BBEConfig.json";
    private const string OptionsPath = "options.txt";
    private const string BbeContents = "{\"bbe.config.storage.main\":[{\"option\":\"optimize.chest\",\"value\":false},{\"option\":\"optimize.shulker\",\"value\":false}]}";

    private static readonly HashSet<string> IrisArchiveHashes = new(StringComparer.OrdinalIgnoreCase)
    {
        TestPackRelease.ArtifactSha512, TestPackRelease.PreviousCurrentArtifactSha512,
        TestPackRelease.SmoothArtifactSha512, TestPackRelease.PriorArtifactSha512,
        TestPackRelease.MapArtifactSha512, TestPackRelease.AnimationArtifactSha512, TestPackRelease.GraphicsArtifactSha512,
        TestPackRelease.InventoryArtifactSha512, TestPackRelease.VisualArtifactSha512, TestPackRelease.C2meArtifactSha512,
        TestPackRelease.VoxyArtifactSha512, TestPackRelease.PreviousArtifactSha512, TestPackRelease.LowFireArtifactSha512,
        Vanilla2PlusRelease.ArtifactSha512, Vanilla2PlusRelease.PreviousCurrentArtifactSha512,
        Vanilla2PlusRelease.PreviousCandidateArtifactSha512,
        Vanilla2PlusRelease.DoorsArtifactSha512, Vanilla2PlusRelease.XalisArtifactSha512,
        Vanilla2PlusRelease.SpidersArtifactSha512, Vanilla2PlusRelease.YungsArtifactSha512, Vanilla2PlusRelease.TunedArtifactSha512,
        Vanilla2PlusRelease.UntunedArtifactSha512, Vanilla2PlusRelease.WorldgenArtifactSha512, Vanilla2PlusRelease.GuardArtifactSha512,
        Vanilla2PlusRelease.PriorArtifactSha512, Vanilla2PlusRelease.PreviousArtifactSha512, Vanilla2PlusRelease.LegacyArtifactSha512,
        Vanilla2PlusRelease.OriginalArtifactSha512
    };

    private static readonly HashSet<string> ResourceDefaultsArchiveHashes = new(StringComparer.OrdinalIgnoreCase)
    {
        TestPackRelease.ArtifactSha512, TestPackRelease.PreviousCurrentArtifactSha512,
        TestPackRelease.LowFireArtifactSha512, TestPackRelease.SmoothArtifactSha512,
        TestPackRelease.PriorArtifactSha512, Vanilla2PlusRelease.ArtifactSha512,
        Vanilla2PlusRelease.PreviousCurrentArtifactSha512, Vanilla2PlusRelease.PreviousCandidateArtifactSha512,
        Vanilla2PlusRelease.DoorsArtifactSha512,
        Vanilla2PlusRelease.XalisArtifactSha512, Vanilla2PlusRelease.SpidersArtifactSha512, Vanilla2PlusRelease.YungsArtifactSha512,
        Vanilla2PlusRelease.TunedArtifactSha512, Vanilla2PlusRelease.WorldgenArtifactSha512,
        Vanilla2PlusRelease.GuardArtifactSha512, Vanilla2PlusRelease.PriorArtifactSha512, Vanilla2PlusRelease.PreviousArtifactSha512,
        Vanilla2PlusRelease.LegacyArtifactSha512, Vanilla2PlusRelease.OriginalArtifactSha512
    };

    private static readonly HashSet<string> GuardAnimationArchiveHashes = new(StringComparer.OrdinalIgnoreCase)
    {
        Vanilla2PlusRelease.ArtifactSha512, Vanilla2PlusRelease.PreviousCandidateArtifactSha512,
        Vanilla2PlusRelease.DoorsArtifactSha512, Vanilla2PlusRelease.XalisArtifactSha512,
        Vanilla2PlusRelease.SpidersArtifactSha512, Vanilla2PlusRelease.YungsArtifactSha512, Vanilla2PlusRelease.TunedArtifactSha512,
        Vanilla2PlusRelease.WorldgenArtifactSha512, Vanilla2PlusRelease.GuardArtifactSha512, Vanilla2PlusRelease.PriorArtifactSha512
    };

    public static bool IsInitialUserConfig(PackArchive pack, string path) =>
        IsInitialUserConfig(pack.ArchiveSha512, path);

    public static bool IsInitialUserConfig(string archiveSha512, string path) =>
        path.Equals(IrisPath, StringComparison.OrdinalIgnoreCase) && IrisArchiveHashes.Contains(archiveSha512) ||
        path.Equals(GuardPath, StringComparison.OrdinalIgnoreCase) && IsGuardAndVoxyArchive(archiveSha512) ||
        path.Equals(VoxyPath, StringComparison.OrdinalIgnoreCase) && IsGuardAndVoxyArchive(archiveSha512) ||
        path.Equals(GuardPath, StringComparison.OrdinalIgnoreCase) &&
            archiveSha512.Equals(Vanilla2PlusRelease.GuardArtifactSha512, StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<InitialConfigurationFile> Create(PackArchive pack, string extractedRoot)
    {
        var files = new List<InitialConfigurationFile>();
        foreach (var item in pack.Overrides.Where(item => IsInitialUserConfig(pack, item.Path)))
        {
            var path = SafePath.Resolve(extractedRoot, item.Path);
            SafePath.EnsureNoReparsePoints(extractedRoot, path);
            if (!File.Exists(path))
                throw new InstallerException("PACK_INVALID", LocalizedText.Get("PackArchiveInvalid"));
            files.Add(new InitialConfigurationFile(item.Path, File.ReadAllBytes(path)));
        }

        var resourcePacks = GetResourcePacks(pack);
        if (resourcePacks is null) return files;
        var packPaths = pack.Files.Select(file => file.Path)
            .Concat(pack.Overrides.Select(file => file.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (resourcePacks.Any(name => !packPaths.Contains("resourcepacks/" + name)))
            throw new InstallerException("PACK_INVALID", LocalizedText.Get("PinnedResourcePackMissing"));
        files.Add(new InitialConfigurationFile(OptionsPath,
            Encoding.UTF8.GetBytes(TestPackRelease.BuildInitialOptions(resourcePacks))));

        if (!pack.Files.Any(file => file.Path.Equals("mods/bbe-fabric-1.3.7+mc26.2.jar", StringComparison.OrdinalIgnoreCase)))
            throw new InstallerException("PACK_INVALID", LocalizedText.Get("PinnedBbeMissing"));
        var bbePath = SafePath.Resolve(extractedRoot, BbePath);
        SafePath.EnsureNoReparsePoints(extractedRoot, bbePath);
        if (File.Exists(bbePath))
            throw new InstallerException("PACK_INVALID", LocalizedText.Get("PinnedBbeConfigExists"));
        files.Add(new InitialConfigurationFile(BbePath, Encoding.UTF8.GetBytes(BbeContents)));
        return files;
    }

    public static IReadOnlySet<string> GetInitialPaths(PackArchive pack)
    {
        var paths = pack.Overrides.Where(item => IsInitialUserConfig(pack, item.Path))
            .Select(item => item.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (GetResourcePacks(pack) is not null)
        {
            paths.Add(OptionsPath);
            if (pack.Files.Any(file => file.Path.Equals("mods/bbe-fabric-1.3.7+mc26.2.jar", StringComparison.OrdinalIgnoreCase)))
                paths.Add(BbePath);
        }
        return paths;
    }

    public static void WriteToRoot(string root, IEnumerable<InitialConfigurationFile> files)
    {
        foreach (var file in files)
        {
            var target = SafePath.Resolve(root, file.Path);
            SafePath.EnsureNoReparsePoints(root, target);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            SafePath.EnsureNoReparsePoints(root, target);
            File.WriteAllBytes(target, file.Contents);
        }
    }

    public static IReadOnlyList<string> RestoreMissing(string instanceRoot, string stagingRoot,
        IEnumerable<InitialConfigurationFile> files)
    {
        var restored = new List<string>();
        foreach (var file in files)
        {
            var source = SafePath.Resolve(stagingRoot, file.Path);
            SafePath.EnsureNoReparsePoints(stagingRoot, source);
            if (!File.Exists(source))
                throw new InstallerException("PACK_INVALID", LocalizedText.Get("PackArchiveInvalid"));

            var target = SafePath.Resolve(instanceRoot, file.Path);
            SafePath.EnsureNoReparsePoints(instanceRoot, target);
            if (File.Exists(target)) continue;

            var directory = Path.GetDirectoryName(target)!;
            SafePath.EnsureNoReparsePoints(instanceRoot, directory);
            Directory.CreateDirectory(directory);
            SafePath.EnsureNoReparsePoints(instanceRoot, target);
            if (File.Exists(target)) continue;
            try
            {
                File.Move(source, target);
                restored.Add(file.Path);
            }
            catch (IOException) when (File.Exists(target))
            {
                SafePath.EnsureNoReparsePoints(instanceRoot, target);
            }
        }
        return restored;
    }

    private static string[]? GetResourcePacks(PackArchive pack)
    {
        var hash = pack.ArchiveSha512;
        if (!ResourceDefaultsArchiveHashes.Contains(hash)) return null;
        if (hash.Equals(Vanilla2PlusRelease.ArtifactSha512, StringComparison.OrdinalIgnoreCase))
            return Vanilla2PlusRelease.InitialResourcePacks;
        if (hash.Equals(Vanilla2PlusRelease.PreviousCurrentArtifactSha512, StringComparison.OrdinalIgnoreCase))
            return Vanilla2PlusRelease.LegacyCurrentResourcePacks;
        if (hash.Equals(Vanilla2PlusRelease.PreviousCandidateArtifactSha512, StringComparison.OrdinalIgnoreCase))
            return Vanilla2PlusRelease.PreviousCandidateResourcePacks;
        if (hash.Equals(Vanilla2PlusRelease.DoorsArtifactSha512, StringComparison.OrdinalIgnoreCase))
            return Vanilla2PlusRelease.PreviousResourcePacks;
        if (hash.Equals(Vanilla2PlusRelease.XalisArtifactSha512, StringComparison.OrdinalIgnoreCase))
            return Vanilla2PlusRelease.PreviousResourcePacks[..^2];
        if (GuardAnimationArchiveHashes.Contains(hash))
            return Vanilla2PlusRelease.PreviousResourcePacks[..^3];
        if (hash.Equals(TestPackRelease.ArtifactSha512, StringComparison.OrdinalIgnoreCase))
            return TestPackRelease.InitialResourcePacks;
        return hash.Equals(TestPackRelease.PreviousCurrentArtifactSha512, StringComparison.OrdinalIgnoreCase)
            ? TestPackRelease.LegacyCurrentResourcePacks : TestPackRelease.PreviousResourcePacks;
    }

    private static bool IsGuardAndVoxyArchive(string hash) =>
        hash.Equals(TestPackRelease.ArtifactSha512, StringComparison.OrdinalIgnoreCase) ||
        hash.Equals(Vanilla2PlusRelease.ArtifactSha512, StringComparison.OrdinalIgnoreCase) ||
        hash.Equals(Vanilla2PlusRelease.PreviousCurrentArtifactSha512, StringComparison.OrdinalIgnoreCase) ||
        hash.Equals(Vanilla2PlusRelease.PreviousCandidateArtifactSha512, StringComparison.OrdinalIgnoreCase) ||
        hash.Equals(Vanilla2PlusRelease.DoorsArtifactSha512, StringComparison.OrdinalIgnoreCase) ||
        hash.Equals(Vanilla2PlusRelease.XalisArtifactSha512, StringComparison.OrdinalIgnoreCase) ||
        hash.Equals(Vanilla2PlusRelease.SpidersArtifactSha512, StringComparison.OrdinalIgnoreCase) ||
        hash.Equals(Vanilla2PlusRelease.YungsArtifactSha512, StringComparison.OrdinalIgnoreCase) ||
        hash.Equals(Vanilla2PlusRelease.TunedArtifactSha512, StringComparison.OrdinalIgnoreCase) ||
        hash.Equals(Vanilla2PlusRelease.UntunedArtifactSha512, StringComparison.OrdinalIgnoreCase) ||
        hash.Equals(Vanilla2PlusRelease.WorldgenArtifactSha512, StringComparison.OrdinalIgnoreCase);
}
