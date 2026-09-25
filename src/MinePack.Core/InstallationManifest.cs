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
        SafePath.EnsureNoReparsePoints(instancePath, path);
        try
        {
            var manifest = JsonSerializer.Deserialize<InstallationManifest>(File.ReadAllText(path))
                ?? throw new InstallerException("MANIFEST_INVALID", LocalizedText.Get("ManifestEmpty"));
            manifest.Validate();
            return manifest;
        }
        catch (InstallerException) { throw; }
        catch (JsonException ex) { throw new InstallerException("MANIFEST_INVALID", LocalizedText.Get("ManifestCorrupt"), ex); }
        catch (IOException ex) { throw new InstallerException("MANIFEST_READ_FAILED", LocalizedText.Get("ManifestReadFailed"), ex); }
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
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Files)
        {
            var normalized = SafePath.ValidateRelative(file.Path);
            if (!paths.Add(normalized) || normalized.Equals(FileName, StringComparison.OrdinalIgnoreCase))
                throw new InstallerException("MANIFEST_INVALID", LocalizedText.Get("ManifestDuplicateOrReservedPath"));
            if (!PackArchive.IsSha512(file.Sha512) || file.Size < 0)
                throw new InstallerException("MANIFEST_INVALID", LocalizedText.Get("ManifestHashOrSizeInvalid"));
            if (!file.IsOverride && (file.Downloads.Length == 0 || file.Downloads.Any(url =>
                    !Uri.TryCreate(url, UriKind.Absolute, out var uri) || !PackArchive.IsAllowedDownloadUri(uri))))
                throw new InstallerException("MANIFEST_INVALID", LocalizedText.Get("ManifestDownloadSourceInvalid"));
        }
    }
}

public sealed class InstallerException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
}

public sealed record InstallProgress(string Stage, string Message, int CompletedFiles = 0, int TotalFiles = 0, long BytesReceived = 0, long? ExpectedBytes = null);

public sealed record InstallResult(bool Success, string Code, string Message, string? GameDirectory, string? LogPath);
