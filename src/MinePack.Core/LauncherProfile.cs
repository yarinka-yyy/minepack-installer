using System.Text.Json;
using System.Text.Json.Nodes;

namespace MinePack.Core;

public static class LauncherProfile
{
    public const string ProfileKey = "minepack-test-pack";
    private const string MarkerName = "minepackInstallerId";

    public static string ProfileName(string minecraftVersion) => $"MinePack for {minecraftVersion}";

    public static string BuildFixtureCandidate(string existingJson, string gameDirectory, string minecraftVersion,
        string loaderVersion, string? defaultJavaArgs = null)
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
        profile["name"] = ProfileName(minecraftVersion);
        profile["type"] = "custom";
        profile["lastVersionId"] = $"fabric-loader-{loaderVersion}-{minecraftVersion}";
        profile["gameDir"] = Path.GetFullPath(gameDirectory);
        profile["created"] ??= DateTimeOffset.UtcNow.ToString("O");
        profile["lastUsed"] ??= DateTimeOffset.UtcNow.ToString("O");
        if (defaultJavaArgs is not null && !profile.ContainsKey("javaArgs"))
            profile["javaArgs"] = defaultJavaArgs;
        profiles[ProfileKey] = profile;
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public static string RemoveOwnedProfile(string existingJson, string expectedGameDirectory)
        => RemoveOwnedProfileCore(existingJson, expectedGameDirectory, profile => IsOurs(profile));

    internal static string RemoveOwnedProfile(string existingJson, string expectedGameDirectory,
        InstallationManifest trustedManifest, PackArchive pack)
    {
        InstallService.ValidateMatchesRelease(trustedManifest, pack);
        return RemoveOwnedProfileCore(existingJson, expectedGameDirectory,
            profile => IsOurs(profile, trustedManifest, pack, expectedGameDirectory));
    }

    private static string RemoveOwnedProfileCore(string existingJson, string expectedGameDirectory,
        Func<JsonObject, bool> ownsProfile)
    {
        JsonObject root;
        try { root = JsonNode.Parse(existingJson) as JsonObject ?? throw new JsonException(); }
        catch (JsonException ex) { throw new InstallerException("LAUNCHER_JSON_INVALID", LocalizedText.Get("LauncherJsonInvalid"), ex); }

        if (root["profiles"] is not JsonObject profiles || profiles[ProfileKey] is not JsonObject profile)
            return existingJson;
        if (profile["gameDir"] is not JsonValue gameDir || !gameDir.TryGetValue<string>(out var current) ||
            !Path.IsPathFullyQualified(current) ||
            !Path.TrimEndingDirectorySeparator(Path.GetFullPath(current)).Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(expectedGameDirectory)), StringComparison.OrdinalIgnoreCase))
            return existingJson;
        if (!ownsProfile(profile))
            throw new InstallerException("LAUNCHER_PROFILE_CONFLICT", LocalizedText.Get("LauncherProfileUnowned"));
        profiles.Remove(ProfileKey);
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static bool IsOurs(JsonObject profile, InstallationManifest? trustedManifest = null,
        PackArchive? trustedPack = null, string? trustedGameDirectory = null)
    {
        if (profile.TryGetPropertyValue(MarkerName, out var markerNode))
            return markerNode is JsonValue marker && marker.TryGetValue<string>(out var value) && value == ProfileKey;

        static string? Text(JsonNode? node) =>
            node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
        if (Text(profile["type"]) != "custom" ||
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
            InstallationManifest manifest;
            if (trustedManifest is not null)
            {
                if (trustedPack is null || trustedGameDirectory is null ||
                    !Path.TrimEndingDirectorySeparator(path).Equals(
                        Path.TrimEndingDirectorySeparator(Path.GetFullPath(trustedGameDirectory)), StringComparison.OrdinalIgnoreCase))
                    return false;
                InstallService.ValidateMatchesRelease(trustedManifest, trustedPack);
                manifest = trustedManifest;
            }
            else
            {
                manifest = InstallationManifest.Load(path);
            }
            if (!InstalledInstanceCatalog.TryGetRelease(manifest.PackVersion, out var knownRelease)) return false;
            var name = Text(profile["name"]);
            var knownName = name is "MinePack Test Pack" or "MinePack" || name == ProfileName(knownRelease.MinecraftVersion);
            return knownName &&
                   manifest.PackArchiveSha512.Equals(knownRelease.ArchiveSha512, StringComparison.OrdinalIgnoreCase) &&
                   manifest.MinecraftVersion == knownRelease.MinecraftVersion &&
                   manifest.FabricLoaderVersion == knownRelease.FabricLoaderVersion &&
                   Text(profile["lastVersionId"]) == $"fabric-loader-{manifest.FabricLoaderVersion}-{manifest.MinecraftVersion}" &&
                   InstalledInstanceCatalog.IsExpectedInstanceDirectory(path, knownRelease);
        }
        catch (Exception ex) when (ex is InstallerException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
