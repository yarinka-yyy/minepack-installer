using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MinePack.Core;

internal sealed record LauncherProfileRemovalReceipt(
    string ProfileFileName,
    string BackupFileName,
    string LauncherRootSha512,
    string OriginalSha512,
    string CandidateSha512,
    bool ChangesProfile)
{
    [JsonIgnore]
    public byte[] OriginalBytes { get; init; } = [];
    [JsonIgnore]
    public byte[] CandidateBytes { get; init; } = [];
}

internal sealed class ManagedFileTransaction
{
    private const string DirectorySuffix = ".minepack-transaction";
    private const string JournalName = "journal.json";
    private const int MaxJournalBytes = 256 * 1024;
    private const int MaxEntries = 2048;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, MaxDepth = 32 };
    private static readonly AsyncLocal<Action<string>?> TestCheckpoint = new();
    private readonly string _instancePath;
    private readonly string _ownerDirectory;
    private readonly string _installRoot;
    private readonly string _folder;
    private readonly string _journalPath;
    private readonly Journal _journal;
    private readonly OperationLog? _operationLog;
    private readonly InstallationLayout? _layout;

    private ManagedFileTransaction(string installRoot, string instancePath, string ownerDirectory, string folder,
        Journal journal, OperationLog? operationLog = null, InstallationLayout? layout = null)
    {
        _installRoot = Path.GetFullPath(installRoot);
        _instancePath = Path.GetFullPath(instancePath);
        _ownerDirectory = Path.GetFullPath(ownerDirectory);
        _folder = Path.GetFullPath(folder);
        _journalPath = Path.Combine(_folder, JournalName);
        _journal = journal;
        _operationLog = operationLog;
        _layout = layout;
    }

    public string Id => _journal.TransactionId;
    public string InstancePath => _instancePath;
    public string PackVersion => _journal.PackVersion;
    public string Folder => _folder;
    public string StagingRoot => Path.Combine(_folder, "staged");
    public string Phase => _journal.Phase;
    public LauncherProfileRemovalReceipt? ProfileReceipt => _journal.ProfileReceipt?.ToReceipt();

    public static IDisposable UseCheckpointsForTesting(Action<string> callback)
    {
        var previous = TestCheckpoint.Value;
        TestCheckpoint.Value = callback;
        return new CallbackScope(() => TestCheckpoint.Value = previous);
    }

    internal static void Checkpoint(string name) => TestCheckpoint.Value?.Invoke(name);

    public static ManagedFileTransaction BeginRepair(string installRoot, string instancePath, PackArchive pack,
        OperationLog? operationLog = null) => Begin(installRoot, instancePath, pack, "repair", operationLog);

    public static ManagedFileTransaction BeginRepair(InstallationLayout layout, PackArchive pack,
        OperationLog? operationLog = null) => Begin(layout, pack, "repair", operationLog);

    public static ManagedFileTransaction BeginUninstall(string installRoot, string instancePath, PackArchive pack,
        OperationLog? operationLog = null) => Begin(installRoot, instancePath, pack, "uninstall", operationLog);

    public static ManagedFileTransaction BeginUninstall(InstallationLayout layout, PackArchive pack,
        OperationLog? operationLog = null) => Begin(layout, pack, "uninstall", operationLog);

    private static ManagedFileTransaction Begin(string installRoot, string instancePath, PackArchive pack, string operation,
        OperationLog? operationLog)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installRoot));
        var fullInstance = Path.TrimEndingDirectorySeparator(Path.GetFullPath(instancePath));
        return BeginCore(fullRoot, fullInstance, fullInstance, pack, operation, operationLog, null);
    }

    private static ManagedFileTransaction Begin(InstallationLayout layout, PackArchive pack, string operation,
        OperationLog? operationLog)
    {
        ArgumentNullException.ThrowIfNull(layout);
        layout.Validate();
        return BeginCore(layout.StateRoot, layout.GameDirectory, layout.InstanceDirectory, pack, operation, operationLog, layout);
    }

    private static ManagedFileTransaction BeginCore(string fullRoot, string fullInstance, string ownerDirectory,
        PackArchive pack, string operation, OperationLog? operationLog, InstallationLayout? layout)
    {
        if (layout is null) EnsureNoPendingForInstance(fullInstance);
        else EnsureNoPendingForInstance(layout);
        fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(fullRoot));
        fullInstance = Path.TrimEndingDirectorySeparator(Path.GetFullPath(fullInstance));
        ownerDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ownerDirectory));
        var parent = Path.GetDirectoryName(ownerDirectory)
            ?? throw RecoveryRequired(TransactionDirectoryFor(fullInstance));
        var expectedInstances = layout?.InstancesRoot ?? Path.Combine(fullRoot, "instances");
        if (!parent.Equals(expectedInstances, StringComparison.OrdinalIgnoreCase) ||
            (layout is null && !ownerDirectory.Equals(fullInstance, StringComparison.OrdinalIgnoreCase)) ||
            (layout is not null && (!layout.InstanceDirectory.Equals(ownerDirectory, StringComparison.OrdinalIgnoreCase) ||
                                    !layout.GameDirectory.Equals(fullInstance, StringComparison.OrdinalIgnoreCase) ||
                                    !layout.StateRoot.Equals(fullRoot, StringComparison.OrdinalIgnoreCase))))
            throw new InstallerException("ROOT_UNSAFE", LocalizedText.Get("UnsafePath"));
        if (!Directory.Exists(parent) || !Directory.Exists(ownerDirectory) || !Directory.Exists(fullInstance))
            throw new InstallerException("INSTANCE_NOT_FOUND", LocalizedText.Get("UninstallInstanceNotFound"));
        SafePath.EnsureNoReparsePoints(parent, fullInstance);
        var folder = TransactionDirectoryFor(ownerDirectory);
        SafePath.EnsureNoReparsePoints(parent, folder);
        if (Directory.Exists(folder) || File.Exists(folder)) throw RecoveryRequired(folder);
        if (!Path.GetPathRoot(parent)!.Equals(Path.GetPathRoot(fullInstance), StringComparison.OrdinalIgnoreCase))
            throw new InstallerException("TRANSACTION_VOLUME", LocalizedText.Get("TransactionVolumeUnsupported"));

        EnsureAvailableSpace(parent, MaxJournalBytes);
        Directory.CreateDirectory(folder);
        if (Directory.EnumerateFileSystemEntries(folder).Any()) throw RecoveryRequired(folder);
        try
        {
            Directory.CreateDirectory(Path.Combine(folder, "staged"));
            Directory.CreateDirectory(Path.Combine(folder, "backup"));
            var journal = new Journal
            {
                SchemaVersion = layout?.IsPrism == true ? 2 : 1,
                LauncherKind = layout?.IsPrism == true ? "Prism" : null,
                LayoutFingerprint = layout?.IsPrism == true ? layout.Fingerprint : null,
                TransactionId = Guid.NewGuid().ToString("N"),
                InstanceDirectoryName = Path.GetFileName(ownerDirectory),
                PackVersion = pack.VersionId,
                PackArchiveSha512 = pack.ArchiveSha512,
                Operation = operation,
                Phase = "Preparing"
            };
            var transaction = new ManagedFileTransaction(fullRoot, fullInstance, ownerDirectory, folder, journal, operationLog, layout);
            transaction.WriteJournal();
            transaction.Log("prepare", "started", "transaction_prepare_started", new { transactionId = journal.TransactionId });
            return transaction;
        }
        catch
        {
            TryRemoveInitialFolder(folder);
            throw;
        }
    }

    public static ManagedFileTransaction? OpenPending(string instancePath, OperationLog? operationLog = null)
    {
        var fullInstance = Path.TrimEndingDirectorySeparator(Path.GetFullPath(instancePath));
        var parent = Path.GetDirectoryName(fullInstance)!;
        var fullRoot = Path.GetDirectoryName(parent)!;
        return OpenPendingCore(fullRoot, fullInstance, fullInstance, null, operationLog);
    }

    public static ManagedFileTransaction? OpenPending(InstallationLayout layout, OperationLog? operationLog = null)
    {
        ArgumentNullException.ThrowIfNull(layout);
        layout.Validate();
        return OpenPendingCore(layout.StateRoot, layout.GameDirectory, layout.InstanceDirectory, layout, operationLog);
    }

    private static ManagedFileTransaction? OpenPendingCore(string stateRoot, string gameDirectory,
        string ownerDirectory, InstallationLayout? layout, OperationLog? operationLog)
    {
        var fullInstance = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameDirectory));
        var fullOwner = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ownerDirectory));
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(stateRoot));
        var folder = TransactionDirectoryFor(fullOwner);
        if (!Directory.Exists(folder) && !File.Exists(folder)) return null;
        var parent = Path.GetDirectoryName(fullOwner)!;
        SafePath.EnsureNoReparsePoints(parent, folder);
        var journalPath = Path.Combine(folder, JournalName);
        SafePath.EnsureNoReparsePoints(folder, journalPath);
        if (!Directory.Exists(folder) || !File.Exists(journalPath)) throw RecoveryRequired(folder);
        Journal journal;
        try
        {
            using var stream = new FileStream(journalPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length is <= 0 or > MaxJournalBytes) throw RecoveryRequired(folder);
            var bytes = new byte[(int)stream.Length];
            stream.ReadExactly(bytes);
            ValidateJournalShape(bytes, folder);
            journal = JsonSerializer.Deserialize<Journal>(bytes, JsonOptions) ?? throw RecoveryRequired(folder);
        }
        catch (InstallerException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            throw RecoveryRequired(folder, ex);
        }

        var transaction = new ManagedFileTransaction(fullRoot, fullInstance, fullOwner, folder, journal, operationLog, layout);
        transaction.ValidateBasic();
        return transaction;
    }

    public static void EnsureNoPendingForInstance(string instancePath)
    {
        var pending = OpenPending(instancePath);
        if (pending is not null) throw RecoveryRequired(pending.Folder);
    }

    public static void EnsureNoPendingForInstance(InstallationLayout layout)
    {
        var pending = OpenPending(layout);
        if (pending is not null) throw RecoveryRequired(pending.Folder);
    }

    public static void EnsureNoPendingForOtherOwnedInstances(InstallationLayout selectedLayout)
    {
        foreach (var ownedLayout in PrismLauncherService.FindBoundInstances(selectedLayout))
        {
            if (ownedLayout.InstanceDirectory.Equals(selectedLayout.InstanceDirectory, StringComparison.OrdinalIgnoreCase)) continue;
            var folder = TransactionDirectoryFor(ownedLayout.InstanceDirectory);
            FileAttributes attributes;
            try { attributes = File.GetAttributes(folder); }
            catch (FileNotFoundException) { continue; }
            catch (DirectoryNotFoundException) { continue; }
            SafePath.EnsureNoReparsePoints(ownedLayout.InstancesRoot, folder);
            if ((attributes & FileAttributes.Directory) == 0) throw RecoveryRequired(folder);
            var pending = OpenPending(ownedLayout);
            throw RecoveryRequired(pending?.Folder ?? folder);
        }
    }

    public static void EnsureNoPendingUnderRoot(string installRoot, string? exceptInstancePath = null)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installRoot));
        var instances = Path.Combine(root, "instances");
        if (!Directory.Exists(instances)) return;
        SafePath.EnsureNoReparsePoints(root, instances);
        foreach (var instance in Directory.EnumerateDirectories(instances))
        {
            if ((File.GetAttributes(instance) & FileAttributes.ReparsePoint) != 0)
                throw new InstallerException("ROOT_UNSAFE", LocalizedText.Get("UnsafePath"));
            var candidate = instance.EndsWith(DirectorySuffix, StringComparison.OrdinalIgnoreCase) &&
                           !Directory.Exists(instance[..^DirectorySuffix.Length])
                ? instance[..^DirectorySuffix.Length]
                : instance;
            var pending = OpenPending(candidate);
            if (pending is null) continue;
            if (exceptInstancePath is not null &&
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(exceptInstancePath))
                    .Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(instance)), StringComparison.OrdinalIgnoreCase))
                continue;
            throw RecoveryRequired(pending.Folder);
        }
    }

    public static string? FindPendingPathUnderRoot(string installRoot)
        => FindPendingUnderRoot(installRoot)?.Folder;

    public static ManagedFileTransaction? FindPendingUnderRoot(string installRoot)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installRoot));
        var instances = Path.Combine(root, "instances");
        if (!Directory.Exists(instances)) return null;
        SafePath.EnsureNoReparsePoints(root, instances);
        foreach (var instance in Directory.EnumerateDirectories(instances))
        {
            if ((File.GetAttributes(instance) & FileAttributes.ReparsePoint) != 0) continue;
            var candidate = instance.EndsWith(DirectorySuffix, StringComparison.OrdinalIgnoreCase) &&
                           !Directory.Exists(instance[..^DirectorySuffix.Length])
                ? instance[..^DirectorySuffix.Length]
                : instance;
            var pending = OpenPending(candidate);
            if (pending is null) continue;
            return pending;
        }
        return null;
    }

    public InstallationManifest LoadTrustedManifest(PackArchive pack)
    {
        ValidateIdentity(pack);
        var currentPath = SafePath.Resolve(_instancePath, InstallationManifest.FileName);
        SafePath.EnsureNoReparsePoints(_instancePath, currentPath);
        if (_journal.Operation == "uninstall" && _journal.ManifestSha512 is { } manifestHash)
        {
            var snapshotDirectory = Path.Combine(_folder, "backup", "manifest-snapshot");
            var snapshotPath = Path.Combine(snapshotDirectory, InstallationManifest.FileName);
            SafePath.EnsureNoReparsePoints(_folder, snapshotPath);
            if (!File.Exists(snapshotPath) || !HashEquals(HashFile(snapshotPath), manifestHash))
                throw RecoveryRequired(_folder);
            if (File.Exists(currentPath) && !HashEquals(HashFile(currentPath), manifestHash))
                throw RecoveryRequired(_folder);
            var manifest = InstallationManifest.Load(snapshotDirectory);
            InstallService.ValidateMatchesRelease(manifest, pack);
            return manifest;
        }
        if (!File.Exists(currentPath)) throw RecoveryRequired(_folder);
        var current = InstallationManifest.Load(_instancePath);
        InstallService.ValidateMatchesRelease(current, pack);
        return current;
    }

    public async Task ValidateEntriesAsync(PackArchive pack, InstallationManifest manifest)
    {
        ValidateIdentity(pack);
        InstallService.ValidateMatchesRelease(manifest, pack);
        if (_journal.Files.Count > MaxEntries) throw RecoveryRequired(_folder);
        var managed = manifest.Files
            .Where(file => !InitialConfiguration.IsInitialUserConfig(pack, file.Path))
            .ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        IReadOnlyDictionary<string, string> defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (_journal.Operation == "repair") defaults = await TrustedInitialHashesAsync(pack);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _journal.Files)
        {
            string path;
            try { path = SafePath.ValidateRelative(entry.Path); }
            catch (InstallerException ex) { throw RecoveryRequired(_folder, ex); }
            if (!seen.Add(path) || !path.Equals(entry.Path, StringComparison.Ordinal) ||
                entry.State is not ("Planned" or "OriginalMoveIntent" or "OriginalMoved" or "ReplacementMoveIntent" or "ReplacementMoved" or "Removed" or "SkippedExisting"))
                throw RecoveryRequired(_folder);
            if (entry.OriginalSha512 is not null && !PackArchive.IsSha512(entry.OriginalSha512)) throw RecoveryRequired(_folder);
            if (entry.ReplacementSha512 is not null && !PackArchive.IsSha512(entry.ReplacementSha512)) throw RecoveryRequired(_folder);
            if (entry.OriginalSize < 0) throw RecoveryRequired(_folder);
            if (_journal.Operation == "repair")
            {
                if (managed.TryGetValue(path, out var expectedManaged))
                {
                    if (entry.CreateOnly || entry.ReplacementSha512 is null || !HashEquals(entry.ReplacementSha512, expectedManaged.Sha512))
                        throw RecoveryRequired(_folder);
                }
                else if (defaults.TryGetValue(path, out var expectedDefault))
                {
                    if (entry.OriginalSha512 is not null || !entry.CreateOnly || entry.ReplacementSha512 is null ||
                        !HashEquals(entry.ReplacementSha512, expectedDefault))
                        throw RecoveryRequired(_folder);
                }
                else throw RecoveryRequired(_folder);
            }
            else if (_journal.Operation == "uninstall")
            {
                if (!managed.ContainsKey(path) || entry.ReplacementSha512 is not null || entry.CreateOnly) throw RecoveryRequired(_folder);
            }
            else throw RecoveryRequired(_folder);
            if (entry.State == "SkippedExisting" && (!entry.CreateOnly || entry.OriginalSha512 is not null))
                throw RecoveryRequired(_folder);
        }
        if (_journal.Phase == "RolledBack" && _journal.RollbackFromPhase is null) throw RecoveryRequired(_folder);
        var rollbackStartedAfterPrepare = _journal.RollbackFromPhase is
            "Prepared" or "Committing" or "FilesCommitted" or "ProfileCommitted" or "Finalizing";
        var uninstallSetPrepared = _journal.Phase is "Prepared" or "Committing" or "FilesCommitted" or "ProfileCommitted" or "Finalizing" or "Completed" ||
            (_journal.Phase is "RollingBack" or "RollbackIncomplete" or "RolledBack" && rollbackStartedAfterPrepare);
        if (_journal.Operation == "uninstall" && uninstallSetPrepared)
        {
            var expectedPaths = managed.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (_journal.Files.Count != expectedPaths.Count || !_journal.Files.All(entry => expectedPaths.Remove(entry.Path)) || expectedPaths.Count != 0)
                throw RecoveryRequired(_folder);
        }
        if (_journal.Operation == "uninstall" &&
            (_journal.Phase is "Prepared" or "Committing" or "FilesCommitted" or "ProfileCommitted" or "Finalizing" or "Completed" ||
             _journal.RollbackFromPhase is "Prepared" or "Committing" or "FilesCommitted" or "ProfileCommitted" or "Finalizing") &&
            _journal.ManifestSha512 is null)
            throw RecoveryRequired(_folder);
    }

    private async Task<IReadOnlyDictionary<string, string>> TrustedInitialHashesAsync(PackArchive pack)
    {
        var validationRoot = Path.Combine(_folder, "validation-" + Guid.NewGuid().ToString("N"));
        SafePath.EnsureNoReparsePoints(_folder, validationRoot);
        Directory.CreateDirectory(validationRoot);
        try
        {
            await pack.ExtractOverridesAsync(validationRoot, CancellationToken.None);
            var defaults = InitialConfiguration.Create(pack, validationRoot);
            InitialConfiguration.WriteToRoot(validationRoot, defaults);
            return defaults.ToDictionary(file => file.Path,
                file => HashFile(SafePath.Resolve(validationRoot, file.Path)), StringComparer.OrdinalIgnoreCase);
        }
        finally { TryDeleteTree(validationRoot); }
    }

    public void AddFile(string relativePath, string? originalSha512, long originalSize, string? replacementSha512,
        bool createOnly = false)
    {
        var path = SafePath.ValidateRelative(relativePath);
        if (_journal.Files.Any(file => file.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
            throw new InstallerException("MANIFEST_INVALID", LocalizedText.Get("ManifestDuplicateOrReservedPath"));
        if (_journal.Files.Count >= MaxEntries ||
            (originalSha512 is not null && !PackArchive.IsSha512(originalSha512)) ||
            (replacementSha512 is not null && !PackArchive.IsSha512(replacementSha512)) || originalSize < 0)
            throw RecoveryRequired(_folder);
        _journal.Files.Add(new JournalEntry
        {
            Path = path,
            OriginalSha512 = originalSha512,
            OriginalSize = originalSize,
            ReplacementSha512 = replacementSha512,
            CreateOnly = createOnly,
            State = "Planned"
        });
        WriteJournal();
    }

    public void SnapshotUninstallMetadata(string root)
    {
        if (_journal.Operation != "uninstall") throw new InvalidOperationException("Metadata snapshots are uninstall-only.");
        if (!Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)).Equals(_installRoot, StringComparison.OrdinalIgnoreCase))
            throw new InstallerException("ROOT_UNSAFE", LocalizedText.Get("UnsafePath"));
        var manifestPath = SafePath.Resolve(_instancePath, InstallationManifest.FileName);
        SafePath.EnsureNoReparsePoints(_instancePath, manifestPath);
        var manifestSnapshot = Path.Combine(_folder, "backup", "manifest-snapshot", InstallationManifest.FileName);
        EnsureAvailableSpace(_folder, checked(new FileInfo(manifestPath).Length + MaxJournalBytes));
        WriteCopyDurable(manifestPath, _instancePath, manifestSnapshot, _folder);
        _journal.ManifestSha512 = HashFile(manifestSnapshot);

        var markerPath = SafePath.Resolve(root, ".minepack-active.json");
        SafePath.EnsureNoReparsePoints(root, markerPath);
        if (File.Exists(markerPath) &&
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(GetMarkerInstancePath(root)))
                .Equals(_instancePath, StringComparison.OrdinalIgnoreCase))
        {
            var markerSnapshot = Path.Combine(_folder, "backup", "active-marker.json");
            EnsureAvailableSpace(_folder, checked(new FileInfo(markerPath).Length + MaxJournalBytes));
            WriteCopyDurable(markerPath, root, markerSnapshot, _folder);
            _journal.ActiveMarkerSha512 = HashFile(markerSnapshot);
        }
        else
        {
            _journal.ActiveMarkerSha512 = null;
        }
        WriteJournal();
    }

    public void SnapshotPrismRepairMetadata(PackArchive pack)
    {
        if (_journal.Operation != "repair" || _layout is not { IsPrism: true } layout ||
            !PrismLauncherService.HasOwnedBinding(layout))
            throw new InstallerException("PRISM_INSTANCE_OWNERSHIP_CONFLICT", LocalizedText.Get("PrismIdentityChanged"));
        var changes = PrismLauncherService.CreateRepairMetadata(layout, pack);
        if (changes.Count == 0) return;
        if (changes.Count > PrismMetadataPaths.Length) throw RecoveryRequired(_folder);

        var receipt = new PrismMetadataReceiptDocument
        {
            Operation = "repair",
            LayoutFingerprint = layout.Fingerprint,
            Entries = changes.Select(change =>
            {
                var names = GetPrismMetadataNames(change.Path);
                return new PrismMetadataEntryDocument
                {
                    Path = change.Path,
                    BackupFileName = names.Backup,
                    StagedFileName = names.Staged,
                    OriginalSha512 = change.OriginalBytes is null ? null : HashBytes(change.OriginalBytes),
                    ReplacementSha512 = HashBytes(change.ReplacementBytes),
                    State = "SnapshotIntent"
                };
            }).ToList()
        };
        _journal.PrismMetadataReceipt = receipt;
        ValidatePrismMetadataReceipt();
        WriteJournal();

        for (var index = 0; index < changes.Count; index++)
        {
            var change = changes[index];
            var entry = receipt.Entries[index];
            var target = SafePath.Resolve(_ownerDirectory, entry.Path);
            SafePath.EnsureNoReparsePoints(_ownerDirectory, target);
            var current = PrismLauncherService.ReadOptionalMetadata(target, allowEmpty: false);
            if (!OptionalBytesMatch(current, entry.OriginalSha512))
                throw new InstallerException("PRISM_INSTANCE_OWNERSHIP_CONFLICT", LocalizedText.Get("PrismIdentityChanged"));
            if (change.OriginalBytes is not null)
                WriteBytesDurable(change.OriginalBytes, SafePath.Resolve(_folder, entry.BackupFileName), _folder);
            WriteBytesDurable(change.ReplacementBytes, SafePath.Resolve(StagingRoot, entry.StagedFileName!), StagingRoot);
            entry.State = "BackupReady";
            WriteJournal();
            Checkpoint($"after-prism-metadata-snapshot-{index}");
        }
    }

    public void SnapshotPrismUninstallMetadata()
    {
        if (_journal.Operation != "uninstall" || _layout is not { IsPrism: true } layout ||
            !PrismLauncherService.HasOwnedBinding(layout))
            throw new InstallerException("PRISM_INSTANCE_OWNERSHIP_CONFLICT", LocalizedText.Get("PrismIdentityChanged"));

        var receipt = new PrismMetadataReceiptDocument
        {
            Operation = "uninstall",
            LayoutFingerprint = layout.Fingerprint,
            Entries = PrismMetadataPaths.Select(path =>
            {
                var names = GetPrismMetadataNames(path);
                var target = SafePath.Resolve(_ownerDirectory, path);
                SafePath.EnsureNoReparsePoints(_ownerDirectory, target);
                var original = PrismLauncherService.ReadOptionalMetadata(target, allowEmpty: true);
                return new PrismMetadataEntryDocument
                {
                    Path = path,
                    BackupFileName = names.Backup,
                    ResidueFileName = names.Residue,
                    OriginalSha512 = original is null ? null : HashBytes(original),
                    State = "SnapshotIntent"
                };
            }).ToList()
        };
        _journal.PrismMetadataReceipt = receipt;
        ValidatePrismMetadataReceipt();
        WriteJournal();

        foreach (var entry in receipt.Entries)
        {
            var target = SafePath.Resolve(_ownerDirectory, entry.Path);
            SafePath.EnsureNoReparsePoints(_ownerDirectory, target);
            var original = PrismLauncherService.ReadOptionalMetadata(target, allowEmpty: true);
            if (!OptionalBytesMatch(original, entry.OriginalSha512))
                throw new InstallerException("PRISM_INSTANCE_OWNERSHIP_CONFLICT", LocalizedText.Get("PrismIdentityChanged"));
            if (original is not null)
                WriteBytesDurable(original, SafePath.Resolve(_folder, entry.BackupFileName), _folder);
            entry.State = "BackupReady";
            WriteJournal();
        }
    }

    public void CommitPrismMetadata()
    {
        var receipt = _journal.PrismMetadataReceipt;
        if (receipt is null) return;
        if (_layout is { IsPrism: true }) PrismLauncherService.RecheckTarget(_layout);
        ValidatePrismMetadataReceipt();
        foreach (var entry in receipt.Entries)
        {
            var target = SafePath.Resolve(_ownerDirectory, entry.Path);
            SafePath.EnsureNoReparsePoints(_ownerDirectory, target);
            if (receipt.Operation == "repair")
            {
                RequirePrismMetadataOriginal(entry, target);
                var staged = SafePath.Resolve(StagingRoot, entry.StagedFileName!);
                SafePath.EnsureNoReparsePoints(StagingRoot, staged);
                if (!File.Exists(staged) || !HashEquals(HashFile(staged), entry.ReplacementSha512))
                    throw RecoveryRequired(_folder);
                entry.State = "CommitIntent";
                WriteJournal();
                Checkpoint("before-prism-metadata-repair-commit:" + entry.Path);
                RequirePrismMetadataOriginal(entry, target);
                if (entry.OriginalSha512 is null) File.Move(staged, target);
                else File.Move(staged, target, overwrite: true);
                if (!HashEquals(HashFile(target), entry.ReplacementSha512)) throw RecoveryRequired(_folder);
                entry.State = "Committed";
                WriteJournal();
                Checkpoint("after-prism-metadata-repair-commit:" + entry.Path);
            }
            else
            {
                if (entry.OriginalSha512 is null)
                {
                    if (File.Exists(target) || File.Exists(SafePath.Resolve(_ownerDirectory, entry.ResidueFileName!)))
                        throw RecoveryRequired(_folder);
                    entry.State = "Moved";
                    WriteJournal();
                    continue;
                }
                RequirePrismMetadataOriginal(entry, target);
                var residue = SafePath.Resolve(_ownerDirectory, entry.ResidueFileName!);
                SafePath.EnsureNoReparsePoints(_ownerDirectory, residue);
                if (File.Exists(residue)) throw RecoveryRequired(_folder);
                entry.State = "MoveIntent";
                WriteJournal();
                Checkpoint("before-prism-metadata-uninstall-move:" + entry.Path);
                RequirePrismMetadataOriginal(entry, target);
                File.Move(target, residue);
                if (!HashEquals(HashFile(residue), entry.OriginalSha512)) throw RecoveryRequired(_folder);
                entry.State = "Moved";
                WriteJournal();
                Checkpoint("after-prism-metadata-uninstall-move:" + entry.Path);
            }
        }
    }

    private static readonly string[] PrismMetadataPaths = ["instance.cfg", "mmc-pack.json"];

    private static (string Backup, string? Staged, string? Residue) GetPrismMetadataNames(string path) => path switch
    {
        "instance.cfg" => ("backup/prism-metadata/instance.cfg.bin", "prism-metadata/instance.cfg.bin",
            PrismLauncherService.UninstalledConfigName),
        "mmc-pack.json" => ("backup/prism-metadata/mmc-pack.json.bin", "prism-metadata/mmc-pack.json.bin",
            PrismLauncherService.UninstalledComponentManifestName),
        _ => throw RecoveryRequired(path)
    };

    private void RequirePrismMetadataOriginal(PrismMetadataEntryDocument entry, string target)
    {
        var current = PrismLauncherService.ReadOptionalMetadata(target, allowEmpty: true);
            if (!OptionalBytesMatch(current, entry.OriginalSha512))
            throw new InstallerException("PRISM_INSTANCE_OWNERSHIP_CONFLICT", LocalizedText.Get("PrismIdentityChanged"));
    }

    private static void WriteBytesDurable(byte[] bytes, string destination, string root)
    {
        SafePath.EnsureNoReparsePoints(root, destination);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        SafePath.EnsureNoReparsePoints(root, destination);
        using var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private static void DeletePrismMetadataTransactionFile(string path, string root)
    {
        if (!File.Exists(path)) return;
        SafePath.EnsureNoReparsePoints(root, path);
        File.Delete(path);
    }

    private static string HashBytes(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA512.HashData(bytes));

    private static bool OptionalBytesMatch(byte[]? bytes, string? expectedHash) =>
        bytes is null ? expectedHash is null : HashEquals(HashBytes(bytes), expectedHash);

    public void SetProfileReceipt(LauncherProfileRemovalReceipt? receipt)
    {
        _journal.ProfileReceipt = receipt is null ? null : ProfileReceiptDocument.FromReceipt(receipt);
        _journal.ProfileState = receipt is null ? "None" : "BackupIntent";
        WriteJournal();
    }

    public void MarkProfileBackupReady()
    {
        if (_journal.ProfileReceipt is null) return;
        _journal.ProfileState = "BackupReady";
        WriteJournal();
    }

    public void MarkPrepared()
    {
        _journal.Phase = "Prepared";
        WriteJournal();
        Log("prepare", "completed", "transaction_prepare_completed", new { transactionId = Id });
    }

    public void EnsureCommitSpace()
    {
        EnsureAvailableSpace(_folder, MaxJournalBytes);
    }

    public void CommitFiles(InstanceUseGuard.Scope instanceUse)
    {
        if (_journal.Phase is not ("Prepared" or "Committing")) throw RecoveryRequired(_folder);
        if (_layout is { IsPrism: true }) PrismLauncherService.RecheckTarget(_layout);
        Log("commit", "started", "transaction_commit_started", new { transactionId = Id });
        _journal.Phase = "Committing";
        WriteJournal();
        for (var index = 0; index < _journal.Files.Count; index++)
        {
            var entry = _journal.Files[index];
            var target = SafePath.Resolve(_instancePath, entry.Path);
            SafePath.EnsureNoReparsePoints(_instancePath, target);
            var staged = SafePath.Resolve(StagingRoot, entry.Path);
            SafePath.EnsureNoReparsePoints(StagingRoot, staged);
            var backup = BackupFile(index);

            if (entry.OriginalSha512 is not null)
            {
                instanceUse.ProtectManagedFile(target);
                if (!File.Exists(target) || !HashEquals(instanceUse.HashManagedFile(target), entry.OriginalSha512))
                    throw new InstallerException("TRANSACTION_TARGET_CHANGED", LocalizedText.Get("TransactionTargetChanged", Path.GetFileName(target)));
                _journal.Files[index].State = "OriginalMoveIntent";
                WriteJournal();
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                SafePath.EnsureNoReparsePoints(_folder, backup);
                File.Move(target, backup);
                instanceUse.ReleaseManagedFile(target);
                if (!HashEquals(HashFile(backup), entry.OriginalSha512)) throw RecoveryRequired(_folder);
                Checkpoint($"after-original-move-{index}-before-journal");
                _journal.Files[index].State = "OriginalMoved";
                WriteJournal();
            }
            else if (File.Exists(target))
            {
                if (entry.CreateOnly)
                {
                    entry.State = "SkippedExisting";
                    WriteJournal();
                    continue;
                }
                instanceUse.ProtectManagedFile(target);
                throw new InstallerException("TRANSACTION_TARGET_CHANGED", LocalizedText.Get("TransactionTargetChanged", Path.GetFileName(target)));
            }

            if (entry.ReplacementSha512 is not null)
            {
                if (!File.Exists(staged) || !HashEquals(HashFile(staged), entry.ReplacementSha512))
                    throw new InstallerException("REPAIR_HASH_MISMATCH", LocalizedText.Get("RepairedHashMismatch"));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                SafePath.EnsureNoReparsePoints(_instancePath, Path.GetDirectoryName(target)!);
                if (File.Exists(target)) throw new InstallerException("TRANSACTION_TARGET_CHANGED", LocalizedText.Get("TransactionTargetChanged", Path.GetFileName(target)));
                _journal.Files[index].State = "ReplacementMoveIntent";
                WriteJournal();
                File.Move(staged, target);
                instanceUse.ProtectManagedFile(target);
                if (!HashEquals(instanceUse.HashManagedFile(target), entry.ReplacementSha512))
                    throw new InstallerException("REPAIR_HASH_MISMATCH", LocalizedText.Get("RepairedHashMismatch"));
                Checkpoint($"after-replacement-move-{index}-before-journal");
                _journal.Files[index].State = "ReplacementMoved";
                WriteJournal();
            }
            else
            {
                _journal.Files[index].State = "Removed";
                WriteJournal();
            }
            Checkpoint($"after-file-move-{index}");
        }

        VerifyCommittedFiles(instanceUse);
        Checkpoint("after-final-verification-before-journal");
        _journal.Phase = "FilesCommitted";
        WriteJournal();
        Log("commit", "files_committed", "transaction_files_committed", new { transactionId = Id });
    }

    public void MarkProfileCommitIntent()
    {
        if (_journal.ProfileReceipt is null || !_journal.ProfileReceipt.ChangesProfile) return;
        Log("profile", "started", "profile_removal_started", new { transactionId = Id });
        _journal.ProfileState = "CommitIntent";
        WriteJournal();
    }

    public void MarkProfileCommitted()
    {
        if (_journal.ProfileReceipt is not null && _journal.ProfileReceipt.ChangesProfile)
            _journal.ProfileState = "Committed";
        _journal.Phase = "ProfileCommitted";
        WriteJournal();
        Log("profile", "completed", "profile_removal_completed", new { transactionId = Id });
    }

    public void RemoveUninstallMetadata(string root)
    {
        if (_journal.Operation != "uninstall" || _journal.Phase != "ProfileCommitted") throw RecoveryRequired(_folder);
        if (_layout is { IsPrism: true } &&
            (_journal.PrismMetadataReceipt is null || _journal.PrismMetadataReceipt.Entries.Any(entry => entry.State != "Moved")))
            throw RecoveryRequired(_folder);
        if (!Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)).Equals(_installRoot, StringComparison.OrdinalIgnoreCase))
            throw new InstallerException("ROOT_UNSAFE", LocalizedText.Get("UnsafePath"));
        _journal.Phase = "Finalizing";
        WriteJournal();

        if (_journal.ActiveMarkerSha512 is not null)
        {
            var marker = SafePath.Resolve(root, ".minepack-active.json");
            SafePath.EnsureNoReparsePoints(root, marker);
            if (!File.Exists(marker) || !HashEquals(HashFile(marker), _journal.ActiveMarkerSha512) ||
                !Path.TrimEndingDirectorySeparator(Path.GetFullPath(GetMarkerInstancePath(root)))
                    .Equals(_instancePath, StringComparison.OrdinalIgnoreCase))
                throw new InstallerException("ACTIVE_MARKER_CONFLICT", LocalizedText.Get("ActiveMarkerCorrupt"));
            Checkpoint("before-active-marker-remove");
            _journal.ActiveMarkerState = "RemoveIntent";
            WriteJournal();
            File.Delete(marker);
            Checkpoint("after-active-marker-remove-before-journal");
            _journal.ActiveMarkerRemoved = true;
            _journal.ActiveMarkerState = "Removed";
            WriteJournal();
        }

        var manifest = SafePath.Resolve(_instancePath, InstallationManifest.FileName);
        SafePath.EnsureNoReparsePoints(_instancePath, manifest);
        if (_journal.ManifestSha512 is null || !File.Exists(manifest) ||
            !HashEquals(HashFile(manifest), _journal.ManifestSha512))
            throw new InstallerException("MANIFEST_INVALID", LocalizedText.Get("ManifestCorrupt"));
        Checkpoint("before-manifest-remove");
        _journal.ManifestState = "RemoveIntent";
        WriteJournal();
        File.Delete(manifest);
        Checkpoint("after-manifest-remove-before-journal");
        _journal.ManifestRemoved = true;
        _journal.ManifestState = "Removed";
        WriteJournal();
        Log("commit", "metadata_removed", "uninstall_metadata_removed", new { transactionId = Id });
    }

    public void MarkCompleted()
    {
        if (_layout is { IsPrism: true } && _journal.Operation == "uninstall" && _journal.PrismMetadataReceipt is null)
            throw RecoveryRequired(_folder);
        if (_journal.PrismMetadataReceipt is { } prismReceipt && prismReceipt.Entries.Any(entry =>
                entry.State != (prismReceipt.Operation == "repair" ? "Committed" : "Moved")))
            throw RecoveryRequired(_folder);
        _journal.Phase = "Completed";
        WriteJournal();
        Log("commit", "completed", "transaction_completed", new { transactionId = Id });
    }

    public void Rollback(string root, InstanceUseGuard.Scope instanceUse, FabricLauncherService? launcher,
        PackArchive pack, InstallationManifest manifest)
    {
        if (_layout is { IsPrism: true }) PrismLauncherService.RecheckTarget(_layout);
        instanceUse.Recheck();
        if (_journal.Phase is not ("RollingBack" or "RollbackIncomplete"))
            _journal.RollbackFromPhase = _journal.Phase;
        Log("rollback", "started", "transaction_rollback_started", new { transactionId = Id });
        _journal.Phase = "RollingBack";
        WriteJournal();
        var failures = new List<Exception>();
        var prismTransaction = _layout is { IsPrism: true };
        if (_journal.Operation == "uninstall" && !prismTransaction)
        {
            TryRestoreMetadata(root, _journal.ManifestSha512,
                Path.Combine(_folder, "backup", "manifest-snapshot", InstallationManifest.FileName),
                SafePath.Resolve(_instancePath, InstallationManifest.FileName), failures);
            TryRestoreMetadata(root, _journal.ActiveMarkerSha512,
                Path.Combine(_folder, "backup", "active-marker.json"), SafePath.Resolve(root, ".minepack-active.json"), failures);

            if (_journal.ProfileReceipt is not null)
            {
                if (launcher is null)
                    failures.Add(RecoveryRequired(_folder));
                else
                {
                    try
                    {
                        if (_journal.ProfileState is "CommitIntent" or "Committed")
                            launcher.RestoreOwnProfileRemoval(_instancePath, _journal.ProfileReceipt.ToReceipt(), pack, manifest);
                    }
                    catch (Exception ex) { failures.Add(ex); }
                }
            }
        }

        for (var index = _journal.Files.Count - 1; index >= 0; index--)
        {
            try
            {
                RestoreFile(index, instanceUse);
                Checkpoint($"after-rollback-file-{index}");
            }
            catch (Exception ex) { failures.Add(ex); }
        }

        if (prismTransaction && failures.Count == 0)
        {
            RestorePrismMetadata(failures);
            if (_journal.Operation == "uninstall" && failures.Count == 0)
            {
                TryRestoreMetadata(root, _journal.ManifestSha512,
                    Path.Combine(_folder, "backup", "manifest-snapshot", InstallationManifest.FileName),
                    SafePath.Resolve(_instancePath, InstallationManifest.FileName), failures,
                    targetRoot: _instancePath);
                if (failures.Count == 0)
                    TryRestoreMetadata(root, _journal.ActiveMarkerSha512,
                        Path.Combine(_folder, "backup", "active-marker.json"), SafePath.Resolve(root, ".minepack-active.json"), failures);
            }
        }

        if (failures.Count > 0)
        {
            _journal.Phase = "RollbackIncomplete";
            WriteJournal();
            Log("rollback", "incomplete", "transaction_rollback_incomplete", new
            {
                transactionId = Id,
                recoveryPath = _folder,
                failureTypes = failures.Select(failure => failure.GetType().Name).Distinct().ToArray(),
                failureCodes = failures.Select(failure => failure is InstallerException installerFailure
                    ? installerFailure.Code : failure.GetType().Name).Distinct().ToArray()
            });
            throw RecoveryRequired(_folder, failures[0]);
        }

        _journal.Phase = "RolledBack";
        WriteJournal();
        if (TryCleanupFinished(launcher, pack, manifest, instanceUse) is { } residue)
        {
            Log("rollback", "recovery_required", "transaction_rollback_residue", new { transactionId = Id, recoveryPath = residue });
            throw RecoveryRequired(residue);
        }
        Log("rollback", "completed", "transaction_rollback_completed", new { transactionId = Id });
    }

    public string? CleanupAfterCommit(FabricLauncherService? launcher, PackArchive pack,
        InstallationManifest manifest, InstanceUseGuard.Scope instanceUse)
    {
        if (!IsFinished) throw RecoveryRequired(_folder);
        return TryCleanupFinished(launcher, pack, manifest, instanceUse);
    }

    private bool IsFinished => _journal.Phase is "Completed" or "RolledBack";

    private string? TryCleanupFinished(FabricLauncherService? launcher, PackArchive pack,
        InstallationManifest manifest, InstanceUseGuard.Scope instanceUse)
    {
        if (!IsFinished) return _folder;
        try
        {
            ValidateTerminalState(pack, manifest, instanceUse, launcher);
            ValidateTransactionInventoryAsync(pack, manifest).GetAwaiter().GetResult();
            if (!ValidateTransactionTree()) return _folder;
            TryDeleteTree(_folder);
            if (Directory.Exists(_folder)) return _folder;
            if (_journal.ProfileReceipt is not null)
            {
                var receipt = _journal.ProfileReceipt.ToReceipt();
                try { launcher!.ValidateAndCleanupOwnProfileBackup(_instancePath, receipt, _journal.Phase == "Completed", pack, manifest); }
                catch { return launcher!.GetOwnProfileRemovalBackupPath(receipt); }
                var backup = launcher!.GetOwnProfileRemovalBackupPath(receipt);
                if (File.Exists(backup)) return backup;
            }
            return null;
        }
        catch { return _folder; }
    }

    public void Recover(string root, PackArchive pack, InstallationManifest manifest,
        InstanceUseGuard.Scope instanceUse, FabricLauncherService? launcher)
    {
        if (!Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)).Equals(_installRoot, StringComparison.OrdinalIgnoreCase))
            throw RecoveryRequired(_folder);
        if (_layout is { IsPrism: true }) PrismLauncherService.RecheckTarget(_layout);
        ValidateIdentity(pack);
        ValidateEntriesAsync(pack, manifest).GetAwaiter().GetResult();
        Log("recovery", "started", "transaction_recovery_started", new { transactionId = Id, phase = _journal.Phase });
        if (IsFinished)
        {
            if (TryCleanupFinished(launcher, pack, manifest, instanceUse) is { } residue) throw RecoveryRequired(residue);
            Log("recovery", "completed", "transaction_recovery_completed", new { transactionId = Id });
            return;
        }
        if (_journal.ProfileReceipt is not null && launcher is null) throw RecoveryRequired(_folder);
        ValidateTransactionInventoryAsync(pack, manifest).GetAwaiter().GetResult();
        Rollback(root, instanceUse, launcher, pack, manifest);
        Log("recovery", "completed", "transaction_recovery_completed", new { transactionId = Id });
    }

    private void ValidateTerminalState(PackArchive pack, InstallationManifest manifest,
        InstanceUseGuard.Scope instanceUse, FabricLauncherService? launcher)
    {
        ValidateIdentity(pack);
        ValidateEntriesAsync(pack, manifest).GetAwaiter().GetResult();
        var currentManifestPath = SafePath.Resolve(_instancePath, InstallationManifest.FileName);
        SafePath.EnsureNoReparsePoints(_instancePath, currentManifestPath);
        var markerPath = SafePath.Resolve(_installRoot, ".minepack-active.json");
        SafePath.EnsureNoReparsePoints(_installRoot, markerPath);
        var entries = _journal.Files.ToDictionary(entry => entry.Path, StringComparer.OrdinalIgnoreCase);
        var managed = manifest.Files.Where(file => !InitialConfiguration.IsInitialUserConfig(pack, file.Path));

        if (_journal.Phase == "Completed" && _journal.Operation == "repair")
        {
            if (!File.Exists(currentManifestPath)) throw RecoveryRequired(_folder);
            var currentManifest = InstallationManifest.Load(_instancePath);
            InstallService.ValidateMatchesRelease(currentManifest, pack);
            foreach (var file in managed)
            {
                var target = SafePath.Resolve(_instancePath, file.Path);
                SafePath.EnsureNoReparsePoints(_instancePath, target);
                var expectedHash = entries.TryGetValue(file.Path, out var entry) ? entry.ReplacementSha512 : file.Sha512;
                if (expectedHash is null || !File.Exists(target) || !HashEquals(instanceUse.HashManagedFile(target), expectedHash))
                    throw RecoveryRequired(_folder);
            }
            foreach (var entry in _journal.Files)
                if (entry.State is not ("ReplacementMoved" or "SkippedExisting")) throw RecoveryRequired(_folder);
            VerifyCreateOnlyCommittedFiles(instanceUse);
            VerifyPrismMetadataTerminal(completed: true);
            return;
        }

        if (_journal.Phase == "Completed" && _journal.Operation == "uninstall")
        {
            if (File.Exists(currentManifestPath) ||
                (_journal.ActiveMarkerSha512 is not null && File.Exists(markerPath)))
                throw RecoveryRequired(_folder);
            foreach (var file in managed)
            {
                var target = SafePath.Resolve(_instancePath, file.Path);
                SafePath.EnsureNoReparsePoints(_instancePath, target);
                if (File.Exists(target)) throw RecoveryRequired(_folder);
            }
            if (_journal.Files.Any(entry => entry.State is not ("Removed" or "SkippedExisting")))
                throw RecoveryRequired(_folder);
            VerifyProfileReceipt(pack, manifest, launcher, expectCandidate: true, requireBackup: true);
            VerifyPrismMetadataTerminal(completed: true);
            return;
        }

        if (_journal.Phase != "RolledBack") throw RecoveryRequired(_folder);
        foreach (var file in managed)
        {
            var target = SafePath.Resolve(_instancePath, file.Path);
            SafePath.EnsureNoReparsePoints(_instancePath, target);
            if (!entries.TryGetValue(file.Path, out var entry)) continue;

            if (entry.OriginalSha512 is not null)
            {
                if (!File.Exists(target) || !HashEquals(instanceUse.HashManagedFile(target), entry.OriginalSha512))
                    throw RecoveryRequired(_folder);
            }
            else if (entry.State != "Planned" && File.Exists(target))
            {
                throw RecoveryRequired(_folder);
            }
        }

        foreach (var entry in _journal.Files.Where(item => item.CreateOnly))
        {
            var target = SafePath.Resolve(_instancePath, entry.Path);
            SafePath.EnsureNoReparsePoints(_instancePath, target);
            if (entry.State == "SkippedExisting")
            {
                if (!File.Exists(target)) throw RecoveryRequired(_folder);
            }
            else if (entry.State != "Planned" && File.Exists(target))
            {
                throw RecoveryRequired(_folder);
            }
        }

        if (_journal.Operation == "uninstall")
        {
            VerifySnapshot(_journal.ManifestSha512, Path.Combine(_folder, "backup", "manifest-snapshot", InstallationManifest.FileName), currentManifestPath);
            VerifySnapshot(_journal.ActiveMarkerSha512, Path.Combine(_folder, "backup", "active-marker.json"), markerPath);
        }
        VerifyProfileReceipt(pack, manifest, launcher, expectCandidate: false,
            requireBackup: _journal.ProfileState is "CommitIntent" or "Committed");
        VerifyPrismMetadataTerminal(completed: false);
    }

    private void VerifyPrismMetadataTerminal(bool completed)
    {
        var receipt = _journal.PrismMetadataReceipt;
        if (_layout is { IsPrism: true } && _journal.Operation == "uninstall" && completed && receipt is null)
            throw RecoveryRequired(_folder);
        if (receipt is null) return;
        ValidatePrismMetadataReceipt();
        foreach (var entry in receipt.Entries)
        {
            var target = SafePath.Resolve(_ownerDirectory, entry.Path);
            SafePath.EnsureNoReparsePoints(_ownerDirectory, target);
            if (completed && receipt.Operation == "repair")
            {
                if (entry.State != "Committed" || !File.Exists(target) ||
                    !HashEquals(HashFile(target), entry.ReplacementSha512)) throw RecoveryRequired(_folder);
            }
            else if (completed)
            {
                if (entry.State != "Moved" || File.Exists(target)) throw RecoveryRequired(_folder);
                var residue = SafePath.Resolve(_ownerDirectory, entry.ResidueFileName!);
                SafePath.EnsureNoReparsePoints(_ownerDirectory, residue);
                if (entry.OriginalSha512 is null ? File.Exists(residue) :
                    !File.Exists(residue) || !HashEquals(HashFile(residue), entry.OriginalSha512))
                    throw RecoveryRequired(_folder);
            }
            else
            {
                if (entry.State != "Restored") throw RecoveryRequired(_folder);
                var current = PrismLauncherService.ReadOptionalMetadata(target, allowEmpty: true);
                if (!OptionalBytesMatch(current, entry.OriginalSha512)) throw RecoveryRequired(_folder);
                if (entry.ResidueFileName is not null && File.Exists(SafePath.Resolve(_ownerDirectory, entry.ResidueFileName)))
                    throw RecoveryRequired(_folder);
            }
        }
    }

    private void VerifyCreateOnlyCommittedFiles(InstanceUseGuard.Scope instanceUse)
    {
        foreach (var entry in _journal.Files.Where(item => item.CreateOnly))
        {
            var target = SafePath.Resolve(_instancePath, entry.Path);
            SafePath.EnsureNoReparsePoints(_instancePath, target);
            if (entry.State == "SkippedExisting")
            {
                if (!File.Exists(target)) throw RecoveryRequired(_folder);
                continue;
            }
            if (entry.State != "ReplacementMoved" || entry.ReplacementSha512 is null || !File.Exists(target) ||
                !HashEquals(instanceUse.HashManagedFile(target), entry.ReplacementSha512))
                throw RecoveryRequired(_folder);
        }
    }

    private void VerifyProfileReceipt(PackArchive pack, InstallationManifest manifest, FabricLauncherService? launcher,
        bool expectCandidate, bool requireBackup)
    {
        if (_journal.ProfileReceipt is null) return;
        if (launcher is null) throw RecoveryRequired(_folder);
        launcher.VerifyOwnProfileRemovalState(_instancePath, _journal.ProfileReceipt.ToReceipt(),
            expectCandidate, pack, manifest, requireBackup);
    }

    private void VerifySnapshot(string? expectedHash, string snapshotPath, string targetPath)
    {
        if (expectedHash is null) return;
        SafePath.EnsureNoReparsePoints(_folder, snapshotPath);
        if (!File.Exists(snapshotPath) || !HashEquals(HashFile(snapshotPath), expectedHash) ||
            !File.Exists(targetPath) || !HashEquals(HashFile(targetPath), expectedHash))
            throw RecoveryRequired(_folder);
    }

    private async Task ValidateTransactionInventoryAsync(PackArchive pack, InstallationManifest manifest)
    {
        var authorizedStagedPaths = pack.Files.Select(file => file.Path)
            .Concat(pack.Overrides.Select(file => file.Path))
            .Concat(InitialConfiguration.GetInitialPaths(pack))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var authorizedStagedHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var authorizedBackups = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (_journal.PrismMetadataReceipt is { } prismReceipt)
        {
            foreach (var metadata in prismReceipt.Entries)
            {
                if (metadata.OriginalSha512 is not null)
                    authorizedBackups[metadata.BackupFileName["backup/".Length..]] =
                        metadata.State == "SnapshotIntent" ? "" : metadata.OriginalSha512;
                if (metadata.StagedFileName is not null)
                {
                    authorizedStagedPaths.Add(metadata.StagedFileName);
                    if (metadata.ReplacementSha512 is not null)
                        authorizedStagedHashes[metadata.StagedFileName] =
                            metadata.State == "SnapshotIntent" ? "" : metadata.ReplacementSha512;
                }
            }
        }
        for (var index = 0; index < _journal.Files.Count; index++)
        {
            var entry = _journal.Files[index];
            if (entry.OriginalSha512 is not null)
                authorizedBackups[$"files/{index:D4}.bin"] = entry.OriginalSha512;
        }
        if (_journal.Operation == "uninstall")
        {
            authorizedBackups[$"manifest-snapshot/{InstallationManifest.FileName}"] = _journal.ManifestSha512 ?? "";
            authorizedBackups["active-marker.json"] = _journal.ActiveMarkerSha512 ?? "";
        }

        SafePath.EnsureNoReparsePoints(Path.GetDirectoryName(_folder)!, _folder);
        foreach (var item in Directory.EnumerateFileSystemEntries(_folder))
        {
            var attributes = File.GetAttributes(item);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw RecoveryRequired(_folder);
            var name = Path.GetFileName(item);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                if (name.Equals("staged", StringComparison.Ordinal))
                    ValidateKnownTree(item, authorizedStagedPaths, authorizedStagedHashes);
                else if (name.Equals("backup", StringComparison.Ordinal))
                    ValidateKnownTree(item, authorizedBackups.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase), authorizedBackups);
                else if (name.StartsWith("validation-", StringComparison.Ordinal) &&
                         Guid.TryParseExact(name[11..], "N", out _))
                    ValidateKnownTree(item, authorizedStagedPaths);
                else
                    throw RecoveryRequired(_folder);
            }
            else if (name == JournalName || name == "journal.tmp")
            {
                if (new FileInfo(item).Length > MaxJournalBytes) throw RecoveryRequired(_folder);
            }
            else throw RecoveryRequired(_folder);
        }

        foreach (var (relative, expectedHash) in authorizedBackups)
        {
            var path = SafePath.Resolve(Path.Combine(_folder, "backup"), relative);
            SafePath.EnsureNoReparsePoints(Path.Combine(_folder, "backup"), path);
            if (!File.Exists(path)) continue;
            if (expectedHash.Length > 0 && !HashEquals(HashFile(path), expectedHash)) throw RecoveryRequired(_folder);
        }
        if (_journal.PrismMetadataReceipt is { } boundedReceipt)
        {
            foreach (var entry in boundedReceipt.Entries)
            {
                foreach (var relative in new[]
                         {
                             entry.BackupFileName,
                             entry.StagedFileName is null ? null : "staged/" + entry.StagedFileName
                         })
                {
                    if (relative is null) continue;
                    var path = SafePath.Resolve(_folder, relative);
                    if (!File.Exists(path)) continue;
                    SafePath.EnsureNoReparsePoints(_folder, path);
                    if (new FileInfo(path).Length > PrismLauncherService.MaxMetadataBytes) throw RecoveryRequired(_folder);
                }
            }
        }
        await Task.CompletedTask;
    }

    private void ValidateKnownTree(string root, IReadOnlySet<string> allowedFiles,
        IReadOnlyDictionary<string, string>? expectedHashes = null)
        => ValidateKnownTree(root, root, allowedFiles, expectedHashes);

    private void ValidateKnownTree(string anchor, string current, IReadOnlySet<string> allowedFiles,
        IReadOnlyDictionary<string, string>? expectedHashes)
    {
        if (!Directory.Exists(current)) throw RecoveryRequired(_folder);
        SafePath.EnsureNoReparsePoints(_folder, current);
        foreach (var path in Directory.EnumerateFileSystemEntries(current))
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw RecoveryRequired(_folder);
            var relative = Path.GetRelativePath(anchor, path).Replace('\\', '/');
            if ((attributes & FileAttributes.Directory) != 0)
            {
                var prefix = relative + "/";
                if (!allowedFiles.Any(candidate => candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                    throw RecoveryRequired(_folder);
                ValidateKnownTree(anchor, path, allowedFiles, expectedHashes);
            }
            else
            {
                if (!allowedFiles.Contains(relative) && !IsDownloadPartial(relative, allowedFiles))
                    throw RecoveryRequired(_folder);
                if (expectedHashes is not null && expectedHashes.TryGetValue(relative, out var expectedHash) &&
                    expectedHash.Length > 0 && !HashEquals(HashFile(path), expectedHash))
                    throw RecoveryRequired(_folder);
            }
        }
    }

    private static bool IsDownloadPartial(string relative, IReadOnlySet<string> allowedFiles)
    {
        if (!relative.EndsWith(".partial", StringComparison.OrdinalIgnoreCase)) return false;
        foreach (var expected in allowedFiles)
        {
            if (!relative.StartsWith(expected + ".", StringComparison.OrdinalIgnoreCase)) continue;
            var token = relative[(expected.Length + 1)..^".partial".Length];
            if (Guid.TryParseExact(token, "N", out _)) return true;
        }
        return false;
    }

    private bool ValidateTransactionTree()
    {
        if (!Directory.Exists(_folder)) return false;
        SafePath.EnsureNoReparsePoints(Path.GetDirectoryName(_folder)!, _folder);
        return true;
    }

    private void VerifyCommittedFiles(InstanceUseGuard.Scope instanceUse)
    {
        foreach (var entry in _journal.Files)
        {
            if (entry.State == "SkippedExisting") continue;
            var target = SafePath.Resolve(_instancePath, entry.Path);
            SafePath.EnsureNoReparsePoints(_instancePath, target);
            if (entry.ReplacementSha512 is null)
            {
                if (File.Exists(target)) throw new InstallerException("UNINSTALL_VERIFY_FAILED", LocalizedText.Get("UninstallFailed"));
            }
            else if (!File.Exists(target) || !HashEquals(instanceUse.HashManagedFile(target), entry.ReplacementSha512))
                throw new InstallerException("REPAIR_HASH_MISMATCH", LocalizedText.Get("RepairedHashMismatch"));
        }
    }

    private void RestoreFile(int index, InstanceUseGuard.Scope instanceUse)
    {
        var entry = _journal.Files[index];
        var target = SafePath.Resolve(_instancePath, entry.Path);
        SafePath.EnsureNoReparsePoints(_instancePath, target);
        var backup = BackupFile(index);
        var staged = SafePath.Resolve(StagingRoot, entry.Path);
        SafePath.EnsureNoReparsePoints(StagingRoot, staged);
        var backupExists = File.Exists(backup);
        var stagedExists = File.Exists(staged);

        if (entry.State == "SkippedExisting") return;

        if (backupExists)
        {
            if (entry.OriginalSha512 is null)
                throw RecoveryRequired(_folder);
            if (File.Exists(target))
            {
                if (entry.ReplacementSha512 is null || stagedExists)
                    throw RecoveryRequired(_folder);
                instanceUse.ProtectManagedFile(target);
                if (!HashEquals(instanceUse.HashManagedFile(target), entry.ReplacementSha512))
                    throw RecoveryRequired(_folder);
                Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
                SafePath.EnsureNoReparsePoints(StagingRoot, Path.GetDirectoryName(staged)!);
                File.Move(target, staged);
                instanceUse.ReleaseManagedFile(target);
                if (!HashEquals(HashFile(staged), entry.ReplacementSha512)) throw RecoveryRequired(_folder);
            }
            instanceUse.ReleaseManagedFile(target);
            if (!HashEquals(HashFile(backup), entry.OriginalSha512)) throw RecoveryRequired(_folder);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Move(backup, target);
            instanceUse.ProtectCommittedFile(target);
            if (!HashEquals(instanceUse.HashManagedFile(target), entry.OriginalSha512)) throw RecoveryRequired(_folder);
            return;
        }

        if (entry.OriginalSha512 is not null)
        {
            if (!File.Exists(target)) throw RecoveryRequired(_folder);
            instanceUse.ProtectManagedFile(target);
            if (!HashEquals(instanceUse.HashManagedFile(target), entry.OriginalSha512)) throw RecoveryRequired(_folder);
            return;
        }

        if (!File.Exists(target) || stagedExists) return;
        instanceUse.ProtectManagedFile(target);
        if (entry.ReplacementSha512 is null || !HashEquals(instanceUse.HashManagedFile(target), entry.ReplacementSha512))
            throw RecoveryRequired(_folder);
        Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
        SafePath.EnsureNoReparsePoints(StagingRoot, Path.GetDirectoryName(staged)!);
        File.Move(target, staged);
        instanceUse.ReleaseManagedFile(target);
        if (!HashEquals(HashFile(staged), entry.ReplacementSha512)) throw RecoveryRequired(_folder);
    }

    private void TryRestoreMetadata(string root, string? expectedHash, string snapshot, string target,
        List<Exception> failures, string? targetRoot = null)
    {
        if (expectedHash is null) return;
        try
        {
            var verifiedTargetRoot = targetRoot ?? root;
            SafePath.EnsureNoReparsePoints(verifiedTargetRoot, target);
            SafePath.EnsureNoReparsePoints(_folder, snapshot);
            if (!File.Exists(snapshot) || !HashEquals(HashFile(snapshot), expectedHash)) throw RecoveryRequired(_folder);
            if (File.Exists(target))
            {
                if (!HashEquals(HashFile(target), expectedHash)) throw RecoveryRequired(_folder);
                return;
            }
            var temp = target + ".minepack-restore-" + _journal.TransactionId + ".tmp";
            SafePath.EnsureNoReparsePoints(verifiedTargetRoot, temp);
            WriteCopyDurable(snapshot, _folder, temp, verifiedTargetRoot);
            File.Move(temp, target);
            if (!HashEquals(HashFile(target), expectedHash)) throw RecoveryRequired(_folder);
        }
        catch (Exception ex) { failures.Add(ex); }
    }

    private void RestorePrismMetadata(List<Exception> failures)
    {
        var receipt = _journal.PrismMetadataReceipt;
        if (receipt is null) return;
        foreach (var entry in receipt.Entries.AsEnumerable().Reverse())
        {
            try
            {
                var target = SafePath.Resolve(_ownerDirectory, entry.Path);
                SafePath.EnsureNoReparsePoints(_ownerDirectory, target);
                if (receipt.Operation == "repair")
                {
                    var current = PrismLauncherService.ReadOptionalMetadata(target, allowEmpty: true);
                    if (entry.OriginalSha512 is null)
                    {
                        if (current is not null)
                        {
                            if (entry.State is not ("CommitIntent" or "Committed" or "RestoreIntent") ||
                                !HashEquals(HashBytes(current), entry.ReplacementSha512)) throw RecoveryRequired(_folder);
                            entry.State = "RestoreIntent";
                            WriteJournal();
                            File.Delete(target);
                        }
                        else if (entry.State is not ("SnapshotIntent" or "BackupReady" or "RestoreIntent" or "Restored"))
                            throw RecoveryRequired(_folder);
                    }
                    else if (!OptionalBytesMatch(current, entry.OriginalSha512))
                    {
                        var backup = SafePath.Resolve(_folder, entry.BackupFileName);
                        SafePath.EnsureNoReparsePoints(_folder, backup);
                        if (!File.Exists(backup) || !HashEquals(HashFile(backup), entry.OriginalSha512))
                            throw RecoveryRequired(_folder);
                        if (entry.State is not ("CommitIntent" or "Committed" or "RestoreIntent") || current is null ||
                            !HashEquals(HashBytes(current), entry.ReplacementSha512))
                            throw RecoveryRequired(_folder);
                        entry.State = "RestoreIntent";
                        WriteJournal();
                        var restoreTemp = SafePath.Resolve(_ownerDirectory,
                            $".minepack-restore-{_journal.TransactionId}-{Path.GetFileName(entry.Path)}.tmp");
                        SafePath.EnsureNoReparsePoints(_ownerDirectory, restoreTemp);
                        if (File.Exists(restoreTemp))
                        {
                            if (!HashEquals(HashFile(restoreTemp), entry.OriginalSha512)) throw RecoveryRequired(_folder);
                        }
                        else WriteCopyDurable(backup, _folder, restoreTemp, _ownerDirectory);
                        Checkpoint("before-prism-metadata-restore-write:" + entry.Path);
                        var currentBeforeRestore = PrismLauncherService.ReadOptionalMetadata(target, allowEmpty: true);
                        if (!OptionalBytesMatch(currentBeforeRestore, entry.ReplacementSha512)) throw RecoveryRequired(_folder);
                        File.Move(restoreTemp, target, overwrite: true);
                        if (!HashEquals(HashFile(target), entry.OriginalSha512)) throw RecoveryRequired(_folder);
                    }
                    DeletePrismMetadataTransactionFile(Path.Combine(_folder, entry.BackupFileName), _folder);
                    DeletePrismMetadataTransactionFile(SafePath.Resolve(StagingRoot, entry.StagedFileName!), StagingRoot);
                    var leftoverRestore = SafePath.Resolve(_ownerDirectory,
                        $".minepack-restore-{_journal.TransactionId}-{Path.GetFileName(entry.Path)}.tmp");
                    if (File.Exists(leftoverRestore))
                    {
                        if (entry.OriginalSha512 is null || !HashEquals(HashFile(leftoverRestore), entry.OriginalSha512))
                            throw RecoveryRequired(_folder);
                        DeletePrismMetadataTransactionFile(leftoverRestore, _ownerDirectory);
                    }
                    entry.State = "Restored";
                    WriteJournal();
                }
                else
                {
                    var residue = SafePath.Resolve(_ownerDirectory, entry.ResidueFileName!);
                    SafePath.EnsureNoReparsePoints(_ownerDirectory, residue);
                    var current = PrismLauncherService.ReadOptionalMetadata(target, allowEmpty: true);
                    var saved = PrismLauncherService.ReadOptionalMetadata(residue, allowEmpty: true);
                    if (entry.OriginalSha512 is null)
                    {
                        if (current is not null || saved is not null) throw RecoveryRequired(_folder);
                    }
                    else if (OptionalBytesMatch(current, entry.OriginalSha512) && saved is null)
                    {
                        // The move did not run, or an earlier recovery already restored it.
                    }
                    else if (current is null && OptionalBytesMatch(saved, entry.OriginalSha512))
                    {
                        if (entry.State is not ("MoveIntent" or "Moved" or "RestoreIntent"))
                            throw RecoveryRequired(_folder);
                        File.Move(residue, target);
                        if (!HashEquals(HashFile(target), entry.OriginalSha512)) throw RecoveryRequired(_folder);
                    }
                    else throw RecoveryRequired(_folder);
                    DeletePrismMetadataTransactionFile(Path.Combine(_folder, entry.BackupFileName), _folder);
                    entry.State = "Restored";
                    WriteJournal();
                }
                Checkpoint("after-prism-metadata-rollback:" + entry.Path);
            }
            catch (Exception ex)
            {
                failures.Add(ex);
                Log("rollback", "failed", "prism_metadata_restore_failed", new
                {
                    path = entry.Path,
                    errorCode = ex is InstallerException installerFailure ? installerFailure.Code : ex.GetType().Name
                });
            }
        }
    }

    private string GetMarkerInstancePath(string root)
    {
        var marker = _layout is null ? InstallService.ReadActiveMarker(root) : InstallService.ReadActiveMarker(_layout);
        if (marker.State != ActiveMarkerState.Valid || marker.InstancePath is null)
            throw new InstallerException("ACTIVE_MARKER_CONFLICT", LocalizedText.Get("ActiveMarkerCorrupt"));
        return marker.InstancePath;
    }

    private string BackupFile(int index) => Path.Combine(_folder, "backup", "files", $"{index:D4}.bin");

    private void ValidateIdentity(PackArchive pack)
    {
        if (!_journal.InstanceDirectoryName.Equals(Path.GetFileName(_ownerDirectory), StringComparison.Ordinal) ||
            !_journal.PackVersion.Equals(pack.VersionId, StringComparison.Ordinal) ||
            !HashEquals(_journal.PackArchiveSha512, pack.ArchiveSha512) ||
            !Guid.TryParseExact(_journal.TransactionId, "N", out _) ||
            _journal.Operation is not ("repair" or "uninstall") ||
            (_journal.SchemaVersion == 2 && (_layout is null || !_layout.IsPrism ||
                !string.Equals(_journal.LauncherKind, "Prism", StringComparison.Ordinal) ||
                !string.Equals(_journal.LayoutFingerprint, _layout.Fingerprint, StringComparison.OrdinalIgnoreCase))) ||
            _journal.Phase is not ("Preparing" or "Prepared" or "Committing" or "FilesCommitted" or "ProfileCommitted" or "Finalizing" or "RollingBack" or "RollbackIncomplete" or "Completed" or "RolledBack"))
            throw RecoveryRequired(_folder);
        ValidatePrismMetadataReceipt();
    }

    private void ValidateBasic()
    {
        if (_journal.SchemaVersion is not (1 or 2) || _journal.Files is null || _journal.Files.Count > MaxEntries ||
            _journal.TransactionId is null || _journal.InstanceDirectoryName is null || _journal.PackVersion is null ||
            _journal.PackArchiveSha512 is null || _journal.Operation is null || _journal.Phase is null)
            throw RecoveryRequired(_folder);
        var validLayout = _journal.SchemaVersion switch
        {
            1 => (_layout is null or { IsPrism: false }) && _journal.LauncherKind is null && _journal.LayoutFingerprint is null &&
                 _ownerDirectory.Equals(_instancePath, StringComparison.OrdinalIgnoreCase) &&
                 Path.GetDirectoryName(_instancePath)!.Equals(Path.Combine(_installRoot, "instances"), StringComparison.OrdinalIgnoreCase),
            2 => _layout is { IsPrism: true } && _journal.LauncherKind == "Prism" &&
                 _journal.LayoutFingerprint is not null &&
                 _journal.LayoutFingerprint.Equals(_layout.Fingerprint, StringComparison.OrdinalIgnoreCase) &&
                 _layout.StateRoot.Equals(_installRoot, StringComparison.OrdinalIgnoreCase) &&
                 _layout.GameDirectory.Equals(_instancePath, StringComparison.OrdinalIgnoreCase) &&
                 _layout.InstanceDirectory.Equals(_ownerDirectory, StringComparison.OrdinalIgnoreCase) &&
                 Path.GetDirectoryName(_ownerDirectory)!.Equals(_layout.InstancesRoot, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
        if (!Guid.TryParseExact(_journal.TransactionId, "N", out _) ||
            !PackArchive.IsSha512(_journal.PackArchiveSha512) ||
            !_journal.InstanceDirectoryName.Equals(Path.GetFileName(_ownerDirectory), StringComparison.Ordinal) ||
            !validLayout ||
            _journal.InstanceDirectoryName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            _journal.InstanceDirectoryName is "." or ".." ||
            _journal.PackVersion.Length is 0 or > 80 ||
            _journal.Operation is not ("repair" or "uninstall") ||
            _journal.Phase is not ("Preparing" or "Prepared" or "Committing" or "FilesCommitted" or "ProfileCommitted" or "Finalizing" or "RollingBack" or "RollbackIncomplete" or "Completed" or "RolledBack") ||
            _journal.Files.Any(entry => entry is null) ||
            _journal.ProfileState is not (null or "None" or "BackupIntent" or "BackupReady" or "CommitIntent" or "Committed") ||
            _journal.ActiveMarkerState is not (null or "RemoveIntent" or "Removed") ||
            _journal.ManifestState is not (null or "RemoveIntent" or "Removed") ||
            _journal.RollbackFromPhase is not (null or "Preparing" or "Prepared" or "Committing" or "FilesCommitted" or "ProfileCommitted" or "Finalizing" or "RollbackIncomplete"))
            throw RecoveryRequired(_folder);
        if ((_journal.ManifestSha512 is not null && !PackArchive.IsSha512(_journal.ManifestSha512)) ||
            (_journal.ActiveMarkerSha512 is not null && !PackArchive.IsSha512(_journal.ActiveMarkerSha512)))
            throw RecoveryRequired(_folder);
        if (_journal.ProfileReceipt is { } receipt &&
            (!new[] { "launcher_profiles.json", "launcher_profiles_microsoft_store.json" }.Contains(receipt.ProfileFileName, StringComparer.Ordinal) ||
             receipt.BackupFileName != $".minepack-{_journal.TransactionId}.profile-backup" ||
             !PackArchive.IsSha512(receipt.LauncherRootSha512) || !PackArchive.IsSha512(receipt.OriginalSha512) ||
             !PackArchive.IsSha512(receipt.CandidateSha512)))
            throw RecoveryRequired(_folder);
        ValidatePrismMetadataReceipt();
    }

    private void ValidatePrismMetadataReceipt()
    {
        var receipt = _journal.PrismMetadataReceipt;
        if (receipt is null) return;
        if (_journal.SchemaVersion != 2 || _layout is not { IsPrism: true } layout ||
            receipt.Operation != _journal.Operation ||
            !string.Equals(receipt.LayoutFingerprint, layout.Fingerprint, StringComparison.OrdinalIgnoreCase) ||
            receipt.Entries is null || receipt.Entries.Count is 0 or > 2 ||
            (_journal.Operation == "uninstall" && receipt.Entries.Count != PrismMetadataPaths.Length))
            throw RecoveryRequired(_folder);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in receipt.Entries)
        {
            if (entry is null || entry.Path is null || entry.BackupFileName is null || entry.State is null ||
                !seen.Add(entry.Path) || !PrismMetadataPaths.Contains(entry.Path, StringComparer.Ordinal))
                throw RecoveryRequired(_folder);
            var names = GetPrismMetadataNames(entry.Path);
            if (entry.BackupFileName != names.Backup ||
                (_journal.Operation == "repair" && (entry.StagedFileName != names.Staged || entry.ResidueFileName is not null ||
                    entry.ReplacementSha512 is null || !PackArchive.IsSha512(entry.ReplacementSha512))) ||
                (_journal.Operation == "uninstall" && (entry.StagedFileName is not null || entry.ResidueFileName != names.Residue ||
                    entry.ReplacementSha512 is not null)) ||
                (entry.OriginalSha512 is not null && !PackArchive.IsSha512(entry.OriginalSha512)) ||
                entry.State is not ("SnapshotIntent" or "BackupReady" or "CommitIntent" or "Committed" or "MoveIntent" or "Moved" or "RestoreIntent" or "Restored") ||
                (_journal.Operation == "repair" && entry.State is ("MoveIntent" or "Moved")) ||
                (_journal.Operation == "uninstall" && entry.State is ("CommitIntent" or "Committed")))
                throw RecoveryRequired(_folder);
        }
    }

    private static void ValidateJournalShape(ReadOnlyMemory<byte> bytes, string folder)
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
        var root = document.RootElement;
        string[] common = ["SchemaVersion", "TransactionId", "InstanceDirectoryName", "PackVersion", "PackArchiveSha512", "Operation", "Phase", "Files", "ManifestSha512", "ActiveMarkerSha512", "ActiveMarkerRemoved", "ManifestRemoved", "ActiveMarkerState", "ManifestState", "RollbackFromPhase", "ProfileState", "ProfileReceipt"];
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("SchemaVersion", out var schemaElement) ||
            schemaElement.ValueKind != JsonValueKind.Number ||
            !schemaElement.TryGetInt32(out var schema) || schema is not (1 or 2))
            throw RecoveryRequired(folder);
        var hasPrismMetadataReceipt = root.TryGetProperty("PrismMetadataReceipt", out var prismMetadataReceipt);
        var required = schema == 1 ? common : common.Concat(["LauncherKind", "LayoutFingerprint"]).ToArray();
        if ((schema == 1 && hasPrismMetadataReceipt) ||
            root.EnumerateObject().Count() != required.Length + (hasPrismMetadataReceipt ? 1 : 0) ||
            required.Any(name => !root.TryGetProperty(name, out _)) ||
            root.GetProperty("TransactionId").ValueKind != JsonValueKind.String ||
            root.GetProperty("InstanceDirectoryName").ValueKind != JsonValueKind.String ||
            root.GetProperty("PackVersion").ValueKind != JsonValueKind.String ||
            root.GetProperty("PackArchiveSha512").ValueKind != JsonValueKind.String ||
            root.GetProperty("Operation").ValueKind != JsonValueKind.String ||
            root.GetProperty("Phase").ValueKind != JsonValueKind.String ||
            root.GetProperty("Files").ValueKind != JsonValueKind.Array ||
            root.GetProperty("ActiveMarkerRemoved").ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
            root.GetProperty("ManifestRemoved").ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw RecoveryRequired(folder);
        if (schema == 2 && (root.GetProperty("LauncherKind").ValueKind != JsonValueKind.String ||
            root.GetProperty("LauncherKind").GetString() != "Prism" ||
            root.GetProperty("LayoutFingerprint").ValueKind != JsonValueKind.String ||
            root.GetProperty("LayoutFingerprint").GetString() is not { Length: 64 } fingerprint ||
            fingerprint.Any(c => !Uri.IsHexDigit(c))))
            throw RecoveryRequired(folder);
        foreach (var name in new[] { "ManifestSha512", "ActiveMarkerSha512", "ActiveMarkerState", "ManifestState", "RollbackFromPhase", "ProfileState" })
            if (root.GetProperty(name).ValueKind is not (JsonValueKind.String or JsonValueKind.Null)) throw RecoveryRequired(folder);
        foreach (var entry in root.GetProperty("Files").EnumerateArray())
        {
            string[] fields = ["Path", "OriginalSha512", "OriginalSize", "ReplacementSha512", "CreateOnly", "State"];
            if (entry.ValueKind != JsonValueKind.Object || entry.EnumerateObject().Count() != fields.Length ||
                fields.Any(name => !entry.TryGetProperty(name, out _)) ||
                entry.GetProperty("Path").ValueKind != JsonValueKind.String ||
                entry.GetProperty("OriginalSize").ValueKind != JsonValueKind.Number ||
                !entry.GetProperty("OriginalSize").TryGetInt64(out _) ||
                entry.GetProperty("CreateOnly").ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                entry.GetProperty("State").ValueKind != JsonValueKind.String ||
                entry.GetProperty("OriginalSha512").ValueKind is not (JsonValueKind.String or JsonValueKind.Null) ||
                entry.GetProperty("ReplacementSha512").ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                throw RecoveryRequired(folder);
        }
        var receipt = root.GetProperty("ProfileReceipt");
        string[] receiptFields = ["ProfileFileName", "BackupFileName", "LauncherRootSha512", "OriginalSha512", "CandidateSha512", "ChangesProfile"];
        if (receipt.ValueKind != JsonValueKind.Null &&
            (receipt.ValueKind != JsonValueKind.Object || receipt.EnumerateObject().Count() != receiptFields.Length ||
             receiptFields.Any(name => !receipt.TryGetProperty(name, out _)) ||
             receiptFields.Take(5).Any(name => receipt.GetProperty(name).ValueKind != JsonValueKind.String) ||
             receipt.GetProperty("ChangesProfile").ValueKind is not (JsonValueKind.True or JsonValueKind.False)))
            throw RecoveryRequired(folder);

        if (hasPrismMetadataReceipt)
        {
            string[] metadataFields = ["Operation", "LayoutFingerprint", "Entries"];
            if (schema != 2 || prismMetadataReceipt.ValueKind != JsonValueKind.Object ||
                prismMetadataReceipt.EnumerateObject().Count() != metadataFields.Length ||
                metadataFields.Any(name => !prismMetadataReceipt.TryGetProperty(name, out _)) ||
                prismMetadataReceipt.GetProperty("Operation").ValueKind != JsonValueKind.String ||
                prismMetadataReceipt.GetProperty("LayoutFingerprint").ValueKind != JsonValueKind.String ||
                prismMetadataReceipt.GetProperty("Entries").ValueKind != JsonValueKind.Array)
                throw RecoveryRequired(folder);
            foreach (var metadata in prismMetadataReceipt.GetProperty("Entries").EnumerateArray())
            {
                string[] fields = ["Path", "BackupFileName", "StagedFileName", "ResidueFileName", "OriginalSha512", "ReplacementSha512", "State"];
                if (metadata.ValueKind != JsonValueKind.Object || metadata.EnumerateObject().Count() != fields.Length ||
                    fields.Any(name => !metadata.TryGetProperty(name, out _)) ||
                    metadata.GetProperty("Path").ValueKind != JsonValueKind.String ||
                    metadata.GetProperty("BackupFileName").ValueKind != JsonValueKind.String ||
                    metadata.GetProperty("State").ValueKind != JsonValueKind.String ||
                    fields.Skip(2).Take(3).Any(name => metadata.GetProperty(name).ValueKind is not (JsonValueKind.String or JsonValueKind.Null)) ||
                    metadata.GetProperty("ReplacementSha512").ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                    throw RecoveryRequired(folder);
            }
        }
    }

    private void WriteJournal()
    {
        SafePath.EnsureNoReparsePoints(_folder, _journalPath);
        var temp = Path.Combine(_folder, "journal.tmp");
        SafePath.EnsureNoReparsePoints(_folder, temp);
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, _journal, JsonOptions);
            stream.Flush(flushToDisk: true);
        }
        if (new FileInfo(temp).Length > MaxJournalBytes) throw RecoveryRequired(_folder);
        File.Move(temp, _journalPath, overwrite: true);
    }

    private void Log(string stage, string outcome, string eventName, object? details = null)
    {
        try { _operationLog?.Write(stage, outcome, eventName, details); }
        catch { }
    }

    private static void WriteCopyDurable(string source, string sourceRoot, string destination, string destinationRoot)
    {
        SafePath.EnsureNoReparsePoints(sourceRoot, source);
        SafePath.EnsureNoReparsePoints(destinationRoot, destination);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        SafePath.EnsureNoReparsePoints(destinationRoot, destination);
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        input.CopyTo(output);
        output.Flush(flushToDisk: true);
    }

    private static void EnsureAvailableSpace(string path, long requiredBytes)
    {
        var driveRoot = Path.GetPathRoot(Path.GetFullPath(path))
            ?? throw new InstallerException("TRANSACTION_VOLUME", LocalizedText.Get("TransactionVolumeUnsupported"));
        try
        {
            if (requiredBytes < 0 || new DriveInfo(driveRoot).AvailableFreeSpace < requiredBytes)
                throw new InstallerException("TRANSACTION_SPACE", LocalizedText.Get("TransactionSpaceUnavailable"));
        }
        catch (InstallerException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or OverflowException)
        {
            throw new InstallerException("TRANSACTION_SPACE", LocalizedText.Get("TransactionSpaceUnavailable"), ex);
        }
    }

    private static void TryRemoveInitialFolder(string folder)
    {
        try
        {
            if (!Directory.Exists(folder)) return;
            SafePath.EnsureNoReparsePoints(Path.GetDirectoryName(folder)!, folder);
            var entries = Directory.EnumerateFileSystemEntries(folder).ToArray();
            foreach (var path in entries)
            {
                var name = Path.GetFileName(path);
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) return;
                if (name == "journal.tmp" && (attributes & FileAttributes.Directory) == 0) continue;
                if ((name is "staged" or "backup") && (attributes & FileAttributes.Directory) != 0 &&
                    !Directory.EnumerateFileSystemEntries(path).Any()) continue;
                return;
            }
            foreach (var path in entries)
            {
                if (Path.GetFileName(path) == "journal.tmp") File.Delete(path);
                else Directory.Delete(path);
            }
            Directory.Delete(folder);
        }
        catch { }
    }

    private static string HashFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA512.HashData(stream));
    }

    private static bool HashEquals(string? first, string? second) =>
        first is not null && second is not null && PackArchive.FixedTimeHashEquals(first, second);

    private static string TransactionDirectoryFor(string instancePath) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(instancePath)) + DirectorySuffix;

    private static InstallerException RecoveryRequired(string path, Exception? inner = null) =>
        new("TRANSACTION_RECOVERY_REQUIRED", LocalizedText.Get("TransactionRecoveryRequired", path), inner);

    private static void TryDeleteTree(string path)
    {
        try
        {
            if (!Directory.Exists(path)) return;
            var parent = Path.GetDirectoryName(path)!;
            SafePath.EnsureNoReparsePoints(parent, path);
            DeleteTreeWithoutReparse(path);
        }
        catch (Exception ex) when (ex is InstallerException or IOException or UnauthorizedAccessException) { }
    }

    private static void DeleteTreeWithoutReparse(string directory)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Transaction cleanup encountered a reparse point.");
            if ((attributes & FileAttributes.Directory) != 0) DeleteTreeWithoutReparse(entry);
            else File.Delete(entry);
        }
        Directory.Delete(directory);
    }

    private sealed class CallbackScope(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }

    private sealed class Journal
    {
        public int SchemaVersion { get; set; } = 1;
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? LauncherKind { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? LayoutFingerprint { get; set; }
        public string TransactionId { get; set; } = "";
        public string InstanceDirectoryName { get; set; } = "";
        public string PackVersion { get; set; } = "";
        public string PackArchiveSha512 { get; set; } = "";
        public string Operation { get; set; } = "";
        public string Phase { get; set; } = "Preparing";
        public List<JournalEntry> Files { get; set; } = [];
        public string? ManifestSha512 { get; set; }
        public string? ActiveMarkerSha512 { get; set; }
        public bool ActiveMarkerRemoved { get; set; }
        public bool ManifestRemoved { get; set; }
        public string? ActiveMarkerState { get; set; }
        public string? ManifestState { get; set; }
        public string? RollbackFromPhase { get; set; }
        public string? ProfileState { get; set; }
        public ProfileReceiptDocument? ProfileReceipt { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public PrismMetadataReceiptDocument? PrismMetadataReceipt { get; set; }
    }

    private sealed class JournalEntry
    {
        public string Path { get; set; } = "";
        public string? OriginalSha512 { get; set; }
        public long OriginalSize { get; set; }
        public string? ReplacementSha512 { get; set; }
        public bool CreateOnly { get; set; }
        public string State { get; set; } = "Planned";
    }

    private sealed class ProfileReceiptDocument
    {
        public string ProfileFileName { get; set; } = "";
        public string BackupFileName { get; set; } = "";
        public string LauncherRootSha512 { get; set; } = "";
        public string OriginalSha512 { get; set; } = "";
        public string CandidateSha512 { get; set; } = "";
        public bool ChangesProfile { get; set; }

        public static ProfileReceiptDocument FromReceipt(LauncherProfileRemovalReceipt receipt) => new()
        {
            ProfileFileName = receipt.ProfileFileName,
            BackupFileName = receipt.BackupFileName,
            LauncherRootSha512 = receipt.LauncherRootSha512,
            OriginalSha512 = receipt.OriginalSha512,
            CandidateSha512 = receipt.CandidateSha512,
            ChangesProfile = receipt.ChangesProfile
        };

        public LauncherProfileRemovalReceipt ToReceipt() => new(ProfileFileName, BackupFileName,
            LauncherRootSha512, OriginalSha512, CandidateSha512, ChangesProfile);
    }

    private sealed class PrismMetadataReceiptDocument
    {
        public string Operation { get; set; } = "";
        public string LayoutFingerprint { get; set; } = "";
        public List<PrismMetadataEntryDocument> Entries { get; set; } = [];
    }

    private sealed class PrismMetadataEntryDocument
    {
        public string Path { get; set; } = "";
        public string BackupFileName { get; set; } = "";
        public string? StagedFileName { get; set; }
        public string? ResidueFileName { get; set; }
        public string? OriginalSha512 { get; set; }
        public string? ReplacementSha512 { get; set; }
        public string State { get; set; } = "SnapshotIntent";
    }

}
