using System.IO;
using System.Linq;
using System.Text.Json;
using MinePack.Core;

namespace MinePack.Installer;

public sealed record InstallerPreferencesState(string? LastValidatedRoot, bool IsCorrupt);

public static class InstallerPreferences
{
    private const int SchemaVersion = 1;
    private const int MaxBytes = 16 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, MaxDepth = 8 };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MinePack", "installer-settings.json");

    public static InstallerPreferencesState Load(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) return new InstallerPreferencesState(null, false);
        try
        {
            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length is <= 0 or > MaxBytes) return new InstallerPreferencesState(null, true);
            var bytes = new byte[(int)stream.Length];
            stream.ReadExactly(bytes);
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 2 ||
                !root.TryGetProperty("SchemaVersion", out var schema) || schema.ValueKind != JsonValueKind.Number ||
                !schema.TryGetInt32(out var version) || version != SchemaVersion ||
                !root.TryGetProperty("LastValidatedRoot", out var savedRoot) || savedRoot.ValueKind != JsonValueKind.String)
                return new InstallerPreferencesState(null, true);
            var savedRootPath = savedRoot.GetString();
            if (string.IsNullOrWhiteSpace(savedRootPath) || !Path.IsPathFullyQualified(savedRootPath))
                return new InstallerPreferencesState(null, true);
            return new InstallerPreferencesState(Path.GetFullPath(savedRootPath), false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
            return new InstallerPreferencesState(null, true);
        }
    }

    public static void SaveLastValidatedRoot(string path, string root)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(InstallService.ValidateInstallRoot(root));
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("Preferences path must have a parent directory.", nameof(path));
        _ = InstallService.ValidateInstallRoot(directory);
        Directory.CreateDirectory(directory);
        _ = InstallService.ValidateInstallRoot(directory);
        var temp = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, new PreferencesDocument(SchemaVersion, fullRoot), JsonOptions);
                stream.Flush(flushToDisk: true);
            }
            if (new FileInfo(temp).Length > MaxBytes) throw new IOException("Preferences data exceeded its size bound.");
            File.Move(temp, fullPath, overwrite: true);
        }
        finally
        {
            try { File.Delete(temp); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private sealed record PreferencesDocument(int SchemaVersion, string LastValidatedRoot);
}
