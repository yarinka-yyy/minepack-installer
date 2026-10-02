using System.Text.Json;

namespace MinePack.Core;

public sealed record ManagedFile(string Path, string Sha512, string[] Downloads, bool IsOverride, long Size);

public sealed class InstallationManifest
{
    public const string FileName = ".minepack-installation.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public int SchemaVersion { get; init; } = 1;
    public string PackVersion { get; init; } = "";
    public string MinecraftVersion { get; init; } = "";
    public string FabricLoaderVersion { get; init; } = "";
    public string PackArchiveSha512 { get; init; } = "";
    public DateTimeOffset InstalledAt { get; init; }
    public List<ManagedFile> Files { get; init; } = [];

    public static InstallationManifest Load(string instancePath)
    {
        var path = SafePath.Resolve(instancePath, FileName);
        try
        {
            SafePath.EnsureNoReparsePoints(instancePath, path);
            var json = File.ReadAllText(path);
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            ValidateJsonShape(document.RootElement);
            var manifest = JsonSerializer.Deserialize<InstallationManifest>(json)
                ?? throw new InstallerException("MANIFEST_INVALID", LocalizedText.Get("ManifestEmpty"));
            manifest.Validate();
            return manifest;
        }
        catch (InstallerException) { throw; }
        catch (JsonException ex) { throw new InstallerException("MANIFEST_INVALID", LocalizedText.Get("ManifestCorrupt"), ex); }
        catch (IOException ex) { throw new InstallerException("MANIFEST_READ_FAILED", LocalizedText.Get("ManifestReadFailed"), ex); }
        catch (UnauthorizedAccessException ex) { throw new InstallerException("MANIFEST_ACCESS_DENIED", LocalizedText.Get("ManifestAccessDenied"), ex); }
    }

    public void SaveAtomic(string instancePath)
    {
        Validate();
        Directory.CreateDirectory(instancePath);
        var path = SafePath.Resolve(instancePath, FileName);
        SafePath.EnsureNoReparsePoints(instancePath, path);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, this, JsonOptions);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            try { File.Delete(temp); }
            catch (IOException) { }
        }
    }

    private void Validate()
    {
        if (SchemaVersion != 1) throw new InstallerException("MANIFEST_SCHEMA_UNSUPPORTED", LocalizedText.Get("ManifestSchemaUnsupported"));
        if (string.IsNullOrWhiteSpace(PackVersion) || string.IsNullOrWhiteSpace(MinecraftVersion) ||
            string.IsNullOrWhiteSpace(FabricLoaderVersion) || !PackArchive.IsSha512(PackArchiveSha512))
            throw new InstallerException("MANIFEST_INVALID", LocalizedText.Get("ManifestVersionsOrHashInvalid"));
        if (Files is null)
            throw new InstallerException("MANIFEST_INVALID", LocalizedText.Get("ManifestCorrupt"));
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Files)
        {
            if (file is null || string.IsNullOrWhiteSpace(file.Path))
                throw new InstallerException("MANIFEST_INVALID", LocalizedText.Get("ManifestCorrupt"));
            string normalized;
            try { normalized = SafePath.ValidateRelative(file.Path); }
            catch (InstallerException ex) { throw new InstallerException("MANIFEST_INVALID", LocalizedText.Get("ManifestCorrupt"), ex); }
            var firstSegment = normalized.Split('/')[0];
            if (!paths.Add(normalized) || normalized.Equals(FileName, StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals(".minepack-active.json", StringComparison.OrdinalIgnoreCase) ||
                firstSegment.Equals("saves", StringComparison.OrdinalIgnoreCase) ||
                firstSegment.Equals("screenshots", StringComparison.OrdinalIgnoreCase))
                throw new InstallerException("MANIFEST_INVALID", LocalizedText.Get("ManifestDuplicateOrReservedPath"));
            if (!PackArchive.IsSha512(file.Sha512) || file.Size < 0)
                throw new InstallerException("MANIFEST_INVALID", LocalizedText.Get("ManifestHashOrSizeInvalid"));
            if (file.Downloads is null ||
                (file.IsOverride ? file.Downloads.Length != 0 : file.Downloads.Length == 0) ||
                file.Downloads.Any(url => url is null || !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                    !PackArchive.IsAllowedDownloadUri(uri)))
                throw new InstallerException("MANIFEST_INVALID", LocalizedText.Get("ManifestDownloadSourceInvalid"));
        }
    }

    private static void ValidateJsonShape(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty(nameof(SchemaVersion), out var schema) || schema.ValueKind != JsonValueKind.Number ||
            !schema.TryGetInt32(out _))
            throw new InstallerException("MANIFEST_INVALID", LocalizedText.Get("ManifestCorrupt"));
        if (schema.GetInt32() != 1)
            throw new InstallerException("MANIFEST_SCHEMA_UNSUPPORTED", LocalizedText.Get("ManifestSchemaUnsupported"));

        foreach (var name in new[]
                 {
                     nameof(PackVersion), nameof(MinecraftVersion), nameof(FabricLoaderVersion),
                     nameof(PackArchiveSha512), nameof(InstalledAt)
                 })
            if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
                throw new InstallerException("MANIFEST_INVALID", LocalizedText.Get("ManifestCorrupt"));

        if (!root.TryGetProperty(nameof(Files), out var files) || files.ValueKind != JsonValueKind.Array)
            throw new InstallerException("MANIFEST_INVALID", LocalizedText.Get("ManifestCorrupt"));
        foreach (var file in files.EnumerateArray())
        {
            if (file.ValueKind != JsonValueKind.Object ||
                !file.TryGetProperty(nameof(ManagedFile.Path), out var path) || path.ValueKind != JsonValueKind.String ||
                !file.TryGetProperty(nameof(ManagedFile.Sha512), out var hash) || hash.ValueKind != JsonValueKind.String ||
                !file.TryGetProperty(nameof(ManagedFile.Downloads), out var downloads) || downloads.ValueKind != JsonValueKind.Array ||
                !file.TryGetProperty(nameof(ManagedFile.IsOverride), out var isOverride) ||
                isOverride.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                !file.TryGetProperty(nameof(ManagedFile.Size), out var size) || size.ValueKind != JsonValueKind.Number ||
                !size.TryGetInt64(out _))
                throw new InstallerException("MANIFEST_INVALID", LocalizedText.Get("ManifestCorrupt"));
            if (downloads.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String))
                throw new InstallerException("MANIFEST_INVALID", LocalizedText.Get("ManifestCorrupt"));
        }
    }
}

public class InstallerException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
}

public sealed record InstallProgress(string Stage, string Message, int CompletedFiles = 0, int TotalFiles = 0, long BytesReceived = 0, long? ExpectedBytes = null);

public sealed record InstallResult(bool Success, string Code, string Message, string? GameDirectory, string? LogPath);
