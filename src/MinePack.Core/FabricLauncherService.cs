using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MinePack.Core;

public sealed class FabricLauncherService : IDisposable
{
    private const string ProfileSha512 = "F5728443FDBBB9307D83D96171FA038D3F66241A2D2D54F0F77F5D807882886255EB6F2DC55D8C3C68AEFD932998E8F7AC54E556974BA65A698FB23E99B405DA";
    private const string PreviousProfileSha512 = "10DADB629030E7EA791A1538F72671BE27F0E2A3D36D1A3A1E8DA2E19AEA6C9020A1EBE13FE4683FC91C5A425D3CF2BA0063695B0C2182809F13157CAC2D95A6";
    private const string MinecraftClientJarSha512 = "9A2465F82D7706E7FECF4C5D9AB05BF85F818D81C1FF1605B9C0D982B29BABDDBF036A6440AA6D4F0C184110457B637F9D7959453B4FB314489782B82CAACC90";
    private const string PreviousMinecraftClientJarSha512 = "9CEDD89122B11B0E079ECD342BABD034E3A2016F8B60CDC9FD1296A167AE606108819E7440381526704B335A9FC8948BEAB66C436B7C3BA5EF3D068301F7CFE6";
    private const long MinecraftClientJarSize = 39_193_383;
    private const long PreviousMinecraftClientJarSize = 41_483_720;
    private readonly string _launcherRoot;
    private readonly string _minecraftVersion;
    private readonly string _versionId;
    private readonly string _profileUrl;
    private readonly string _expectedSha512;
    private readonly string _expectedClientJarSha512;
    private readonly long _expectedClientJarSize;
    private readonly HttpClient _http;
    private readonly Action _ensureLauncherClosed;

