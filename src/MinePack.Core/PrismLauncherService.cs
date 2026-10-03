using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MinePack.Core;

internal sealed record PrismMetadataChange(string Path, byte[]? OriginalBytes, byte[] ReplacementBytes);

public static class PrismLauncherService
{
    public const string OwnershipMarkerName = ".minepack-prism.json";
    public const string UninstalledConfigName = ".minepack-uninstalled-instance.cfg";
    public const string UninstalledComponentManifestName = ".minepack-uninstalled-mmc-pack.json";
    private const int MaxOwnershipBytes = 16 * 1024;
    internal const int MaxMetadataBytes = 256 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, MaxDepth = 16 };

    internal static void RecheckTarget(InstallationLayout layout)
    {
        if (!layout.IsPrism) throw new InstallerException("ROOT_UNSAFE", LocalizedText.Get("UnsafePath"));
        layout.Validate();
        var target = new PrismLauncherTarget(layout.LauncherIdentity, layout.PrismDataRoot!, layout.InstancesRoot,
            layout.Fingerprint, "verified-layout");
        LauncherDiscovery.RevalidatePrismTarget(target);
        new PrismLauncherController().EnsureClosed(target);
    }

    public static void PrepareFreshInstance(string stagingWrapper, InstallationLayout finalLayout, PackArchive pack)
    {
        ArgumentNullException.ThrowIfNull(finalLayout);
        ArgumentNullException.ThrowIfNull(pack);
        if (!finalLayout.IsPrism || !finalLayout.Fingerprint.Equals(
                InstallationLayout.ComputePrismFingerprint(finalLayout.PrismDataRoot!, finalLayout.InstancesRoot),
                StringComparison.OrdinalIgnoreCase))
            throw new InstallerException("PRISM_IDENTITY_CHANGED", LocalizedText.Get("PrismIdentityChanged"));

        var wrapper = Path.TrimEndingDirectorySeparator(Path.GetFullPath(stagingWrapper));
        var instanceName = Path.GetFileName(finalLayout.InstanceDirectory);
        Directory.CreateDirectory(wrapper);
        SafePath.EnsureNoReparsePoints(wrapper, wrapper);
        WriteNew(Path.Combine(wrapper, "instance.cfg"), BuildInstanceConfig(pack), wrapper);
        WriteNew(Path.Combine(wrapper, "mmc-pack.json"), BuildComponentManifest(pack), wrapper);
        WriteNew(Path.Combine(wrapper, OwnershipMarkerName), BuildOwnershipRecord(finalLayout, pack, instanceName), wrapper);
    }

    public static bool IsOwned(InstallationLayout layout, PackArchive pack, InstallationManifest manifest)
    {
        if (!layout.IsPrism) return false;
        InstallService.ValidateMatchesRelease(manifest, pack);
        if (!HasOwnedBinding(layout)) return false;
        var expectedBytes = BuildOwnershipRecord(layout, pack, Path.GetFileName(layout.InstanceDirectory));
        var wrapperMarker = SafePath.Resolve(layout.InstanceDirectory, OwnershipMarkerName);
        var localRecord = OwnershipRecordPath(layout);
        SafePath.EnsureNoReparsePoints(layout.InstancesRoot, wrapperMarker);
        SafePath.EnsureNoReparsePoints(layout.StateRoot, localRecord);
        return HasExactBytes(wrapperMarker, expectedBytes) && HasExactBytes(localRecord, expectedBytes);
    }

    internal static IReadOnlyList<PrismMetadataChange> CreateRepairMetadata(InstallationLayout layout, PackArchive pack)
    {
        if (!layout.IsPrism || !HasOwnedBinding(layout))
            throw new InstallerException("PRISM_INSTANCE_OWNERSHIP_CONFLICT", LocalizedText.Get("PrismIdentityChanged"));
        layout.Validate();
        var result = new List<PrismMetadataChange>();
        foreach (var relative in new[] { "instance.cfg", "mmc-pack.json" })
        {
            var path = SafePath.Resolve(layout.InstanceDirectory, relative);
            SafePath.EnsureNoReparsePoints(layout.InstanceDirectory, path);
            var original = ReadOptionalMetadata(path, allowEmpty: false);
            var repaired = original is null
                ? relative.Equals("instance.cfg", StringComparison.OrdinalIgnoreCase)
                    ? BuildInstanceConfig(pack)
                    : BuildComponentManifest(pack)
                : relative.Equals("instance.cfg", StringComparison.OrdinalIgnoreCase)
                    ? RepairInstanceConfig(original, pack.MinecraftVersion)
                    : RepairComponentManifest(original, pack);
            if (original is null || !repaired.AsSpan().SequenceEqual(original))
                result.Add(new PrismMetadataChange(relative, original, repaired));
        }
        return result;
    }

    internal static bool IsExpectedInstanceDirectory(InstallationLayout layout, KnownPackRelease release)
    {
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(layout.InstanceDirectory));
        if (!Path.GetDirectoryName(path)!.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(layout.InstancesRoot)),
                StringComparison.OrdinalIgnoreCase)) return false;
        return InstanceDirectoryNaming.IsExpected(Path.GetFileName(path), release);
    }

    public static bool HasOwnedBinding(InstallationLayout layout)
    {
        if (!layout.IsPrism) return false;
        try
        {
            layout.Validate();
            if (!Directory.Exists(layout.InstanceDirectory) || !Directory.Exists(layout.GameDirectory)) return false;
            var wrapperMarker = SafePath.Resolve(layout.InstanceDirectory, OwnershipMarkerName);
            var localRecord = OwnershipRecordPath(layout);
            SafePath.EnsureNoReparsePoints(layout.InstancesRoot, wrapperMarker);
            SafePath.EnsureNoReparsePoints(layout.StateRoot, localRecord);
            var wrapperBytes = ReadBounded(wrapperMarker);
            var localBytes = ReadBounded(localRecord);
            if (!wrapperBytes.AsSpan().SequenceEqual(localBytes)) return false;
            using var document = JsonDocument.Parse(wrapperBytes, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            var expectedFields = new HashSet<string>(StringComparer.Ordinal)
            {
                "SchemaVersion", "LauncherKind", "InstanceDirectoryName", "LayoutFingerprint",
                "PackVersion", "PackArchiveSha512", "MinecraftVersion", "FabricLoaderVersion"
            };
            if (root.EnumerateObject().Count() != expectedFields.Count ||
                root.EnumerateObject().Any(property => !expectedFields.Remove(property.Name)) || expectedFields.Count != 0)
                return false;
            var record = JsonSerializer.Deserialize<OwnershipRecord>(wrapperBytes, JsonOptions);
            if (record is null || record.SchemaVersion != 1 || record.LauncherKind != "Prism" ||
                record.InstanceDirectoryName != Path.GetFileName(layout.InstanceDirectory) ||
                !string.Equals(record.LayoutFingerprint, layout.Fingerprint, StringComparison.OrdinalIgnoreCase) ||
                !PackArchive.IsSha512(record.PackArchiveSha512) ||
                !layout.TryGetPinnedRelease(record.PackVersion, out var release) ||
                !record.PackArchiveSha512.Equals(release.ArchiveSha512, StringComparison.OrdinalIgnoreCase) ||
                record.MinecraftVersion != release.MinecraftVersion || record.FabricLoaderVersion != release.FabricLoaderVersion ||
                !IsExpectedInstanceDirectory(layout, release))
                return false;

            var manifest = InstallationManifest.Load(layout.GameDirectory);
            return manifest.PackVersion == release.PackVersion &&
                   manifest.PackArchiveSha512.Equals(release.ArchiveSha512, StringComparison.OrdinalIgnoreCase) &&
                   manifest.MinecraftVersion == release.MinecraftVersion &&
                   manifest.FabricLoaderVersion == release.FabricLoaderVersion;
        }
        catch (Exception ex) when (ex is InstallerException or IOException or UnauthorizedAccessException or JsonException or
                                       KeyNotFoundException or InvalidOperationException or FormatException or ArgumentException or NotSupportedException)
        { return false; }
    }

    internal static IReadOnlyList<InstallationLayout> FindBoundInstances(InstallationLayout selectedLayout)
    {
        if (!selectedLayout.IsPrism) return [];
        selectedLayout.Validate();
        var ownershipRoot = Path.Combine(selectedLayout.StateRoot, "ownership");
        FileAttributes ownershipAttributes;
        try { ownershipAttributes = File.GetAttributes(ownershipRoot); }
        catch (FileNotFoundException) { return []; }
        catch (DirectoryNotFoundException) { return []; }
        if ((ownershipAttributes & FileAttributes.Directory) == 0)
            throw new InstallerException("PRISM_OWNERSHIP_INVALID", LocalizedText.Get("PrismIdentityChanged"));
        SafePath.EnsureNoReparsePoints(selectedLayout.StateRoot, ownershipRoot);
        var records = Directory.EnumerateFiles(ownershipRoot, "*.json").Take(4097).ToArray();
        if (records.Length > 4096)
            throw new InstallerException("PRISM_OWNERSHIP_INVALID", LocalizedText.Get("PrismIdentityChanged"));

        var found = new List<InstallationLayout>();
        foreach (var recordPath in records)
        {
            SafePath.EnsureNoReparsePoints(ownershipRoot, recordPath);
            var bytes = ReadBounded(recordPath);
            ValidateNoDuplicateJsonProperties(bytes);
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) continue;
            var expectedFields = new HashSet<string>(StringComparer.Ordinal)
            {
                "SchemaVersion", "LauncherKind", "InstanceDirectoryName", "LayoutFingerprint",
                "PackVersion", "PackArchiveSha512", "MinecraftVersion", "FabricLoaderVersion"
            };
            if (root.EnumerateObject().Count() != expectedFields.Count ||
                root.EnumerateObject().Any(property => !expectedFields.Remove(property.Name)) || expectedFields.Count != 0)
                continue;
            var record = JsonSerializer.Deserialize<OwnershipRecord>(bytes, JsonOptions);
            if (record is null || record.SchemaVersion != 1 || record.LauncherKind != "Prism" ||
                string.IsNullOrWhiteSpace(record.InstanceDirectoryName) ||
                !Path.GetFileName(record.InstanceDirectoryName).Equals(record.InstanceDirectoryName, StringComparison.Ordinal) ||
                record.InstanceDirectoryName is "." or ".." ||
                !Path.GetFileName(recordPath).Equals(record.InstanceDirectoryName + ".json", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(record.LayoutFingerprint, selectedLayout.Fingerprint, StringComparison.OrdinalIgnoreCase) ||
                !PackArchive.IsSha512(record.PackArchiveSha512) ||
                string.IsNullOrWhiteSpace(record.PackVersion) ||
                !selectedLayout.TryGetPinnedRelease(record.PackVersion, out var release) ||
                !record.PackArchiveSha512.Equals(release.ArchiveSha512, StringComparison.OrdinalIgnoreCase) ||
                record.MinecraftVersion != release.MinecraftVersion || record.FabricLoaderVersion != release.FabricLoaderVersion)
                continue;

            var wrapper = SafePath.Resolve(selectedLayout.InstancesRoot, record.InstanceDirectoryName);
            if (!Path.GetDirectoryName(wrapper)!.Equals(selectedLayout.InstancesRoot, StringComparison.OrdinalIgnoreCase)) continue;
            FileAttributes wrapperAttributes;
            try { wrapperAttributes = File.GetAttributes(wrapper); }
            catch (FileNotFoundException) { continue; }
            catch (DirectoryNotFoundException) { continue; }
            if ((wrapperAttributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != FileAttributes.Directory) continue;
            SafePath.EnsureNoReparsePoints(selectedLayout.InstancesRoot, wrapper);
            var wrapperMarker = SafePath.Resolve(wrapper, OwnershipMarkerName);
            SafePath.EnsureNoReparsePoints(wrapper, wrapperMarker);
            if (!ReadBounded(wrapperMarker).AsSpan().SequenceEqual(bytes)) continue;
            var candidate = selectedLayout.ForInstance(wrapper);
            if (!IsExpectedInstanceDirectory(candidate, release)) continue;
            candidate.Validate();
            found.Add(candidate);
        }
        return found;
    }

    internal static bool TryGetOwnedResidueRelease(InstallationLayout layout, out KnownPackRelease release)
    {
        release = null!;
        if (!layout.IsPrism) return false;
        try
        {
            layout.Validate();
            if (!Directory.Exists(layout.InstanceDirectory) || !Directory.Exists(layout.GameDirectory))
                return false;

            var gameManifest = SafePath.Resolve(layout.GameDirectory, InstallationManifest.FileName);
            var currentConfig = SafePath.Resolve(layout.InstanceDirectory, "instance.cfg");
            var currentComponents = SafePath.Resolve(layout.InstanceDirectory, "mmc-pack.json");
            SafePath.EnsureNoReparsePoints(layout.GameDirectory, gameManifest);
            SafePath.EnsureNoReparsePoints(layout.InstanceDirectory, currentConfig);
            SafePath.EnsureNoReparsePoints(layout.InstanceDirectory, currentComponents);
            if (PathExistsStrict(gameManifest) || PathExistsStrict(currentConfig) || PathExistsStrict(currentComponents))
                return false;

            var wrapperMarker = SafePath.Resolve(layout.InstanceDirectory, OwnershipMarkerName);
            var localRecord = OwnershipRecordPath(layout);
            var uninstalledConfig = SafePath.Resolve(layout.InstanceDirectory, UninstalledConfigName);
            var uninstalledComponents = SafePath.Resolve(layout.InstanceDirectory, UninstalledComponentManifestName);
            SafePath.EnsureNoReparsePoints(layout.InstancesRoot, wrapperMarker);
            SafePath.EnsureNoReparsePoints(layout.StateRoot, localRecord);
            SafePath.EnsureNoReparsePoints(layout.InstanceDirectory, uninstalledConfig);
            SafePath.EnsureNoReparsePoints(layout.InstanceDirectory, uninstalledComponents);
            var wrapperBytes = ReadBounded(wrapperMarker);
            if (!wrapperBytes.AsSpan().SequenceEqual(ReadBounded(localRecord))) return false;
            _ = ReadOptionalMetadata(uninstalledConfig, allowEmpty: true);
            _ = ReadOptionalMetadata(uninstalledComponents, allowEmpty: true);

            using var document = JsonDocument.Parse(wrapperBytes, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            var expectedFields = new HashSet<string>(StringComparer.Ordinal)
            {
                "SchemaVersion", "LauncherKind", "InstanceDirectoryName", "LayoutFingerprint",
                "PackVersion", "PackArchiveSha512", "MinecraftVersion", "FabricLoaderVersion"
            };
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != expectedFields.Count ||
                root.EnumerateObject().Any(property => !expectedFields.Remove(property.Name)) || expectedFields.Count != 0)
                return false;
            var record = JsonSerializer.Deserialize<OwnershipRecord>(wrapperBytes, JsonOptions);
            if (record is null || record.SchemaVersion != 1 || record.LauncherKind != "Prism" ||
                record.InstanceDirectoryName != Path.GetFileName(layout.InstanceDirectory) ||
                !string.Equals(record.LayoutFingerprint, layout.Fingerprint, StringComparison.OrdinalIgnoreCase) ||
                !layout.TryGetPinnedRelease(record.PackVersion, out release) ||
                !string.Equals(record.PackArchiveSha512, release.ArchiveSha512, StringComparison.OrdinalIgnoreCase) ||
                record.MinecraftVersion != release.MinecraftVersion || record.FabricLoaderVersion != release.FabricLoaderVersion ||
                !IsExpectedInstanceDirectory(layout, release))
            {
                release = null!;
                return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is InstallerException or IOException or UnauthorizedAccessException or JsonException or
                                       KeyNotFoundException or InvalidOperationException or FormatException or ArgumentException or NotSupportedException)
        {
            release = null!;
            return false;
        }
    }

    public static void WriteLocalOwnershipRecord(InstallationLayout layout, PackArchive pack)
    {
        if (!layout.IsPrism) throw new InstallerException("ROOT_UNSAFE", LocalizedText.Get("UnsafePath"));
        layout.Validate();
        var directory = Path.Combine(layout.StateRoot, "ownership");
        SafePath.EnsureNoReparsePoints(layout.StateRoot, directory);
        Directory.CreateDirectory(directory);
        SafePath.EnsureNoReparsePoints(layout.StateRoot, directory);
        var path = OwnershipRecordPath(layout);
        var bytes = BuildOwnershipRecord(layout, pack, Path.GetFileName(layout.InstanceDirectory));
        if (File.Exists(path))
        {
            if (HasExactBytes(path, bytes)) return;
            throw new InstallerException("PRISM_INSTANCE_OWNERSHIP_CONFLICT", LocalizedText.Get("PrismIdentityChanged"));
        }
        WriteNew(path, bytes, layout.StateRoot);
    }

    public static void RemoveLocalOwnershipRecord(InstallationLayout layout, PackArchive pack)
    {
        var path = OwnershipRecordPath(layout);
        var expected = BuildOwnershipRecord(layout, pack, Path.GetFileName(layout.InstanceDirectory));
        SafePath.EnsureNoReparsePoints(layout.StateRoot, path);
        if (!File.Exists(path)) return;
        if (!HasExactBytes(path, expected))
            throw new InstallerException("PRISM_INSTANCE_OWNERSHIP_CONFLICT", LocalizedText.Get("PrismIdentityChanged"));
        File.Delete(path);
    }

    internal static byte[] BuildInstanceConfig(PackArchive pack)
    {
        var profileName = LauncherProfile.ProfileName(pack.MinecraftVersion);
        var content = "[General]\r\n" +
                      "ConfigVersion=1.2\r\n" +
                      "InstanceType=OneSix\r\n" +
                      $"name={profileName}\r\n" +
                      $"notes=MinePack Installer; pack {pack.VersionId}\r\n" +
                      "iconKey=default\r\n";
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(content);
    }

    private static byte[] ReadMetadata(string path)
        => ReadOptionalMetadata(path, allowEmpty: false)
           ?? throw new InstallerException("PRISM_INSTANCE_OWNERSHIP_CONFLICT", LocalizedText.Get("PrismIdentityChanged"));

    internal static byte[]? ReadOptionalMetadata(string path, bool allowEmpty)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaxMetadataBytes || (!allowEmpty && stream.Length == 0))
                throw new InstallerException("PRISM_INSTANCE_OWNERSHIP_CONFLICT", LocalizedText.Get("PrismIdentityChanged"));
            var bytes = new byte[(int)stream.Length];
            stream.ReadExactly(bytes);
            return bytes;
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    private static byte[] RepairInstanceConfig(byte[] bytes, string minecraftVersion)
    {
        string text;
        try { text = new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException ex)
        { throw new InstallerException("PRISM_INSTANCE_OWNERSHIP_CONFLICT", LocalizedText.Get("PrismIdentityChanged"), ex); }
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = text.Split('\n');
        var inGeneral = false;
        var generalCount = 0;
        var nameIndex = -1;
        var instanceTypeSeen = false;
        var expectedName = LauncherProfile.ProfileName(minecraftVersion);
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var trimmed = line.Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                inGeneral = trimmed.Equals("[General]", StringComparison.OrdinalIgnoreCase);
                if (inGeneral) generalCount++;
                continue;
            }
            if (!inGeneral || trimmed.Length == 0 || trimmed.StartsWith('#') || trimmed.StartsWith(';')) continue;
            var separator = line.IndexOf('=');
            if (separator < 0) continue;
            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (key.Equals("InstanceType", StringComparison.OrdinalIgnoreCase))
            {
                if (instanceTypeSeen || !value.Equals("OneSix", StringComparison.Ordinal))
                    throw new InstallerException("PRISM_INSTANCE_OWNERSHIP_CONFLICT", LocalizedText.Get("PrismIdentityChanged"));
                instanceTypeSeen = true;
            }
            else if (key.Equals("name", StringComparison.OrdinalIgnoreCase))
            {
                if (nameIndex >= 0)
                    throw new InstallerException("PRISM_INSTANCE_OWNERSHIP_CONFLICT", LocalizedText.Get("PrismIdentityChanged"));
                nameIndex = i;
                lines[i] = line[..(separator + 1)] + expectedName + (line.EndsWith('\r') ? "\r" : string.Empty);
            }
        }
        if (generalCount != 1 || !instanceTypeSeen)
            throw new InstallerException("PRISM_INSTANCE_OWNERSHIP_CONFLICT", LocalizedText.Get("PrismIdentityChanged"));
        if (nameIndex < 0)
        {
            var generalIndex = Array.FindIndex(lines, line => line.TrimEnd('\r').Trim().Equals("[General]", StringComparison.OrdinalIgnoreCase));
            var insertionIndex = generalIndex + 1;
            while (insertionIndex < lines.Length &&
                   !lines[insertionIndex].TrimEnd('\r').Trim().StartsWith('[')) insertionIndex++;
            Array.Resize(ref lines, lines.Length + 1);
            Array.Copy(lines, insertionIndex, lines, insertionIndex + 1, lines.Length - insertionIndex - 1);
            lines[insertionIndex] = "name=" + expectedName;
        }
        var hadFinalNewline = text.EndsWith('\n');
        var normalized = string.Join(newline, lines.Select(line => line.TrimEnd('\r')));
        if (!hadFinalNewline) normalized = normalized.TrimEnd('\r', '\n');
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(normalized);
    }

    private static byte[] RepairComponentManifest(byte[] bytes, PackArchive pack)
    {
        JsonNode? node;
        try { node = JsonNode.Parse(bytes, documentOptions: new JsonDocumentOptions { MaxDepth = 16 }); }
        catch (JsonException ex)
        { throw new InstallerException("PRISM_INSTANCE_OWNERSHIP_CONFLICT", LocalizedText.Get("PrismIdentityChanged"), ex); }
        if (node is not JsonObject root || root["formatVersion"]?.GetValue<int>() != 1 || root["components"] is not JsonArray components)
            throw new InstallerException("PRISM_INSTANCE_OWNERSHIP_CONFLICT", LocalizedText.Get("PrismIdentityChanged"));
        ValidateNoDuplicateJsonProperties(bytes);
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["net.minecraft"] = pack.MinecraftVersion,
            ["net.fabricmc.fabric-loader"] = pack.FabricLoaderVersion
        };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in components)
        {
            if (item is not JsonObject component || component["uid"]?.GetValue<string>() is not { } uid || !expected.TryGetValue(uid, out var version))
                continue;
            if (!seen.Add(uid) || !string.Equals(component["version"]?.GetValue<string>(), version, StringComparison.Ordinal))
                throw new InstallerException("PRISM_INSTANCE_OWNERSHIP_CONFLICT", LocalizedText.Get("PrismIdentityChanged"));
            if (component["cachedVersion"] is not JsonValue cached || !string.Equals(cached.GetValue<string>(), version, StringComparison.Ordinal))
                component["cachedVersion"] = version;
        }
        if (seen.Count != expected.Count)
            throw new InstallerException("PRISM_INSTANCE_OWNERSHIP_CONFLICT", LocalizedText.Get("PrismIdentityChanged"));
        return JsonSerializer.SerializeToUtf8Bytes(node, JsonOptions);
    }

    private static void ValidateNoDuplicateJsonProperties(ReadOnlySpan<byte> bytes)
    {
        using var document = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions { MaxDepth = 16 });
        var objects = new Stack<JsonElement>();
        objects.Push(document.RootElement);
        while (objects.TryPop(out var element))
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name))
                        throw new InstallerException("PRISM_INSTANCE_OWNERSHIP_CONFLICT", LocalizedText.Get("PrismIdentityChanged"));
                    objects.Push(property.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
                foreach (var item in element.EnumerateArray()) objects.Push(item);
        }
    }

    internal static byte[] BuildComponentManifest(PackArchive pack)
    {
        var json = new
        {
            formatVersion = 1,
            components = new object[]
            {
                new { cachedName = "Minecraft", cachedVersion = pack.MinecraftVersion, version = pack.MinecraftVersion, important = true, uid = "net.minecraft" },
                new { cachedName = "Fabric Loader", cachedVersion = pack.FabricLoaderVersion, version = pack.FabricLoaderVersion, uid = "net.fabricmc.fabric-loader" }
            }
        };
        return JsonSerializer.SerializeToUtf8Bytes(json, JsonOptions);
    }

    private static byte[] BuildOwnershipRecord(InstallationLayout layout, PackArchive pack, string instanceName)
    {
        var record = new OwnershipRecord(1, "Prism", instanceName, layout.Fingerprint, pack.VersionId,
            pack.ArchiveSha512, pack.MinecraftVersion, pack.FabricLoaderVersion);
        return JsonSerializer.SerializeToUtf8Bytes(record, JsonOptions);
    }

    private static string OwnershipRecordPath(InstallationLayout layout)
    {
        var instanceName = Path.GetFileName(layout.InstanceDirectory);
        return SafePath.Resolve(layout.StateRoot, "ownership/" + instanceName + ".json");
    }

    private static bool PathExistsStrict(string path)
    {
        try { _ = File.GetAttributes(path); return true; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    private static bool HasExactBytes(string path, byte[] expected)
    {
        try
        {
            var bytes = ReadBounded(path);
            return bytes.AsSpan().SequenceEqual(expected);
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    private static byte[] ReadBounded(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is <= 0 or > MaxOwnershipBytes) throw new IOException("Prism ownership record size is invalid.");
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static void WriteNew(string path, byte[] contents, string root)
    {
        SafePath.EnsureNoReparsePoints(root, path);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(contents);
        stream.Flush(flushToDisk: true);
    }

    private sealed record OwnershipRecord(int SchemaVersion, string LauncherKind, string InstanceDirectoryName,
        string LayoutFingerprint, string PackVersion, string PackArchiveSha512, string MinecraftVersion,
        string FabricLoaderVersion);
}
