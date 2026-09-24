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

    public static string RemoveFixtureCandidate(string existingJson)
    {
        JsonObject root;
        try { root = JsonNode.Parse(existingJson) as JsonObject ?? throw new JsonException(); }
        catch (JsonException ex) { throw new InstallerException("LAUNCHER_JSON_INVALID", "Файл профилей Launcher содержит некорректный JSON.", ex); }

        if (root["profiles"] is not JsonObject profiles || profiles[ProfileKey] is not JsonObject profile)
            return existingJson;
        if (!IsOurs(profile))
            throw new InstallerException("LAUNCHER_PROFILE_CONFLICT", "Профиль не помечен как принадлежащий установщику; он не изменён.");
        profiles.Remove(ProfileKey);
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static bool IsOurs(JsonObject profile) =>
        profile[MarkerName] is JsonValue marker && marker.TryGetValue<string>(out var value) && value == ProfileKey;
}
