using System.Text.Json;
using System.Text.Json.Nodes;

namespace MinePack.Core;

public static class LauncherProfile
{
    public const string ProfileKey = "minepack-test-pack";
    private const string MarkerName = "minepackInstallerId";

    public static string BuildFixtureCandidate(string existingJson, string gameDirectory, string minecraftVersion, string loaderVersion)
    {
        JsonObject root;
        try { root = JsonNode.Parse(existingJson) as JsonObject ?? throw new JsonException(); }
        catch (JsonException ex) { throw new InstallerException("LAUNCHER_JSON_INVALID", LocalizedText.Get("LauncherJsonInvalid"), ex); }

        var profiles = root["profiles"] switch
        {
            null => new JsonObject(),
            JsonObject value => value,
            _ => throw new InstallerException("LAUNCHER_JSON_INVALID", LocalizedText.Get("LauncherProfileListUnsupported"))
        };
        root["profiles"] = profiles;
        if (profiles[ProfileKey] is JsonNode existingNode)
        {
            if (existingNode is not JsonObject existing || !IsOurs(existing))
                throw new InstallerException("LAUNCHER_PROFILE_CONFLICT", LocalizedText.Get("LauncherProfileIdConflict"));
        }

        var profile = (JsonObject?)profiles[ProfileKey] ?? new JsonObject();
        profile[MarkerName] = ProfileKey;
        profile["name"] = "MinePack";
        profile["type"] = "custom";
        profile["lastVersionId"] = $"fabric-loader-{loaderVersion}-{minecraftVersion}";
        profile["gameDir"] = Path.GetFullPath(gameDirectory);
        profile["created"] ??= DateTimeOffset.UtcNow.ToString("O");
        profile["lastUsed"] ??= DateTimeOffset.UtcNow.ToString("O");
        profiles[ProfileKey] = profile;
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public static string RemoveFixtureCandidate(string existingJson, string? expectedGameDirectory = null)
    {
        JsonObject root;
        try { root = JsonNode.Parse(existingJson) as JsonObject ?? throw new JsonException(); }
        catch (JsonException ex) { throw new InstallerException("LAUNCHER_JSON_INVALID", LocalizedText.Get("LauncherJsonInvalid"), ex); }

        if (root["profiles"] is not JsonObject profiles || profiles[ProfileKey] is not JsonObject profile)
            return existingJson;
        if (expectedGameDirectory is not null &&
            (profile["gameDir"] is not JsonValue gameDir || !gameDir.TryGetValue<string>(out var current) ||
             !Path.GetFullPath(current).Equals(Path.GetFullPath(expectedGameDirectory), StringComparison.OrdinalIgnoreCase)))
            return existingJson;
        if (!IsOurs(profile))
            throw new InstallerException("LAUNCHER_PROFILE_CONFLICT", LocalizedText.Get("LauncherProfileUnowned"));
        profiles.Remove(ProfileKey);
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static bool IsOurs(JsonObject profile)
    {
        if (profile.TryGetPropertyValue(MarkerName, out var markerNode))
            return markerNode is JsonValue marker && marker.TryGetValue<string>(out var value) && value == ProfileKey;

        static string? Text(JsonNode? node) =>
            node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
        if (Text(profile["name"]) is not ("MinePack Test Pack" or "MinePack") || Text(profile["type"]) != "custom" ||
            Text(profile["gameDir"]) is not { } gameDir)
            return false;

        try
        {
            if (!Path.IsPathFullyQualified(gameDir)) return false;
            var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameDir));
            var vanilla = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");
            if (path.Equals(vanilla, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(vanilla + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return false;
            SafePath.EnsureNoReparsePoints(path, path);
            var manifest = InstallationManifest.Load(path);
            var expectedHash = manifest.PackVersion switch
            {
                "0.1.0" => TestPackRelease.LegacyArtifactSha512,
                "0.2.0" => TestPackRelease.PreviousArtifactSha512,
                "0.3.0" => TestPackRelease.VoxyArtifactSha512,
                "0.4.0" => TestPackRelease.C2meArtifactSha512,
                "0.5.0" => TestPackRelease.VisualArtifactSha512,
                "0.6.0" => TestPackRelease.InventoryArtifactSha512,
                "0.7.0" => TestPackRelease.GraphicsArtifactSha512,
                "0.8.0" => TestPackRelease.AnimationArtifactSha512,
                "0.9.0" => TestPackRelease.MapArtifactSha512,
                "0.10.0" => TestPackRelease.PriorArtifactSha512,
                TestPackRelease.PackVersion => TestPackRelease.ArtifactSha512,
                "0.15.0" => TestPackRelease.SmoothArtifactSha512,
                Vanilla2PlusRelease.PackVersion => Vanilla2PlusRelease.ArtifactSha512,
                "0.17.0" => Vanilla2PlusRelease.WorldgenArtifactSha512,
                "0.16.0" => Vanilla2PlusRelease.GuardArtifactSha512,
                "0.14.0" => Vanilla2PlusRelease.PriorArtifactSha512,
                "0.13.0" => Vanilla2PlusRelease.PreviousArtifactSha512,
                "0.12.0" => Vanilla2PlusRelease.LegacyArtifactSha512,
                "0.11.0" => Vanilla2PlusRelease.OriginalArtifactSha512,
                _ => null
            };
            var expectedMinecraftVersion = manifest.PackVersion is "0.3.0" or "0.4.0" or "0.5.0" or "0.6.0" or "0.7.0" or "0.8.0" or "0.9.0" or "0.10.0" or "0.11.0" or "0.12.0" or "0.13.0" or "0.14.0" or "0.15.0" or "0.16.0" or "0.17.0" or TestPackRelease.PackVersion or Vanilla2PlusRelease.PackVersion
                ? TestPackRelease.MinecraftVersion : "26.3";
            var expectedDirectoryName = $"test-pack-{manifest.PackVersion}-{expectedHash?[..12].ToLowerInvariant()}";
            var actualDirectoryName = Path.GetFileName(path);
            var isDirectlyInInstances = string.Equals(Directory.GetParent(path)?.Name, "instances", StringComparison.OrdinalIgnoreCase);
            var isExpectedDirectory = string.Equals(actualDirectoryName, expectedDirectoryName, StringComparison.OrdinalIgnoreCase);
            const string reinstallMarker = "-reinstall-";
            var suffixStart = expectedDirectoryName.Length + reinstallMarker.Length;
            var isReinstallDirectory = actualDirectoryName.Length == suffixStart + 32 &&
                                       actualDirectoryName.StartsWith(expectedDirectoryName, StringComparison.OrdinalIgnoreCase) &&
                                       actualDirectoryName.AsSpan(expectedDirectoryName.Length, reinstallMarker.Length).SequenceEqual(reinstallMarker) &&
                                       IsLowerHex32(actualDirectoryName.AsSpan(suffixStart));
            return expectedHash is not null &&
                   manifest.PackArchiveSha512.Equals(expectedHash, StringComparison.OrdinalIgnoreCase) &&
                   manifest.MinecraftVersion == expectedMinecraftVersion &&
                   manifest.FabricLoaderVersion == TestPackRelease.FabricLoaderVersion &&
                   Text(profile["lastVersionId"]) == $"fabric-loader-{manifest.FabricLoaderVersion}-{manifest.MinecraftVersion}" &&
                   isDirectlyInInstances && (isExpectedDirectory || isReinstallDirectory);

            static bool IsLowerHex32(ReadOnlySpan<char> value)
            {
                if (value.Length != 32) return false;
                foreach (var character in value)
                    if (character is not (>= '0' and <= '9' or >= 'a' and <= 'f')) return false;
                return true;
            }
        }
        catch (Exception ex) when (ex is InstallerException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
