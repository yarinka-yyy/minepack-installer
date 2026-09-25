using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace MinePack.Core;

public sealed record PackFile(string Path, IReadOnlyList<Uri> Downloads, string Sha512, long Size);

public sealed record PackOverride(string Path, string ArchivePath, long Size);

public sealed class PackArchive
{
    private const int MaxManifestBytes = 4 * 1024 * 1024;
    private const int MaxFileCount = 10_000;
    private const int MaxArchiveEntries = 20_000;
    private const long MaxOverrideBytes = 64 * 1024 * 1024;
    private readonly string _archivePath;

    private PackArchive(string archivePath, string name, string versionId, string minecraftVersion,
        string fabricLoaderVersion, IReadOnlyList<PackFile> files, IReadOnlyList<PackOverride> overrides,
        string archiveSha512)
    {
        _archivePath = archivePath;
        Name = name;
        VersionId = versionId;
        MinecraftVersion = minecraftVersion;
        FabricLoaderVersion = fabricLoaderVersion;
        Files = files;
        Overrides = overrides;
        ArchiveSha512 = archiveSha512;
    }

    public string Name { get; }
    public string VersionId { get; }
    public string MinecraftVersion { get; }
    public string FabricLoaderVersion { get; }
    public IReadOnlyList<PackFile> Files { get; }
    public IReadOnlyList<PackOverride> Overrides { get; }
    public string ArchiveSha512 { get; }

