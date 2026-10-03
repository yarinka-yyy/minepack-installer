namespace MinePack.Core;

public sealed record KnownPackRelease(
    string PackName,
    string PackVersion,
    string MinecraftVersion,
    string FabricLoaderVersion,
    string ArtifactRelativePath,
    string ArchiveSha512)
{
    public string ArchivePath(string applicationDirectory) =>
        Path.GetFullPath(Path.Combine(applicationDirectory,
            ArtifactRelativePath.Replace('/', Path.DirectorySeparatorChar)));

    public string InstanceDirectoryPrefix =>
        $"test-pack-{PackVersion}-{ArchiveSha512[..12].ToLowerInvariant()}";
}

public enum InstalledInstanceState
{
    Trusted,
    PackageMissing,
    Residue,
    UnknownRelease,
    Invalid,
    Unsafe
}

public sealed record InstalledInstanceEntry(
    string Path,
    string DirectoryName,
    InstalledInstanceState State,
    KnownPackRelease? Release,
    string? DiagnosticCode)
{
    public InstallationLayout? Layout { get; init; }
    public bool IsTrusted => State == InstalledInstanceState.Trusted && Release is not null;
    public bool IsOpenable => State is InstalledInstanceState.Trusted or InstalledInstanceState.Residue or
        InstalledInstanceState.PackageMissing or InstalledInstanceState.UnknownRelease or InstalledInstanceState.Invalid;
}

public static class InstalledInstanceCatalog
{
    private static readonly KnownPackRelease[] Releases =
    [
        Test(TestPackRelease.PackVersion, TestPackRelease.ArtifactFileName, TestPackRelease.ArtifactSha512),
        Test("0.18.0", TestPackRelease.LowFireArtifactFileName, TestPackRelease.LowFireArtifactSha512),
        Test("0.15.0", TestPackRelease.SmoothArtifactFileName, TestPackRelease.SmoothArtifactSha512),
        Test("0.10.0", TestPackRelease.PriorArtifactFileName, TestPackRelease.PriorArtifactSha512),
        Test("0.9.0", TestPackRelease.MapArtifactFileName, TestPackRelease.MapArtifactSha512),
        Test("0.8.0", TestPackRelease.AnimationArtifactFileName, TestPackRelease.AnimationArtifactSha512),
        Test("0.7.0", TestPackRelease.GraphicsArtifactFileName, TestPackRelease.GraphicsArtifactSha512),
        Test("0.6.0", TestPackRelease.InventoryArtifactFileName, TestPackRelease.InventoryArtifactSha512),
        Test("0.5.0", TestPackRelease.VisualArtifactFileName, TestPackRelease.VisualArtifactSha512),
        Test("0.4.0", TestPackRelease.C2meArtifactFileName, TestPackRelease.C2meArtifactSha512),
        Test("0.3.0", TestPackRelease.VoxyArtifactFileName, TestPackRelease.VoxyArtifactSha512),
        Test("0.2.0", TestPackRelease.PreviousArtifactFileName, TestPackRelease.PreviousArtifactSha512, "26.3"),
        Test("0.1.0", TestPackRelease.LegacyArtifactFileName, TestPackRelease.LegacyArtifactSha512, "26.3"),
        Frontier(Vanilla2PlusRelease.PackVersion, Vanilla2PlusRelease.ArtifactFileName, Vanilla2PlusRelease.ArtifactSha512),
        Frontier("0.19.6", Vanilla2PlusRelease.PreviousCandidateArtifactFileName, Vanilla2PlusRelease.PreviousCandidateArtifactSha512),
        Frontier("0.19.5", Vanilla2PlusRelease.DoorsArtifactFileName, Vanilla2PlusRelease.DoorsArtifactSha512),
        Frontier("0.19.4", Vanilla2PlusRelease.XalisArtifactFileName, Vanilla2PlusRelease.XalisArtifactSha512),
        Frontier("0.19.3", Vanilla2PlusRelease.SpidersArtifactFileName, Vanilla2PlusRelease.SpidersArtifactSha512),
        Frontier("0.19.2", Vanilla2PlusRelease.YungsArtifactFileName, Vanilla2PlusRelease.YungsArtifactSha512),
        Frontier("0.19.1", Vanilla2PlusRelease.TunedArtifactFileName, Vanilla2PlusRelease.TunedArtifactSha512),
        Frontier("0.19.0", Vanilla2PlusRelease.UntunedArtifactFileName, Vanilla2PlusRelease.UntunedArtifactSha512),
        Frontier("0.17.0", Vanilla2PlusRelease.WorldgenArtifactFileName, Vanilla2PlusRelease.WorldgenArtifactSha512),
        Frontier("0.16.0", Vanilla2PlusRelease.GuardArtifactFileName, Vanilla2PlusRelease.GuardArtifactSha512),
        Frontier("0.14.0", Vanilla2PlusRelease.PriorArtifactFileName, Vanilla2PlusRelease.PriorArtifactSha512),
        Frontier("0.13.0", Vanilla2PlusRelease.PreviousArtifactFileName, Vanilla2PlusRelease.PreviousArtifactSha512),
        Frontier("0.12.0", Vanilla2PlusRelease.LegacyArtifactFileName, Vanilla2PlusRelease.LegacyArtifactSha512),
        Frontier("0.11.0", Vanilla2PlusRelease.OriginalArtifactFileName, Vanilla2PlusRelease.OriginalArtifactSha512)
    ];

