using System.Security.Cryptography;
using System.Text;

namespace MinePack.Core;

public enum LauncherKind
{
    Official,
    Prism
}

public sealed class InstallationLayout
{
    private InstallationLayout(LauncherKind launcherKind, string stateRoot, string instancesRoot,
        string instanceDirectory, string gameDirectory, string launcherIdentity, string fingerprint,
        string? prismDataRoot = null, KnownPackRelease? fixtureRelease = null)
    {
        LauncherKind = launcherKind;
        StateRoot = Full(stateRoot);
        InstancesRoot = Full(instancesRoot);
        InstanceDirectory = Full(instanceDirectory);
        GameDirectory = Full(gameDirectory);
        LauncherIdentity = launcherIdentity;
        Fingerprint = fingerprint;
        PrismDataRoot = prismDataRoot is null ? null : Full(prismDataRoot);
        FixtureRelease = fixtureRelease;
        Validate();
    }

    public LauncherKind LauncherKind { get; }
    public string StateRoot { get; }
    public string InstancesRoot { get; }
    public string InstanceDirectory { get; }
    public string GameDirectory { get; }
    public string LauncherIdentity { get; }
    public string Fingerprint { get; }
    public string? PrismDataRoot { get; }
    public bool IsPrism => LauncherKind == LauncherKind.Prism;
    internal KnownPackRelease? FixtureRelease { get; }

    public static InstallationLayout Official(string stateRoot, string instanceName)
    {
        var root = Full(stateRoot);
        var instances = Path.Combine(root, "instances");
        var instance = SafePath.Resolve(instances, instanceName);
        if (!Path.GetDirectoryName(instance)!.Equals(Full(instances), StringComparison.OrdinalIgnoreCase))
            throw new InstallerException("ROOT_UNSAFE", LocalizedText.Get("UnsafePath"));
        return new InstallationLayout(LauncherKind.Official, root, instances, instance, instance, "official", "official-v1");
    }

    public static InstallationLayout Prism(PrismLauncherTarget target, string instanceName)
    {
        var expectedFingerprint = ValidatePrismTarget(target);
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MinePack", "prism", expectedFingerprint);
        return MakePrism(target, instanceName, root, expectedFingerprint);
    }

    internal static InstallationLayout PrismForTesting(PrismLauncherTarget target, string instanceName, string stateRoot,
        KnownPackRelease? fixtureRelease = null)
    {
        var fingerprint = ValidatePrismTarget(target);
        return MakePrism(target, instanceName, stateRoot, fingerprint, fixtureRelease);
    }

    private static InstallationLayout MakePrism(PrismLauncherTarget target, string instanceName, string stateRoot,
        string fingerprint, KnownPackRelease? fixtureRelease = null)
    {
        var instance = SafePath.Resolve(target.InstancesRoot, instanceName);
        if (!Path.GetDirectoryName(instance)!.Equals(Full(target.InstancesRoot), StringComparison.OrdinalIgnoreCase))
            throw new InstallerException("ROOT_UNSAFE", LocalizedText.Get("UnsafePath"));
        return new InstallationLayout(LauncherKind.Prism, stateRoot, target.InstancesRoot, instance,
            Path.Combine(instance, "minecraft"), target.ExecutablePath, fingerprint, target.DataRoot, fixtureRelease);
    }

    public InstallationLayout ForInstance(string instanceDirectory)
    {
        var instance = Full(instanceDirectory);
        var game = IsPrism ? Path.Combine(instance, "minecraft") : instance;
        return new InstallationLayout(LauncherKind, StateRoot, InstancesRoot, instance, game, LauncherIdentity, Fingerprint, PrismDataRoot,
            FixtureRelease);
    }

    internal bool TryGetPinnedRelease(string packVersion, out KnownPackRelease release)
    {
        if (FixtureRelease is { } fixture && fixture.PackVersion == packVersion)
        {
            release = fixture;
            return true;
        }
        return InstalledInstanceCatalog.TryGetRelease(packVersion, out release!);
    }

    public void Validate()
    {
        EnsureSafeRoot(StateRoot);
        EnsureSafeRoot(InstancesRoot);
        if (!Path.GetDirectoryName(InstanceDirectory)!.Equals(InstancesRoot, StringComparison.OrdinalIgnoreCase) ||
            (IsPrism
                ? !Path.GetDirectoryName(GameDirectory)!.Equals(InstanceDirectory, StringComparison.OrdinalIgnoreCase)
                : !GameDirectory.Equals(InstanceDirectory, StringComparison.OrdinalIgnoreCase)))
            throw new InstallerException("ROOT_UNSAFE", LocalizedText.Get("UnsafePath"));
        if (!IsPrism && !InstancesRoot.Equals(Path.Combine(StateRoot, "instances"), StringComparison.OrdinalIgnoreCase))
            throw new InstallerException("ROOT_UNSAFE", LocalizedText.Get("UnsafePath"));
        SafePath.EnsureNoReparsePoints(InstancesRoot, GameDirectory);
        if (IsPrism)
        {
            if (PrismDataRoot is null || !Fingerprint.Equals(
                    ComputePrismFingerprint(PrismDataRoot, InstancesRoot), StringComparison.OrdinalIgnoreCase))
                throw new InstallerException("PRISM_IDENTITY_CHANGED", LocalizedText.Get("PrismIdentityChanged"));
            SafePath.EnsureNoReparsePoints(PrismDataRoot, PrismDataRoot);
        }
    }

    public static string ComputePrismFingerprint(string dataRoot, string instancesRoot)
    {
        var canonical = Full(dataRoot).ToUpperInvariant() + "\n" + Full(instancesRoot).ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static string ValidatePrismTarget(PrismLauncherTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var expected = ComputePrismFingerprint(target.DataRoot, target.InstancesRoot);
        if (!expected.Equals(target.Fingerprint, StringComparison.OrdinalIgnoreCase) ||
            !Path.IsPathFullyQualified(target.ExecutablePath))
            throw new InstallerException("PRISM_IDENTITY_CHANGED", LocalizedText.Get("PrismIdentityChanged"));
        var executable = Full(target.ExecutablePath);
        SafePath.EnsureNoReparsePoints(Path.GetDirectoryName(executable)!, executable);
        return expected;
    }

    private static void EnsureSafeRoot(string path)
    {
        var full = Full(path);
        var volumeRoot = Path.GetPathRoot(full);
        if (full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Equals(volumeRoot?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new InstallerException("ROOT_UNSAFE", LocalizedText.Get("DriveRootUnsafe"));
        SafePath.EnsureNoReparsePoints(full, full);
    }

    private static string Full(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) ||
            path.StartsWith("\\\\?\\", StringComparison.Ordinal) || path.StartsWith("\\\\.\\", StringComparison.Ordinal))
            throw new InstallerException("ROOT_UNSAFE", LocalizedText.Get("UnsafePath"));
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
}