    public static PackArchive Open(string path, string? expectedArchiveSha512 = null)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) throw new InstallerException("PACK_NOT_FOUND", LocalizedText.Get("PackFileMissing"));

        try
        {
            using var file = File.OpenRead(fullPath);
            var archiveHash = Convert.ToHexString(SHA512.HashData(file));
            if (expectedArchiveSha512 is not null && !FixedTimeHashEquals(archiveHash, expectedArchiveSha512))
                throw new InstallerException("PACK_HASH_MISMATCH", LocalizedText.Get("PinnedPackCorrupt"));
            file.Position = 0;
            using var zip = new ZipArchive(file, ZipArchiveMode.Read);
            if (zip.Entries.Count > MaxArchiveEntries)
                throw new InstallerException("PACK_TOO_MANY_FILES", LocalizedText.Get("ArchiveTooManyEntries"));
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var entryKinds = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            ZipArchiveEntry? manifestEntry = null;
            var overrideEntries = new List<PackOverride>();
            long totalOverrideBytes = 0;

            foreach (var entry in zip.Entries)
            {
                var isDirectory = entry.FullName.EndsWith('/');
                var relative = SafePath.ValidateRelative(entry.FullName, isDirectory);
                if (!names.Add(relative)) throw new InstallerException("PACK_DUPLICATE_PATH", LocalizedText.Get("ArchiveDuplicatePath"));
                var segments = relative.Split('/');
                for (var i = 1; i < segments.Length; i++)
                {
                    var parent = string.Join('/', segments.Take(i));
                    if (entryKinds.TryGetValue(parent, out var parentIsDirectory) && !parentIsDirectory)
                        throw new InstallerException("PACK_DUPLICATE_PATH", LocalizedText.Get("ArchiveFileParentConflict"));
                }
                if (!isDirectory && entryKinds.Keys.Any(existing => existing.StartsWith(relative + "/", StringComparison.OrdinalIgnoreCase)))
                    throw new InstallerException("PACK_DUPLICATE_PATH", LocalizedText.Get("ArchiveDirectoryFileConflict"));
                entryKinds.Add(relative, isDirectory);
                RejectLink(entry);

                if (relative.Equals("modrinth.index.json", StringComparison.OrdinalIgnoreCase))
                {
                    if (isDirectory || manifestEntry is not null)
                        throw new InstallerException("PACK_INVALID_INDEX", LocalizedText.Get("PackIndexInvalid"));
                    if (entry.Length > MaxManifestBytes)
                        throw new InstallerException("PACK_INDEX_TOO_LARGE", LocalizedText.Get("PackIndexTooLarge"));
                    manifestEntry = entry;
                    continue;
                }

                if (isDirectory)
                {
                    if (!relative.Equals("overrides", StringComparison.OrdinalIgnoreCase) &&
                        !relative.Equals("client-overrides", StringComparison.OrdinalIgnoreCase) &&
                        !relative.StartsWith("overrides/", StringComparison.OrdinalIgnoreCase) &&
                        !relative.StartsWith("client-overrides/", StringComparison.OrdinalIgnoreCase))
                        throw new InstallerException("PACK_UNSUPPORTED_ENTRY", LocalizedText.Get("ArchiveUnsupportedDirectory"));
                    continue;
                }

                var target = relative.StartsWith("overrides/", StringComparison.OrdinalIgnoreCase)
                    ? relative["overrides/".Length..]
                    : relative.StartsWith("client-overrides/", StringComparison.OrdinalIgnoreCase)
                        ? relative["client-overrides/".Length..]
                        : throw new InstallerException("PACK_UNSUPPORTED_ENTRY", LocalizedText.Get("ArchiveUnsupportedFile"));
                target = SafePath.ValidateRelative(target);
                ValidateManagedTarget(target);
                if (!names.Add("override-target:" + target))
                    throw new InstallerException("PACK_DUPLICATE_PATH", LocalizedText.Get("PackOverridesConflict"));
                totalOverrideBytes += entry.Length;
                if (entry.Length > MaxOverrideBytes || totalOverrideBytes > MaxOverrideBytes)
                    throw new InstallerException("PACK_OVERRIDE_TOO_LARGE", LocalizedText.Get("PackOverridesTooLarge"));
                overrideEntries.Add(new PackOverride(target, entry.FullName, entry.Length));
            }

            if (manifestEntry is null) throw new InstallerException("PACK_INVALID_INDEX", LocalizedText.Get("ModrinthIndexMissing"));
            var (name, version, minecraft, fabric, files) = ReadIndex(manifestEntry);
            var allTargets = new HashSet<string>(overrideEntries.Select(x => x.Path), StringComparer.OrdinalIgnoreCase);
            foreach (var packFile in files)
                if (!allTargets.Add(packFile.Path))
                    throw new InstallerException("PACK_DUPLICATE_PATH", LocalizedText.Get("IndexedFileOverridesConflict"));

            return new PackArchive(fullPath, name, version, minecraft, fabric, files, overrideEntries, archiveHash);
        }
        catch (InstallerException) { throw; }
        catch (InvalidDataException ex) { throw new InstallerException("PACK_INVALID_ARCHIVE", LocalizedText.Get("PackArchiveInvalid"), ex); }
        catch (JsonException ex) { throw new InstallerException("PACK_INVALID_INDEX", LocalizedText.Get("PackIndexJsonInvalid"), ex); }
        catch (IOException ex) { throw new InstallerException("PACK_READ_FAILED", LocalizedText.Get("PackReadFailed"), ex); }
    }

    public async Task<IReadOnlyList<ManagedFile>> ExtractOverridesAsync(string stagingRoot, CancellationToken cancellationToken)
    {
        var result = new List<ManagedFile>();
        using var file = File.OpenRead(_archivePath);
        var archiveHash = Convert.ToHexString(SHA512.HashData(file));
        if (!FixedTimeHashEquals(archiveHash, ArchiveSha512))
            throw new InstallerException("PACK_HASH_MISMATCH", LocalizedText.Get("PackChangedAfterCheck"));
        file.Position = 0;
        if (Overrides.Count == 0) return result;

        using var zip = new ZipArchive(file, ZipArchiveMode.Read);
        foreach (var item in Overrides)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = zip.GetEntry(item.ArchivePath)
                ?? throw new InstallerException("PACK_INVALID_ARCHIVE", LocalizedText.Get("OverrideFileMissing"));
            var target = SafePath.Resolve(stagingRoot, item.Path);
            SafePath.EnsureNoReparsePoints(stagingRoot, target);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            await using (var source = entry.Open())
            await using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[81920];
                long copied = 0;
                while (true)
                {
                    var count = await source.ReadAsync(buffer, cancellationToken);
                    if (count == 0) break;
                    copied += count;
                    if (copied > item.Size || copied > MaxOverrideBytes)
                        throw new InstallerException("OVERRIDE_SIZE_MISMATCH", LocalizedText.Get("OverrideExtractedTooLarge"));
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                }
                if (copied != item.Size)
                    throw new InstallerException("OVERRIDE_SIZE_MISMATCH", LocalizedText.Get("OverrideSizeMismatch"));
            }

            var info = new FileInfo(target);
            if (info.Length != item.Size) throw new InstallerException("OVERRIDE_SIZE_MISMATCH", LocalizedText.Get("OverrideSizeMismatch"));
            result.Add(new ManagedFile(item.Path, HashFile(target), [], true, info.Length));
        }
        return result;
    }

    private static (string Name, string Version, string Minecraft, string Fabric, IReadOnlyList<PackFile> Files) ReadIndex(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var bounded = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var count = stream.Read(buffer, 0, buffer.Length);
            if (count == 0) break;
            if (bounded.Length + count > MaxManifestBytes)
                throw new InstallerException("PACK_INDEX_TOO_LARGE", LocalizedText.Get("PackIndexTooLarge"));
            bounded.Write(buffer, 0, count);
        }
        using var document = JsonDocument.Parse(bounded.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || RequiredInt(root, "formatVersion") != 1 || RequiredString(root, "game") != "minecraft")
            throw new InstallerException("PACK_SCHEMA_UNSUPPORTED", LocalizedText.Get("PackSchemaUnsupported"));

        var name = RequiredString(root, "name");
        var version = RequiredString(root, "versionId");
        if (name.Length > 200 || version.Length > 80 || version.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_')))
            throw new InstallerException("PACK_INVALID_INDEX", LocalizedText.Get("PackNameOrVersionInvalid"));

        var dependencies = RequiredObject(root, "dependencies");
        var minecraft = RequiredString(dependencies, "minecraft");
        var fabric = RequiredString(dependencies, "fabric-loader");
        if (dependencies.EnumerateObject().Any(x => x.Name is not ("minecraft" or "fabric-loader")))
            throw new InstallerException("PACK_UNSUPPORTED_DEPENDENCY", LocalizedText.Get("PackDependencyUnsupported"));

        var filesElement = RequiredArray(root, "files");
        if (filesElement.GetArrayLength() > MaxFileCount)
            throw new InstallerException("PACK_TOO_MANY_FILES", LocalizedText.Get("PackTooManyFiles"));
        var files = new List<PackFile>(filesElement.GetArrayLength());
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in filesElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                throw new InstallerException("PACK_INVALID_INDEX", LocalizedText.Get("IndexedFileStructureUnsupported"));
            var filePath = SafePath.ValidateRelative(RequiredString(item, "path"));
            ValidateManagedTarget(filePath);
            if (!paths.Add(filePath)) throw new InstallerException("PACK_DUPLICATE_PATH", LocalizedText.Get("IndexDuplicatePath"));
            var hashes = RequiredObject(item, "hashes");
            var sha512 = RequiredString(hashes, "sha512");
            if (!IsSha512(sha512)) throw new InstallerException("PACK_INVALID_HASH", LocalizedText.Get("IndexSha512Invalid"));
            var downloadsElement = RequiredArray(item, "downloads");
            if (downloadsElement.GetArrayLength() is < 1 or > 10)
                throw new InstallerException("PACK_INVALID_DOWNLOADS", LocalizedText.Get("DownloadListInvalid"));
            var downloads = downloadsElement.EnumerateArray().Select(x =>
            {
                if (x.ValueKind != JsonValueKind.String)
                    throw new InstallerException("PACK_INVALID_DOWNLOADS", LocalizedText.Get("DownloadAddressInvalid"));
                return ValidateDownloadUrl(x.GetString() ?? "");
            }).ToArray();
            var size = -1L;
            if (item.TryGetProperty("fileSize", out var sizeElement) && (!sizeElement.TryGetInt64(out size) || size < 0))
                throw new InstallerException("PACK_INVALID_INDEX", LocalizedText.Get("IndexFileSizeInvalid"));
            var clientEnvironment = "required";
            if (item.TryGetProperty("env", out var env))
            {
                if (env.ValueKind != JsonValueKind.Object || !env.TryGetProperty("client", out var client) || client.ValueKind != JsonValueKind.String)
                    throw new InstallerException("PACK_INVALID_INDEX", LocalizedText.Get("FileEnvironmentInvalid"));
                clientEnvironment = client.GetString() ?? "";
            }
            if (clientEnvironment != "required")
                throw new InstallerException("PACK_OPTIONAL_UNSUPPORTED", LocalizedText.Get("OptionalFilesUnsupported"));
            files.Add(new PackFile(filePath, downloads, sha512.ToLowerInvariant(), size));
        }
        return (name, version, minecraft, fabric, files);
    }

    private static Uri ValidateDownloadUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals("cdn.modrinth.com", StringComparison.OrdinalIgnoreCase) || uri.Port != 443 ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InstallerException("DOWNLOAD_URL_BLOCKED", LocalizedText.Get("DownloadSourceBlocked"));
        return uri;
    }

    private static void ValidateManagedTarget(string path)
    {
        var firstSegment = path.Split('/')[0];
        if (path.Equals(InstallationManifest.FileName, StringComparison.OrdinalIgnoreCase) ||
            path.Equals(".minepack-active.json", StringComparison.OrdinalIgnoreCase) ||
            firstSegment.Equals("saves", StringComparison.OrdinalIgnoreCase) ||
            firstSegment.Equals("screenshots", StringComparison.OrdinalIgnoreCase))
            throw new InstallerException("PACK_RESERVED_PATH", LocalizedText.Get("ReservedPackPath"));
    }

    internal static bool IsAllowedDownloadUri(Uri uri) =>
        uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps &&
        uri.Host.Equals("cdn.modrinth.com", StringComparison.OrdinalIgnoreCase) && uri.Port == 443 &&
        string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Fragment);

    internal static bool IsSha512(string value) => value.Length == 128 && value.All(Uri.IsHexDigit);

    internal static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA512.HashData(stream));
    }

    internal static bool FixedTimeHashEquals(string actual, string expected)
    {
        if (!IsSha512(actual) || !IsSha512(expected)) return false;
        return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actual), Convert.FromHexString(expected));
    }

    private static string RequiredString(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : throw new InstallerException("PACK_INVALID_INDEX", LocalizedText.Get("IndexRequiredFieldMissing", name));

    private static int RequiredInt(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.TryGetInt32(out var number)
            ? number
            : throw new InstallerException("PACK_INVALID_INDEX", LocalizedText.Get("IndexRequiredFieldMissing", name));

    private static JsonElement RequiredObject(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : throw new InstallerException("PACK_INVALID_INDEX", LocalizedText.Get("IndexRequiredFieldMissing", name));

    private static JsonElement RequiredArray(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value
            : throw new InstallerException("PACK_INVALID_INDEX", LocalizedText.Get("IndexRequiredFieldMissing", name));

    private static void RejectLink(ZipArchiveEntry entry)
    {
        const int fileTypeMask = 0xF000;
        const int symbolicLink = 0xA000;
        if (((entry.ExternalAttributes >> 16) & fileTypeMask) == symbolicLink ||
            (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
            throw new InstallerException("PACK_LINK_BLOCKED", LocalizedText.Get("ArchiveLinkBlocked"));
    }
}

internal static class SafePath
{
    public static string ValidateRelative(string value, bool directory = false)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 1024 || value.StartsWith('/') || value.StartsWith('\\') || value.Contains('\\') || value.Contains(':') || value.Contains('\0'))
            throw new InstallerException("PATH_BLOCKED", LocalizedText.Get("FilePathUnsafe"));
        if (directory && value.EndsWith('/')) value = value[..^1];
        var parts = value.Split('/');
        if (parts.Any(part => part.Length is 0 or > 255 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ') ||
                              part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || IsDeviceName(part)))
            throw new InstallerException("PATH_BLOCKED", LocalizedText.Get("FilePathSegmentBlocked"));
        return string.Join('/', parts);
    }

    public static string Resolve(string root, string relative)
    {
        var normalized = ValidateRelative(relative);
        var fullRoot = Path.GetFullPath(root);
        var rootWithSeparator = Path.EndsInDirectorySeparator(fullRoot) ? fullRoot : fullRoot + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(rootWithSeparator, normalized.Replace('/', Path.DirectorySeparatorChar)));
        if (!candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new InstallerException("PATH_BLOCKED", LocalizedText.Get("PackPathEscape"));
        return candidate;
    }

    public static void EnsureNoReparsePoints(string root, string target)
    {
        var fullRoot = Path.GetFullPath(root);
        var fullTarget = Path.GetFullPath(target);
        var volumeRoot = Path.GetPathRoot(fullRoot)!;
        var current = volumeRoot;
        Check(current);
        foreach (var part in fullRoot[volumeRoot.Length..].Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (part.Length == 0) continue;
            current = Path.Combine(current, part);
            Check(current);
        }
        var relative = Path.GetRelativePath(fullRoot, fullTarget);
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative))
            throw new InstallerException("PATH_BLOCKED", LocalizedText.Get("PackPathEscape"));
        current = fullRoot;
        foreach (var part in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (part is "" or ".") continue;
            current = Path.Combine(current, part);
            Check(current);
        }
    }

    private static void Check(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InstallerException("PATH_REPARSE_BLOCKED", LocalizedText.Get("DirectoryReparseBlocked"));
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }

    private static bool IsDeviceName(string segment)
    {
        var name = segment.Split('.')[0];
        return name.Equals("CON", StringComparison.OrdinalIgnoreCase) || name.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("AUX", StringComparison.OrdinalIgnoreCase) || name.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
               (name.Length == 4 && (name.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && name[3] is >= '1' and <= '9');
    }
}
