using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MinePack.Core;

public sealed class FabricLauncherService : IDisposable
{
    private const string ProfileUrl = "https://meta.fabricmc.net/v2/versions/loader/26.3/0.19.5/profile/zip";
    private const string ProfileSha512 = "E951DB8CFBFCCBFDB95DA6EBDD2E89F4B3C1E5F821F75EA7629EE35DD5782AF2451DCC447F21B2F1A264A3A86407470DEDF56651B593E051C858DFCC2D5845AC";
    private const string VersionId = "fabric-loader-0.19.5-26.3";
    private readonly string _launcherRoot;
    private readonly string _expectedSha512;
    private readonly HttpClient _http;

    public FabricLauncherService(string? launcherRoot = null, HttpMessageHandler? handler = null, string? expectedSha512 = null)
    {
        _launcherRoot = launcherRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");
        _expectedSha512 = expectedSha512 ?? ProfileSha512;
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
        try { response = await _http.GetAsync(ProfileUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken); }
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
        var versionPath = Path.Combine(versionsRoot, VersionId);
        SafePath.EnsureNoReparsePoints(_launcherRoot, versionPath);
        Directory.CreateDirectory(versionsRoot);
        var createdVersion = false;
        if (Directory.Exists(versionPath))
        {
            if (!File.Exists(Path.Combine(versionPath, VersionId + ".json")) ||
                !File.ReadAllBytes(Path.Combine(versionPath, VersionId + ".json")).AsSpan().SequenceEqual(versionJson) ||
                !File.Exists(Path.Combine(versionPath, VersionId + ".jar")) ||
                !File.ReadAllBytes(Path.Combine(versionPath, VersionId + ".jar")).AsSpan().SequenceEqual(versionJar))
                throw new InstallerException("FABRIC_VERSION_CONFLICT", "Версия Fabric с таким именем уже существует и отличается от закреплённой. Чужие файлы не изменены.");
        }
        else
        {
            var staging = Path.Combine(versionsRoot, ".minepack-" + Guid.NewGuid().ToString("N"));
            SafePath.EnsureNoReparsePoints(_launcherRoot, staging);
            try
            {
                Directory.CreateDirectory(staging);
                await File.WriteAllBytesAsync(Path.Combine(staging, VersionId + ".json"), versionJson, cancellationToken);
                await File.WriteAllBytesAsync(Path.Combine(staging, VersionId + ".jar"), versionJar, cancellationToken);
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
                TestPackRelease.MinecraftVersion, TestPackRelease.FabricLoaderVersion));
        }
        catch
        {
            if (createdVersion) RemoveCreatedVersion(versionPath);
            throw;
        }
    }

    public void RemoveOwnProfile()
    {
        if (!Directory.Exists(_launcherRoot) ||
            !new[] { "launcher_profiles.json", "launcher_profiles_microsoft_store.json" }
                .Any(name => File.Exists(Path.Combine(_launcherRoot, name)))) return;
        var profilePath = FindProfilePath();
        EnsureLauncherClosed();
        UpdateProfile(profilePath, LauncherProfile.RemoveFixtureCandidate);
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

    private static (byte[] Json, byte[] Jar) ReadProfileArchive(byte[] archive)
    {
        using var zip = new ZipArchive(new MemoryStream(archive), ZipArchiveMode.Read);
        var jsonName = $"{VersionId}/{VersionId}.json";
        var jarName = $"{VersionId}/{VersionId}.jar";
        if (zip.Entries.Count != 2 || zip.GetEntry(jsonName) is not { Length: > 0 and < 100_000 } jsonEntry ||
            zip.GetEntry(jarName) is not { Length: 0 } jarEntry)
            throw new InstallerException("FABRIC_ARCHIVE", "Официальный пакет Fabric имеет неожиданную структуру.");
        using var reader = new StreamReader(jsonEntry.Open(), Encoding.UTF8);
        var json = reader.ReadToEnd();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("id").GetString() != VersionId ||
            root.GetProperty("inheritsFrom").GetString() != TestPackRelease.MinecraftVersion)
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
        File.Delete(Path.Combine(versionPath, VersionId + ".json"));
        File.Delete(Path.Combine(versionPath, VersionId + ".jar"));
        if (!Directory.EnumerateFileSystemEntries(versionPath).Any()) Directory.Delete(versionPath);
    }

    public void Dispose() => _http.Dispose();
}