    public static IReadOnlyList<KnownPackRelease> KnownReleases => Releases;

    public static bool TryGetRelease(string packVersion, out KnownPackRelease release)
    {
        release = Releases.FirstOrDefault(item => item.PackVersion.Equals(packVersion, StringComparison.Ordinal))!;
        return release is not null;
    }

    public static (string Path, string Hash) PinnedArchive(string packVersion, string applicationDirectory)
    {
        if (!TryGetRelease(packVersion, out var release))
            throw new InstallerException("RELEASE_UNKNOWN", LocalizedText.Get("PinnedArchiveUnavailable"));
        return (release.ArchivePath(applicationDirectory), release.ArchiveSha512);
    }

    public static bool IsExpectedInstanceDirectory(string instancePath, KnownPackRelease release)
    {
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(instancePath));
        if (!Path.GetFileName(Path.GetDirectoryName(fullPath))!.Equals("instances", StringComparison.OrdinalIgnoreCase))
            return false;
        return InstanceDirectoryNaming.IsExpected(Path.GetFileName(fullPath), release);
    }

    public static IReadOnlyList<InstalledInstanceEntry> Enumerate(string installRoot, string applicationDirectory)
    {
        var root = Path.TrimEndingDirectorySeparator(InstallService.ValidateInstallRoot(installRoot));
        if (!Directory.Exists(root)) return [];
        SafePath.EnsureNoReparsePoints(root, root);
        var instancesRoot = Path.Combine(root, "instances");
        SafePath.EnsureNoReparsePoints(root, instancesRoot);
        if (!Directory.Exists(instancesRoot)) return [];

        var appRoot = Path.GetFullPath(applicationDirectory);
        var results = new List<InstalledInstanceEntry>();
        foreach (var path in Directory.EnumerateDirectories(instancesRoot))
        {
            var name = Path.GetFileName(path);
            try
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                {
                    results.Add(new InstalledInstanceEntry(path, name, InstalledInstanceState.Unsafe, null, "ROOT_UNSAFE"));
                    continue;
                }

                var manifestPath = SafePath.Resolve(path, InstallationManifest.FileName);
                SafePath.EnsureNoReparsePoints(path, manifestPath);
                if (!File.Exists(manifestPath))
                {
                    results.Add(new InstalledInstanceEntry(path, name, InstalledInstanceState.Residue,
                        InstanceDirectoryNaming.TryGetReleaseFromName(name, out var namedRelease) ? namedRelease : null, null));
                    continue;
                }

                InstallationManifest manifest;
                try { manifest = InstallationManifest.Load(path); }
                catch (InstallerException ex)
                {
                    results.Add(new InstalledInstanceEntry(path, name, InstalledInstanceState.Invalid, null, ex.Code));
                    continue;
                }

                if (!TryGetRelease(manifest.PackVersion, out var release))
                {
                    results.Add(new InstalledInstanceEntry(path, name, InstalledInstanceState.UnknownRelease, null, "RELEASE_UNKNOWN"));
                    continue;
                }
                if (!IsExpectedInstanceDirectory(path, release) ||
                    !manifest.PackArchiveSha512.Equals(release.ArchiveSha512, StringComparison.OrdinalIgnoreCase) ||
                    !manifest.MinecraftVersion.Equals(release.MinecraftVersion, StringComparison.Ordinal) ||
                    !manifest.FabricLoaderVersion.Equals(release.FabricLoaderVersion, StringComparison.Ordinal))
                {
                    results.Add(new InstalledInstanceEntry(path, name, InstalledInstanceState.Invalid, release, "RELEASE_MISMATCH"));
                    continue;
                }

                var archive = release.ArchivePath(appRoot);
                try { SafePath.EnsureNoReparsePoints(appRoot, archive); }
                catch (InstallerException ex)
                {
                    results.Add(new InstalledInstanceEntry(path, name, InstalledInstanceState.Unsafe, release, ex.Code));
                    continue;
                }
                if (!File.Exists(archive))
                {
                    results.Add(new InstalledInstanceEntry(path, name, InstalledInstanceState.PackageMissing, release, "PACK_NOT_FOUND"));
                    continue;
                }

                try
                {
                    var pack = PackArchive.Open(archive, release.ArchiveSha512);
                    InstallService.ValidateMatchesRelease(manifest, pack);
                    results.Add(new InstalledInstanceEntry(path, name, InstalledInstanceState.Trusted, release, null));
                }
                catch (InstallerException ex)
                {
                    results.Add(new InstalledInstanceEntry(path, name, InstalledInstanceState.Invalid, release, ex.Code));
                }
            }
            catch (InstallerException ex)
            {
                var state = ex.Code is "ROOT_UNSAFE" or "PATH_REPARSE_BLOCKED" or "PATH_BLOCKED"
                    ? InstalledInstanceState.Unsafe : InstalledInstanceState.Invalid;
                results.Add(new InstalledInstanceEntry(path, name, state, null, ex.Code));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                results.Add(new InstalledInstanceEntry(path, name, InstalledInstanceState.Invalid, null, "INSTANCE_READ_FAILED"));
            }
        }
        return results;
    }

    public static IReadOnlyList<InstalledInstanceEntry> Enumerate(InstallationLayout prismLayout, string applicationDirectory)
    {
        ArgumentNullException.ThrowIfNull(prismLayout);
        if (!prismLayout.IsPrism)
            throw new InstallerException("ROOT_UNSAFE", LocalizedText.Get("UnsafePath"));
        prismLayout.Validate();
        if (!Directory.Exists(prismLayout.InstancesRoot)) return [];

        var appRoot = Path.GetFullPath(applicationDirectory);
        SafePath.EnsureNoReparsePoints(prismLayout.InstancesRoot, prismLayout.InstancesRoot);
        var results = new List<InstalledInstanceEntry>();
        foreach (var wrapper in Directory.EnumerateDirectories(prismLayout.InstancesRoot))
        {
            var name = Path.GetFileName(wrapper);
            try
            {
                if ((File.GetAttributes(wrapper) & FileAttributes.ReparsePoint) != 0) continue;
                var layout = prismLayout.ForInstance(wrapper);
                var wrapperMarker = Path.Combine(wrapper, PrismLauncherService.OwnershipMarkerName);
                var localMarker = Path.Combine(prismLayout.StateRoot, "ownership", name + ".json");
                if (!File.Exists(wrapperMarker) && !File.Exists(localMarker)) continue;
                if (!PrismLauncherService.HasOwnedBinding(layout))
                {
                    if (PrismLauncherService.TryGetOwnedResidueRelease(layout, out var residueRelease))
                    {
                        results.Add(new InstalledInstanceEntry(layout.GameDirectory, name,
                            InstalledInstanceState.Residue, residueRelease, null) { Layout = layout });
                        continue;
                    }
                    results.Add(new InstalledInstanceEntry(layout.GameDirectory, name,
                        InstalledInstanceState.Invalid, null, "PRISM_OWNERSHIP_INVALID") { Layout = layout });
                    continue;
                }

                var manifest = InstallationManifest.Load(layout.GameDirectory);
                if (!layout.TryGetPinnedRelease(manifest.PackVersion, out var release) ||
                    !PrismLauncherService.IsExpectedInstanceDirectory(layout, release) ||
                    !manifest.PackArchiveSha512.Equals(release.ArchiveSha512, StringComparison.OrdinalIgnoreCase) ||
                    manifest.MinecraftVersion != release.MinecraftVersion ||
                    manifest.FabricLoaderVersion != release.FabricLoaderVersion)
                {
                    results.Add(new InstalledInstanceEntry(layout.GameDirectory, name,
                        InstalledInstanceState.Invalid, null, "RELEASE_MISMATCH") { Layout = layout });
                    continue;
                }

                var archive = release.ArchivePath(appRoot);
                SafePath.EnsureNoReparsePoints(appRoot, archive);
                if (!File.Exists(archive))
                {
                    results.Add(new InstalledInstanceEntry(layout.GameDirectory, name,
                        InstalledInstanceState.PackageMissing, release, "PACK_NOT_FOUND") { Layout = layout });
                    continue;
                }

                var pack = PackArchive.Open(archive, release.ArchiveSha512);
                InstallService.ValidateMatchesRelease(manifest, pack);
                results.Add(new InstalledInstanceEntry(layout.GameDirectory, name,
                    InstalledInstanceState.Trusted, release, null) { Layout = layout });
            }
            catch (InstallerException ex)
            {
                results.Add(new InstalledInstanceEntry(Path.Combine(wrapper, "minecraft"), name,
                    ex.Code is "ROOT_UNSAFE" or "PATH_REPARSE_BLOCKED" or "PATH_BLOCKED"
                        ? InstalledInstanceState.Unsafe : InstalledInstanceState.Invalid, null, ex.Code));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                results.Add(new InstalledInstanceEntry(Path.Combine(wrapper, "minecraft"), name,
                    InstalledInstanceState.Invalid, null, "INSTANCE_READ_FAILED"));
            }
        }
        return results;
    }

    private static KnownPackRelease Test(string version, string fileName, string hash, string? minecraftVersion = null) =>
        new("Vanilla Plus", version, minecraftVersion ?? TestPackRelease.MinecraftVersion,
            TestPackRelease.FabricLoaderVersion, "releases/test-pack/" + fileName, hash);

    private static KnownPackRelease Frontier(string version, string fileName, string hash) =>
        new("Frontier", version, TestPackRelease.MinecraftVersion,
            TestPackRelease.FabricLoaderVersion, "releases/vanilla-2-plus/" + fileName, hash);
}
