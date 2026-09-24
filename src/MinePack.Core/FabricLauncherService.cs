using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MinePack.Core;

public sealed class FabricLauncherService : IDisposable
{
    private const string ProfileSha512 = "A33455AA111C1EB32E79D22716CDFA499744E0625C7491E12200AE2D188E1E0F5DA45372D8723BDBAEB3BA814F2248165B2EB50366A82C969EB8B542ED9E0280";
    private const string PreviousProfileSha512 = "E951DB8CFBFCCBFDB95DA6EBDD2E89F4B3C1E5F821F75EA7629EE35DD5782AF2451DCC447F21B2F1A264A3A86407470DEDF56651B593E051C858DFCC2D5845AC";
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

    public FabricLauncherService(string? launcherRoot = null, HttpMessageHandler? handler = null, string? expectedSha512 = null,
        string? expectedClientJarSha512 = null, long? expectedClientJarSize = null, string? minecraftVersion = null)
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
    }

    public void CheckReady()
    {
        _ = FindProfilePath();
        EnsureLauncherClosed();
    }

    public async Task ConfigureAsync(string gameDirectory, CancellationToken cancellationToken = default)
    {
        CheckReady();
        var profilePath = FindProfilePath();
        HttpResponseMessage response;
        try { response = await _http.GetAsync(_profileUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken); }
        catch (HttpRequestException ex)
        {
            throw new InstallerException("FABRIC_NETWORK", "Не удалось связаться с официальным сервером Fabric. Проверьте подключение и повторите попытку.", ex);
        }
        using var responseScope = response;
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength is > 1_000_000)
            throw new InstallerException("FABRIC_DOWNLOAD", "Не удалось получить закреплённый профиль Fabric с официального сервера.");
        var archive = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (archive.Length > 1_000_000 || !CryptographicOperations.FixedTimeEquals(
                SHA512.HashData(archive), Convert.FromHexString(_expectedSha512)))
            throw new InstallerException("FABRIC_HASH", "Проверка целостности профиля Fabric не прошла.");

        var (versionJson, versionJar) = ReadProfileArchive(archive);
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
                !File.ReadAllBytes(existingJson).AsSpan().SequenceEqual(versionJson) ||
                !IsExpectedClientJar(existingJar))
                throw new InstallerException("FABRIC_VERSION_CONFLICT", "Версия Fabric с таким именем уже существует и отличается от закреплённой. Чужие файлы не изменены.");
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
        EnsureLauncherClosed();
        UpdateProfile(profilePath, json => LauncherProfile.RemoveFixtureCandidate(json, expectedGameDirectory));
    }

    private string FindProfilePath()
    {
        if (!Directory.Exists(_launcherRoot))
            throw new InstallerException("LAUNCHER_NOT_FOUND", "Официальный Minecraft Launcher не найден. Установите и один раз откройте его.");
        var candidates = new[] { "launcher_profiles.json", "launcher_profiles_microsoft_store.json" }
            .Select(name => Path.Combine(_launcherRoot, name)).Where(File.Exists).ToArray();
        if (candidates.Length != 1)
            throw new InstallerException("LAUNCHER_PROFILE_UNKNOWN", candidates.Length == 0
                ? "Файл профилей Launcher не найден. Откройте официальный Launcher один раз и закройте его."
                : "Найдены два варианта профилей Launcher; автоматический выбор невозможен без риска изменить не тот профиль.");
        SafePath.EnsureNoReparsePoints(_launcherRoot, candidates[0]);
        return candidates[0];
    }

    private static void EnsureLauncherClosed()
    {
        if (Process.GetProcesses().Any(process => process.ProcessName.Contains("MinecraftLauncher", StringComparison.OrdinalIgnoreCase)))
            throw new InstallerException("LAUNCHER_RUNNING", "Закройте Minecraft Launcher и повторите настройку. Открытый Launcher может перезаписать профиль.");
    }

    private (byte[] Json, byte[] Jar) ReadProfileArchive(byte[] archive)
    {
        using var zip = new ZipArchive(new MemoryStream(archive), ZipArchiveMode.Read);
        var jsonName = $"{_versionId}/{_versionId}.json";
        var jarName = $"{_versionId}/{_versionId}.jar";
        if (zip.Entries.Count != 2 || zip.GetEntry(jsonName) is not { Length: > 0 and < 100_000 } jsonEntry ||
            zip.GetEntry(jarName) is not { Length: 0 } jarEntry)
            throw new InstallerException("FABRIC_ARCHIVE", "Официальный пакет Fabric имеет неожиданную структуру.");
        using var reader = new StreamReader(jsonEntry.Open(), Encoding.UTF8);
        var json = reader.ReadToEnd();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("id").GetString() != _versionId ||
            root.GetProperty("inheritsFrom").GetString() != _minecraftVersion)
            throw new InstallerException("FABRIC_ARCHIVE", "Версии в пакете Fabric не соответствуют тестовому релизу.");
        return (Encoding.UTF8.GetBytes(json), Array.Empty<byte>());
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
                throw new InstallerException("LAUNCHER_PROFILE_CHANGED", "Профиль Launcher изменился во время установки. Повторите попытку после закрытия Launcher.");
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
