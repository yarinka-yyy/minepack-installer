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
        catch (JsonException ex) { throw new InstallerException("LAUNCHER_JSON_INVALID", "Файл профилей Launcher содержит некорректный JSON.", ex); }

        var profiles = root["profiles"] switch
        {
            null => new JsonObject(),
            JsonObject value => value,
            _ => throw new InstallerException("LAUNCHER_JSON_INVALID", "Список профилей Launcher имеет неподдерживаемый формат.")
        };
        root["profiles"] = profiles;
        if (profiles[ProfileKey] is JsonNode existingNode)
        {
            if (existingNode is not JsonObject existing || !IsOurs(existing))
                throw new InstallerException("LAUNCHER_PROFILE_CONFLICT", "Идентификатор тестового профиля уже занят чужим профилем.");
        }

        var profile = (JsonObject?)profiles[ProfileKey] ?? new JsonObject();
        profile[MarkerName] = ProfileKey;
        profile["name"] = "MinePack Test Pack";
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
        catch (JsonException ex) { throw new InstallerException("LAUNCHER_JSON_INVALID", "Файл профилей Launcher содержит некорректный JSON.", ex); }

        if (root["profiles"] is not JsonObject profiles || profiles[ProfileKey] is not JsonObject profile)
            return existingJson;
        if (expectedGameDirectory is not null &&
            (profile["gameDir"] is not JsonValue gameDir || !gameDir.TryGetValue<string>(out var current) ||
             !Path.GetFullPath(current).Equals(Path.GetFullPath(expectedGameDirectory), StringComparison.OrdinalIgnoreCase)))
            return existingJson;
        if (!IsOurs(profile))
            throw new InstallerException("LAUNCHER_PROFILE_CONFLICT", "Профиль не помечен как принадлежащий установщику; он не изменён.");
        profiles.Remove(ProfileKey);
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static bool IsOurs(JsonObject profile)
    {
        if (profile.TryGetPropertyValue(MarkerName, out var markerNode))
            return markerNode is JsonValue marker && marker.TryGetValue<string>(out var value) && value == ProfileKey;

        static string? Text(JsonNode? node) =>
            node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
        if (Text(profile["name"]) != "MinePack Test Pack" || Text(profile["type"]) != "custom" ||
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
                TestPackRelease.PackVersion => TestPackRelease.ArtifactSha512,
                _ => null
            };
            var expectedMinecraftVersion = manifest.PackVersion is "0.3.0" or "0.4.0" or "0.5.0" or "0.6.0" or "0.7.0" or TestPackRelease.PackVersion
                ? TestPackRelease.MinecraftVersion : "26.3";
            return expectedHash is not null &&
                   manifest.PackArchiveSha512.Equals(expectedHash, StringComparison.OrdinalIgnoreCase) &&
                   manifest.MinecraftVersion == expectedMinecraftVersion &&
                   manifest.FabricLoaderVersion == TestPackRelease.FabricLoaderVersion &&
                   Text(profile["lastVersionId"]) == $"fabric-loader-{manifest.FabricLoaderVersion}-{manifest.MinecraftVersion}" &&
                   Path.GetFileName(path).Equals($"test-pack-{manifest.PackVersion}-{expectedHash[..12].ToLowerInvariant()}", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is InstallerException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