    public FabricLauncherService(string? launcherRoot = null, HttpMessageHandler? handler = null, string? expectedSha512 = null,
        string? expectedClientJarSha512 = null, long? expectedClientJarSize = null, string? minecraftVersion = null,
        Action? ensureLauncherClosed = null)
    {
        _minecraftVersion = minecraftVersion ?? TestPackRelease.MinecraftVersion;
        if (_minecraftVersion is not ("26.2" or "26.3"))
            throw new ArgumentException("Unsupported pinned Minecraft version.", nameof(minecraftVersion));
        var previous = _minecraftVersion == "26.3";
        _versionId = $"fabric-loader-{TestPackRelease.FabricLoaderVersion}-{_minecraftVersion}";
        _profileUrl = $"https://meta.fabricmc.net/v2/versions/loader/{_minecraftVersion}/{TestPackRelease.FabricLoaderVersion}/profile/zip";
        _launcherRoot = launcherRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");
        _expectedSha512 = expectedSha512 ?? (previous ? PreviousProfileSha512 : ProfileSha512);
        _expectedClientJarSha512 = expectedClientJarSha512 ?? (previous ? PreviousMinecraftClientJarSha512 : MinecraftClientJarSha512);
        _expectedClientJarSize = expectedClientJarSize ?? (previous ? PreviousMinecraftClientJarSize : MinecraftClientJarSize);
        _http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(45) };
        _ensureLauncherClosed = ensureLauncherClosed ?? new MinecraftLauncherController().EnsureClosed;
    }

    public void CheckReady()
    {
        CheckProfileReady();
        _ensureLauncherClosed();
    }

    public void CheckProfileReady() => _ = FindProfilePath();

    public async Task ConfigureAsync(string gameDirectory, CancellationToken cancellationToken = default)
    {
        CheckReady();
        var profilePath = FindProfilePath();
        HttpResponseMessage response;
        try { response = await _http.GetAsync(_profileUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken); }
        catch (HttpRequestException ex)
        {
            throw new InstallerException("FABRIC_NETWORK", LocalizedText.Get("FabricNetworkFailed"), ex);
        }
        using var responseScope = response;
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength is > 1_000_000)
            throw new InstallerException("FABRIC_DOWNLOAD", LocalizedText.Get("PinnedFabricProfileUnavailable"));
        var archive = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (archive.Length > 1_000_000)
            throw new InstallerException("FABRIC_HASH", LocalizedText.Get("FabricProfileHashInvalid"));

        var (versionJson, versionJar) = ReadProfileArchive(archive);
        var expectedProfileHash = Convert.FromHexString(_expectedSha512);
        if (!MatchesPinnedProfile(versionJson, expectedProfileHash))
            throw new InstallerException("FABRIC_HASH", LocalizedText.Get("FabricProfileHashInvalid"));
        var versionsRoot = Path.Combine(_launcherRoot, "versions");
        var versionPath = Path.Combine(versionsRoot, _versionId);
        SafePath.EnsureNoReparsePoints(_launcherRoot, versionPath);
        Directory.CreateDirectory(versionsRoot);
        var createdVersion = false;
        if (Directory.Exists(versionPath))
        {
            var existingJson = Path.Combine(versionPath, _versionId + ".json");
            var existingJar = Path.Combine(versionPath, _versionId + ".jar");
            SafePath.EnsureNoReparsePoints(_launcherRoot, existingJson);
            SafePath.EnsureNoReparsePoints(_launcherRoot, existingJar);
            if (!File.Exists(existingJson) ||
                !MatchesPinnedProfile(File.ReadAllBytes(existingJson), expectedProfileHash) ||
                !IsExpectedClientJar(existingJar))
                throw new InstallerException("FABRIC_VERSION_CONFLICT", LocalizedText.Get("FabricVersionConflict"));
        }
        else
        {
            var staging = Path.Combine(versionsRoot, ".minepack-" + Guid.NewGuid().ToString("N"));
            SafePath.EnsureNoReparsePoints(_launcherRoot, staging);
            try
            {
                Directory.CreateDirectory(staging);
                await File.WriteAllBytesAsync(Path.Combine(staging, _versionId + ".json"), versionJson, cancellationToken);
                await File.WriteAllBytesAsync(Path.Combine(staging, _versionId + ".jar"), versionJar, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                Directory.Move(staging, versionPath);
                createdVersion = true;
            }
            finally
            {
                SafePath.EnsureNoReparsePoints(_launcherRoot, staging);
                if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            }
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ensureLauncherClosed();
            UpdateProfile(profilePath, json => LauncherProfile.BuildFixtureCandidate(json, gameDirectory,
                _minecraftVersion, TestPackRelease.FabricLoaderVersion));
        }
        catch
        {
            if (createdVersion) RemoveCreatedVersion(versionPath);
            throw;
        }
    }

    private bool IsExpectedClientJar(string path)
    {
        if (!File.Exists(path)) return false;
        using var stream = File.OpenRead(path);
        if (stream.Length == 0) return true;
        return stream.Length == _expectedClientJarSize && CryptographicOperations.FixedTimeEquals(
            SHA512.HashData(stream), Convert.FromHexString(_expectedClientJarSha512));
    }

    public void RemoveOwnProfile(string? expectedGameDirectory = null)
    {
        if (!Directory.Exists(_launcherRoot) ||
            !new[] { "launcher_profiles.json", "launcher_profiles_microsoft_store.json" }
                .Any(name => File.Exists(Path.Combine(_launcherRoot, name)))) return;
        var profilePath = FindProfilePath();
        _ensureLauncherClosed();
        UpdateProfile(profilePath, json => LauncherProfile.RemoveFixtureCandidate(json, expectedGameDirectory));
    }

    private string FindProfilePath()
    {
        if (!Directory.Exists(_launcherRoot))
            throw new InstallerException("LAUNCHER_NOT_FOUND", LocalizedText.Get("OfficialLauncherNotFound"));
        var candidates = new[] { "launcher_profiles.json", "launcher_profiles_microsoft_store.json" }
            .Select(name => Path.Combine(_launcherRoot, name)).Where(File.Exists).ToArray();
        if (candidates.Length != 1)
            throw new InstallerException("LAUNCHER_PROFILE_UNKNOWN", candidates.Length == 0
                ? LocalizedText.Get("LauncherProfilesMissing")
                : LocalizedText.Get("LauncherProfilesAmbiguous"));
        SafePath.EnsureNoReparsePoints(_launcherRoot, candidates[0]);
        return candidates[0];
    }

    private (byte[] Json, byte[] Jar) ReadProfileArchive(byte[] archive)
    {
        using var zip = new ZipArchive(new MemoryStream(archive), ZipArchiveMode.Read);
        var jsonName = $"{_versionId}/{_versionId}.json";
        var jarName = $"{_versionId}/{_versionId}.jar";
        if (zip.Entries.Count != 2 || zip.GetEntry(jsonName) is not { Length: > 0 and < 100_000 } jsonEntry ||
            zip.GetEntry(jarName) is not { Length: 0 } jarEntry)
            throw new InstallerException("FABRIC_ARCHIVE", LocalizedText.Get("FabricArchiveUnexpected"));
        using var reader = new StreamReader(jsonEntry.Open(), Encoding.UTF8);
        var json = reader.ReadToEnd();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("id").GetString() != _versionId ||
            root.GetProperty("inheritsFrom").GetString() != _minecraftVersion)
            throw new InstallerException("FABRIC_ARCHIVE", LocalizedText.Get("FabricArchiveVersionMismatch"));
        return (Encoding.UTF8.GetBytes(json), Array.Empty<byte>());
    }

    private static byte[] StableProfileHash(byte[] json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("time", out var time) || time.ValueKind != JsonValueKind.String ||
            !root.TryGetProperty("releaseTime", out var releaseTime) || releaseTime.ValueKind != JsonValueKind.String)
            throw new JsonException("Fabric profile timestamps are missing or invalid.");
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output))
            WriteStableJson(root, writer, isRoot: true);
        return SHA512.HashData(output.ToArray());
    }

    private static bool MatchesPinnedProfile(byte[] json, byte[] expectedHash)
    {
        try { return CryptographicOperations.FixedTimeEquals(StableProfileHash(json), expectedHash); }
        catch (JsonException) { return false; }
    }

    private static void WriteStableJson(JsonElement value, Utf8JsonWriter writer, bool isRoot = false)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
                {
                    if (isRoot && property.Name is ("time" or "releaseTime")) continue;
                    writer.WritePropertyName(property.Name);
                    WriteStableJson(property.Value, writer);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray()) WriteStableJson(item, writer);
                writer.WriteEndArray();
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }

    private static void UpdateProfile(string path, Func<string, string> change)
    {
        var original = File.ReadAllBytes(path);
        var updated = Encoding.UTF8.GetBytes(change(Encoding.UTF8.GetString(original)));
        if (original.AsSpan().SequenceEqual(updated)) return;
        var temp = path + ".minepack-" + Guid.NewGuid().ToString("N") + ".tmp";
        var backup = path + ".minepack-" + Guid.NewGuid().ToString("N") + ".bak";
        try
        {
            File.WriteAllBytes(temp, updated);
            if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(original))
                throw new InstallerException("LAUNCHER_PROFILE_CHANGED", LocalizedText.Get("LauncherProfileChanged"));
            File.Replace(temp, path, backup);
            try
            {
                if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(updated))
                    throw new IOException("Profile verification failed.");
            }
            catch
            {
                File.Replace(backup, path, null);
                throw;
            }
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private void RemoveCreatedVersion(string versionPath)
    {
        SafePath.EnsureNoReparsePoints(_launcherRoot, versionPath);
        File.Delete(Path.Combine(versionPath, _versionId + ".json"));
        File.Delete(Path.Combine(versionPath, _versionId + ".jar"));
        if (!Directory.EnumerateFileSystemEntries(versionPath).Any()) Directory.Delete(versionPath);
    }

    public void Dispose() => _http.Dispose();
}
