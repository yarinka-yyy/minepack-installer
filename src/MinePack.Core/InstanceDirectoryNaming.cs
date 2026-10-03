namespace MinePack.Core;

public static class InstanceDirectoryNaming
{
    public static string CreateBaseName(KnownPackRelease release)
    {
        ArgumentNullException.ThrowIfNull(release);
        var pack = release.PackName switch
        {
            "Vanilla Plus" => "VanillaPlus",
            "Frontier" => "Frontier",
            _ => throw new InstallerException("RELEASE_UNKNOWN", LocalizedText.Get("PinnedArchiveUnavailable"))
        };
        return $"MinePack-{release.MinecraftVersion}-{pack}-{release.PackVersion}";
    }

    public static string Allocate(string instancesRoot, KnownPackRelease release)
    {
        var baseName = CreateBaseName(release);
        var plain = SafePath.Resolve(instancesRoot, baseName);
        if (!File.Exists(plain) && !Directory.Exists(plain)) return baseName;
        for (var number = 2; number < 100_000; number++)
        {
            var candidate = $"{baseName}-{number:D2}";
            var path = SafePath.Resolve(instancesRoot, candidate);
            if (!File.Exists(path) && !Directory.Exists(path)) return candidate;
        }
        throw new InstallerException("INSTANCE_NAME_EXHAUSTED", LocalizedText.Get("InstanceNameExhausted"));
    }

    public static bool IsExpected(string directoryName, KnownPackRelease release) =>
        IsNewName(directoryName, release) || IsLegacyName(directoryName, release);

    public static bool TryGetReleaseFromName(string directoryName, out KnownPackRelease release)
    {
        var matches = InstalledInstanceCatalog.KnownReleases
            .Where(candidate => IsExpected(directoryName, candidate)).Take(2).ToArray();
        if (matches.Length == 1)
        {
            release = matches[0];
            return true;
        }
        release = null!;
        return false;
    }

    public static bool IsNewName(string directoryName, KnownPackRelease release)
    {
        var baseName = CreateBaseName(release);
        if (directoryName.Equals(baseName, StringComparison.OrdinalIgnoreCase)) return true;
        if (!directoryName.StartsWith(baseName + "-", StringComparison.OrdinalIgnoreCase)) return false;
        var suffix = directoryName.AsSpan(baseName.Length + 1);
        if (suffix.Length < 2 || suffix.Length > 6) return false;
        var value = 0;
        foreach (var character in suffix)
        {
            if (character is < '0' or > '9') return false;
            value = value * 10 + character - '0';
        }
        return value >= 2 && suffix.SequenceEqual(value.ToString("D2"));
    }

    public static bool IsLegacyName(string directoryName, KnownPackRelease release)
    {
        var prefix = release.InstanceDirectoryPrefix;
        if (directoryName.Equals(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        const string reinstallMarker = "-reinstall-";
        if (!directoryName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            directoryName.Length < prefix.Length + reinstallMarker.Length ||
            !directoryName.AsSpan(prefix.Length, reinstallMarker.Length).SequenceEqual(reinstallMarker)) return false;
        var suffix = directoryName.AsSpan(prefix.Length + reinstallMarker.Length);
        if (suffix.Length != 32) return false;
        foreach (var character in suffix)
            if (character is not (>= '0' and <= '9' or >= 'a' and <= 'f')) return false;
        return true;
    }
}
