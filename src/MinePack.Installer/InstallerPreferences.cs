using System.IO;
using System.Linq;
using System.Text.Json;
using MinePack.Core;

namespace MinePack.Installer;

public sealed record InstallerPreferencesState(string? LastValidatedRoot, bool IsCorrupt,
    IReadOnlyList<PrismTargetHint> PrismTargetHints);

public static class InstallerPreferences
{
    private const int SchemaVersion = 2;
    private const int MaxBytes = 16 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, MaxDepth = 8 };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MinePack", "installer-settings.json");

    public static InstallerPreferencesState Load(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) return new InstallerPreferencesState(null, false, []);
        try
        {
            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length is <= 0 or > MaxBytes) return new InstallerPreferencesState(null, true, []);
            var bytes = new byte[(int)stream.Length];
            stream.ReadExactly(bytes);
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("SchemaVersion", out var schema) || schema.ValueKind != JsonValueKind.Number ||
                !schema.TryGetInt32(out var version) || version is not (1 or SchemaVersion) ||
                !root.TryGetProperty("LastValidatedRoot", out var savedRoot) || savedRoot.ValueKind != JsonValueKind.String)
                return new InstallerPreferencesState(null, true, []);
            if (root.EnumerateObject().Count() != (version == 1 ? 2 : 3))
                return new InstallerPreferencesState(null, true, []);
            var savedRootPath = savedRoot.GetString();
            if (string.IsNullOrWhiteSpace(savedRootPath) || !Path.IsPathFullyQualified(savedRootPath))
                return new InstallerPreferencesState(null, true, []);
            var hints = version == 1 ? [] : ReadPrismHints(root);
            return new InstallerPreferencesState(Path.GetFullPath(savedRootPath), false, hints);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
            return new InstallerPreferencesState(null, true, []);
        }
    }

    public static void SaveLastValidatedRoot(string path, string root, IEnumerable<PrismLauncherTarget>? prismTargets = null,
        IEnumerable<PrismTargetHint>? existingHints = null)
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
                if (prismTargets is null && existingHints is null)
                    JsonSerializer.Serialize(stream, new LegacyPreferencesDocument(1, fullRoot), JsonOptions);
                else
                    JsonSerializer.Serialize(stream, new PreferencesDocument(SchemaVersion, fullRoot,
                        prismTargets is not null ? NormalizePrismHints(prismTargets) : NormalizePrismHints(existingHints!)), JsonOptions);
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

    private static IReadOnlyList<PrismTargetHint> ReadPrismHints(JsonElement root)
    {
        if (!root.TryGetProperty("PrismTargets", out var array) || array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > 32)
            throw new JsonException("Invalid saved Prism target hints.");
        var results = new List<PrismTargetHint>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || item.EnumerateObject().Count() != 4 ||
                !ReadPath(item, "ExecutablePath", out var executable) || !ReadPath(item, "DataRoot", out var dataRoot) ||
                !ReadPath(item, "InstancesRoot", out var instancesRoot) ||
                !item.TryGetProperty("Fingerprint", out var fingerprintElement) || fingerprintElement.ValueKind != JsonValueKind.String)
                throw new JsonException("Invalid saved Prism target hints.");
            var fingerprint = fingerprintElement.GetString();
            if (fingerprint is null || fingerprint.Length != 64 || fingerprint.Any(character => !Uri.IsHexDigit(character)) ||
                !InstallationLayout.ComputePrismFingerprint(dataRoot, instancesRoot).Equals(fingerprint, StringComparison.OrdinalIgnoreCase))
                throw new JsonException("Invalid saved Prism target hints.");
            results.Add(new PrismTargetHint(executable, dataRoot, instancesRoot, fingerprint.ToLowerInvariant()));
        }
        return results;
    }

    private static bool ReadPath(JsonElement item, string name, out string value)
    {
        value = "";
        if (!item.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String ||
            element.GetString() is not { Length: > 0 and <= 2048 } path || !Path.IsPathFullyQualified(path) ||
            path.StartsWith("\\\\?\\", StringComparison.Ordinal) || path.StartsWith("\\\\.\\", StringComparison.Ordinal))
            return false;
        value = Path.GetFullPath(path);
        return true;
    }

    private static PrismTargetHint[] NormalizePrismHints(IEnumerable<PrismLauncherTarget> targets) => targets
        .Take(32)
        .Select(target => new PrismTargetHint(Path.GetFullPath(target.ExecutablePath), Path.GetFullPath(target.DataRoot),
            Path.GetFullPath(target.InstancesRoot), target.Fingerprint.ToLowerInvariant()))
        .Where(target => Path.IsPathFullyQualified(target.ExecutablePath) && Path.IsPathFullyQualified(target.DataRoot) &&
                         Path.IsPathFullyQualified(target.InstancesRoot) && target.Fingerprint.Length == 64 &&
                         InstallationLayout.ComputePrismFingerprint(target.DataRoot, target.InstancesRoot)
                             .Equals(target.Fingerprint, StringComparison.OrdinalIgnoreCase))
        .DistinctBy(target => target.ExecutablePath + "\n" + target.Fingerprint, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static PrismTargetHint[] NormalizePrismHints(IEnumerable<PrismTargetHint> hints) => hints
        .Take(32)
        .Select(target => new PrismTargetHint(Path.GetFullPath(target.ExecutablePath), Path.GetFullPath(target.DataRoot),
            Path.GetFullPath(target.InstancesRoot), target.Fingerprint.ToLowerInvariant()))
        .Where(target => Path.IsPathFullyQualified(target.ExecutablePath) && Path.IsPathFullyQualified(target.DataRoot) &&
                         Path.IsPathFullyQualified(target.InstancesRoot) && target.Fingerprint.Length == 64 &&
                         InstallationLayout.ComputePrismFingerprint(target.DataRoot, target.InstancesRoot)
                             .Equals(target.Fingerprint, StringComparison.OrdinalIgnoreCase))
        .DistinctBy(target => target.ExecutablePath + "\n" + target.Fingerprint, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private sealed record LegacyPreferencesDocument(int SchemaVersion, string LastValidatedRoot);
    private sealed record PreferencesDocument(int SchemaVersion, string LastValidatedRoot, PrismTargetHint[] PrismTargets);
}
