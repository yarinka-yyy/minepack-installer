using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MinePack.Core;

public sealed class FabricLauncherService : IDisposable
{
    private const int MaxProfileAttempts = 3;
    private const int MaxProfileBytes = 1_000_000;
    private static readonly TimeSpan ProfileBodyDeadline = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(60);
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
    private readonly TimeSpan _profileBodyDeadline;
    private readonly TimeSpan _maxRetryAfter;
    internal string MinecraftVersion => _minecraftVersion;

    public FabricLauncherService(string? launcherRoot = null, HttpMessageHandler? handler = null, string? expectedSha512 = null,
        string? expectedClientJarSha512 = null, long? expectedClientJarSize = null, string? minecraftVersion = null,
        Action? ensureLauncherClosed = null)
        : this(launcherRoot, handler, expectedSha512, expectedClientJarSha512, expectedClientJarSize,
            minecraftVersion, ensureLauncherClosed, ProfileBodyDeadline, MaxRetryAfter)
    {
    }

    internal FabricLauncherService(string? launcherRoot, HttpMessageHandler? handler, string? expectedSha512,
        string? expectedClientJarSha512, long? expectedClientJarSize, string? minecraftVersion,
        Action? ensureLauncherClosed, TimeSpan profileBodyDeadline, TimeSpan maxRetryAfter)
    {
        if (profileBodyDeadline <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(profileBodyDeadline));
        if (maxRetryAfter < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maxRetryAfter));
        _minecraftVersion = minecraftVersion ?? TestPackRelease.MinecraftVersion;
        if (_minecraftVersion is not ("26.2" or "26.3"))
            throw new ArgumentException("Unsupported pinned Minecraft version.", nameof(minecraftVersion));
        var previous = _minecraftVersion == "26.3";
        _versionId = $"fabric-loader-{TestPackRelease.FabricLoaderVersion}-{_minecraftVersion}";
        _profileUrl = $"https://meta.fabricmc.net/v2/versions/loader/{_minecraftVersion}/{TestPackRelease.FabricLoaderVersion}/profile/zip";
        _launcherRoot = launcherRoot ?? DefaultLauncherRoot;
        _expectedSha512 = expectedSha512 ?? (previous ? PreviousProfileSha512 : ProfileSha512);
        _expectedClientJarSha512 = expectedClientJarSha512 ?? (previous ? PreviousMinecraftClientJarSha512 : MinecraftClientJarSha512);
        _expectedClientJarSize = expectedClientJarSize ?? (previous ? PreviousMinecraftClientJarSize : MinecraftClientJarSize);
        _http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(45) };
        _ensureLauncherClosed = ensureLauncherClosed ?? new MinecraftLauncherController().EnsureClosed;
        _profileBodyDeadline = profileBodyDeadline;
        _maxRetryAfter = maxRetryAfter;
    }

    public static string DefaultLauncherRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");

    public string LauncherRoot => _launcherRoot;

    public void CheckReady()
    {
        CheckProfileReady();
        _ensureLauncherClosed();
    }

    public void CheckProfileReady() => _ = FindProfilePath();

    public async Task ConfigureAsync(string gameDirectory, CancellationToken cancellationToken = default,
        OperationLog? operationLog = null)
    {
        var ownsLog = operationLog is null;
        operationLog ??= new OperationLog("fabric_configure", null, gameDirectory);
        operationLog.Write("profile", "started", "fabric_configure_started");
        try
        {
            await OperationGuard.RunAsync(() => ConfigureCoreAsync(gameDirectory, cancellationToken, operationLog), cancellationToken)
                .ConfigureAwait(false);
            if (ownsLog) operationLog.Complete("completed");
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            operationLog.WriteException("profile", "fabric_configure_cancelled", ex, "cancelled");
            if (ownsLog) operationLog.Complete("cancelled", "CANCELLED");
            throw;
        }
        catch (Exception ex)
        {
            operationLog.WriteException("profile", "fabric_configure_failed", ex);
            if (ownsLog) operationLog.Complete("failed", (ex as InstallerException)?.Code);
            throw;
        }
    }

    private async Task ConfigureCoreAsync(string gameDirectory, CancellationToken cancellationToken, OperationLog operationLog,
        InstanceUseGuard.Scope? sharedInstanceUse = null, bool updateProfile = true)
    {
        ManagedFileTransaction.EnsureNoPendingForInstance(gameDirectory);
        using var ownedInstanceUse = sharedInstanceUse is null ? InstanceUseGuard.Acquire(gameDirectory) : null;
        var instanceUse = sharedInstanceUse ?? ownedInstanceUse!;
        try
        {
            var fullGameDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameDirectory));
            var manifestPath = Path.Combine(fullGameDirectory, InstallationManifest.FileName);
            if (File.Exists(manifestPath))
            {
                try
                {
                    var manifest = InstallationManifest.Load(fullGameDirectory);
                    operationLog.SetRelease(manifest.PackVersion, manifest.MinecraftVersion,
                        manifest.FabricLoaderVersion, manifest.PackArchiveSha512);
                }
                catch { }
            }
            var instancesRoot = Path.GetDirectoryName(fullGameDirectory);
            var installRoot = instancesRoot is null ? null : Path.GetDirectoryName(instancesRoot);
            if (installRoot is not null && instancesRoot is not null)
            {
                var validatedRoot = InstallService.ValidateInstallRoot(installRoot);
                if (validatedRoot.Equals(installRoot, StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileName(instancesRoot).Equals("instances", StringComparison.OrdinalIgnoreCase) &&
                    Directory.Exists(installRoot) && Directory.Exists(instancesRoot))
                {
                    SafePath.EnsureNoReparsePoints(installRoot, instancesRoot);
                    SafePath.EnsureNoReparsePoints(installRoot, fullGameDirectory);
                    operationLog.BindValidatedRoot(installRoot, fullGameDirectory);
                }
            }
        }
        catch { }
        operationLog.Write("profile", "preflight", "fabric_profile_preflight_passed");
        CheckReady();
        var profilePath = FindProfilePath();
        var expectedProfileHash = Convert.FromHexString(_expectedSha512);
        var versionsRoot = Path.Combine(_launcherRoot, "versions");
        var versionPath = Path.Combine(versionsRoot, _versionId);
        SafePath.EnsureNoReparsePoints(_launcherRoot, versionPath);
        var createdVersion = false;
        if (Directory.Exists(versionPath) || File.Exists(versionPath))
        {
            var existingJson = Path.Combine(versionPath, _versionId + ".json");
            var existingJar = Path.Combine(versionPath, _versionId + ".jar");
            SafePath.EnsureNoReparsePoints(_launcherRoot, existingJson);
            SafePath.EnsureNoReparsePoints(_launcherRoot, existingJar);
            if (!Directory.Exists(versionPath) || !File.Exists(existingJson) ||
                new FileInfo(existingJson).Length > MaxProfileBytes ||
                !MatchesPinnedProfile(File.ReadAllBytes(existingJson), expectedProfileHash) ||
                !IsExpectedClientJar(existingJar))
                throw new InstallerException("FABRIC_VERSION_CONFLICT", LocalizedText.Get("FabricVersionConflict"));
        }
        else
        {
            var archive = await DownloadPinnedProfileAsync(cancellationToken, operationLog);
            var (versionJson, versionJar) = ReadProfileArchive(archive);
            if (!MatchesPinnedProfile(versionJson, expectedProfileHash))
                throw new InstallerException("FABRIC_HASH", LocalizedText.Get("FabricProfileHashInvalid"));

            instanceUse.Recheck();
            Directory.CreateDirectory(versionsRoot);
            var staging = Path.Combine(versionsRoot, ".minepack-" + Guid.NewGuid().ToString("N"));
            SafePath.EnsureNoReparsePoints(_launcherRoot, staging);
            try
            {
                Directory.CreateDirectory(staging);
                await File.WriteAllBytesAsync(Path.Combine(staging, _versionId + ".json"), versionJson, cancellationToken);
                await File.WriteAllBytesAsync(Path.Combine(staging, _versionId + ".jar"), versionJar, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                instanceUse.Recheck();
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
            instanceUse.Recheck();
            if (updateProfile)
            {
                UpdateProfile(profilePath, json => LauncherProfile.BuildFixtureCandidate(json, gameDirectory,
                    _minecraftVersion, TestPackRelease.FabricLoaderVersion));
                operationLog.Write("profile", "completed", "fabric_profile_configured");
            }
        }
        catch
        {
            if (createdVersion) RemoveCreatedVersion(versionPath);
            throw;
        }
    }

    internal async Task ConfigureAndCommitActivationAsync(string gameDirectory, InstanceUseGuard.Scope instanceUse,
        Func<Task> commitMarker, CancellationToken cancellationToken, OperationLog operationLog)
    {
        await OperationGuard.RunAsync(async () =>
        {
            operationLog.Write("profile", "started", "fabric_activation_profile_started");
            var profilePath = FindProfilePath();
            var original = ReadActivationProfile(profilePath);
            byte[]? candidate = null;
            string? activationBackupPath = null;
            Exception? markerFailure = null;
            try
            {
                await ConfigureCoreAsync(gameDirectory, cancellationToken, operationLog, instanceUse,
                    updateProfile: false).ConfigureAwait(false);
                candidate = Encoding.UTF8.GetBytes(LauncherProfile.BuildFixtureCandidate(
                    Encoding.UTF8.GetString(original), gameDirectory, _minecraftVersion,
                    TestPackRelease.FabricLoaderVersion));
                cancellationToken.ThrowIfCancellationRequested();
                instanceUse.Recheck();
                if (!ReadActivationProfile(profilePath).AsSpan().SequenceEqual(original))
                    throw new InstallerException("LAUNCHER_PROFILE_CHANGED", LocalizedText.Get("LauncherProfileChanged"));
                if (!candidate.AsSpan().SequenceEqual(original))
                {
                    EnsureProfileSpace(checked(2L * original.Length + candidate.Length));
                    activationBackupPath = Path.Combine(_launcherRoot,
                        ".minepack-activation-" + Guid.NewGuid().ToString("N") + ".profile-backup");
                    SafePath.EnsureNoReparsePoints(_launcherRoot, activationBackupPath);
                    WriteBytesDurable(activationBackupPath, original);
                    ManagedFileTransaction.Checkpoint("before-activation-profile-replace");
                    try { ReplaceProfileDurably(profilePath, candidate); }
                    catch (InstallerException) { throw; }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        throw new InstallerException("LAUNCHER_PROFILE_WRITE_FAILED",
                            LocalizedText.Get("LauncherProfileWriteFailed"), ex);
                    }
                    ManagedFileTransaction.Checkpoint("after-activation-profile-replace");
                }

                operationLog.Write("profile", "completed", "fabric_activation_profile_configured");
                cancellationToken.ThrowIfCancellationRequested();
                try { await commitMarker().ConfigureAwait(false); }
                catch (Exception ex) { markerFailure = ex; throw; }
                operationLog.Write("profile", "completed", "fabric_activation_marker_committed");
                TryDeleteActivationBackup(activationBackupPath, operationLog);
            }
            catch (Exception activationFailure)
            {
                if (activationBackupPath is not null && candidate is not null)
                {
                    try
                    {
                        SafePath.EnsureNoReparsePoints(_launcherRoot, activationBackupPath);
                        if (!File.Exists(activationBackupPath) ||
                            !File.ReadAllBytes(activationBackupPath).AsSpan().SequenceEqual(original))
                            throw new IOException("Activation profile backup verification failed.");
                        var current = ReadActivationProfile(profilePath);
                        if (current.AsSpan().SequenceEqual(candidate))
                        {
                            instanceUse.Recheck();
                            ReplaceProfileDurably(profilePath, original);
                            if (!ReadActivationProfile(profilePath).AsSpan().SequenceEqual(original))
                                throw new IOException("Activation profile rollback verification failed.");
                        }
                        else if (!current.AsSpan().SequenceEqual(original))
                            throw new InstallerException("LAUNCHER_PROFILE_CHANGED", LocalizedText.Get("LauncherProfileChanged"));
                        TryDeleteActivationBackup(activationBackupPath, operationLog);
                    }
                    catch (Exception restoreFailure)
                    {
                        throw new InstallerException("LAUNCHER_PROFILE_RECOVERY_REQUIRED",
                            LocalizedText.Get("LauncherProfileRecoveryRequired", activationBackupPath),
                            new AggregateException(activationFailure, restoreFailure));
                    }
                }
                if (markerFailure is not null and not InstallerException)
                    throw new InstallerException("ACTIVE_MARKER_WRITE_FAILED",
                        LocalizedText.Get("ActiveMarkerWriteFailed"), markerFailure);
                throw;
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private byte[] ReadActivationProfile(string profilePath)
    {
        SafePath.EnsureNoReparsePoints(_launcherRoot, profilePath);
        using var stream = new FileStream(profilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length <= 0 || stream.Length > MaxProfileBytes)
            throw new InstallerException("LAUNCHER_PROFILE_UNKNOWN", LocalizedText.Get("LauncherProfileInvalid"));
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private void TryDeleteActivationBackup(string? path, OperationLog operationLog)
    {
        if (path is null) return;
        try
        {
            SafePath.EnsureNoReparsePoints(_launcherRoot, path);
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InstallerException)
        {
            operationLog.WriteException("profile", "activation_profile_backup_cleanup_pending", ex, "warning");
            operationLog.Write("profile", "warning", "activation_profile_backup_path", new { recoveryPath = path });
        }
    }

    private async Task<byte[]> DownloadPinnedProfileAsync(CancellationToken cancellationToken, OperationLog operationLog)
    {
        InstallerException? lastFailure = null;
        for (var attempt = 0; attempt < MaxProfileAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                operationLog.Write("profile", "progress", "fabric_profile_download_attempt", new { attempt = attempt + 1, origin = "meta.fabricmc.net" });
                using var response = await _http.GetAsync(_profileUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                operationLog.Write("profile", response.IsSuccessStatusCode ? "response" : "failed",
                    "fabric_profile_download_response", new { attempt = attempt + 1, statusCode = (int)response.StatusCode });
                if (IsRetryableStatus(response.StatusCode))
                {
                    lastFailure = new InstallerException("FABRIC_DOWNLOAD", LocalizedText.Get("PinnedFabricProfileUnavailable"));
                    if (attempt + 1 == MaxProfileAttempts) break;
                    await Task.Delay(RetryAfter(response) ?? TimeSpan.FromMilliseconds(250 * (1 << attempt)), cancellationToken);
                    continue;
                }
                if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength is > MaxProfileBytes)
                    throw new InstallerException("FABRIC_DOWNLOAD", LocalizedText.Get("PinnedFabricProfileUnavailable"));

                return await ReadBoundedProfileBodyAsync(response, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException ex)
            {
                lastFailure = new InstallerException("FABRIC_TIMEOUT", LocalizedText.Get("FabricTimeout"), ex);
            }
            catch (HttpRequestException ex)
            {
                lastFailure = new InstallerException("FABRIC_NETWORK", LocalizedText.Get("FabricNetworkFailed"), ex);
            }

            if (attempt + 1 < MaxProfileAttempts)
                await Task.Delay(TimeSpan.FromMilliseconds(250 * (1 << attempt)), cancellationToken);
        }
        throw lastFailure ?? new InstallerException("FABRIC_DOWNLOAD", LocalizedText.Get("PinnedFabricProfileUnavailable"));
    }

    private async Task<byte[]> ReadBoundedProfileBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_profileBodyDeadline);
        await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var remaining = MaxProfileBytes + 1 - (int)output.Length;
            var count = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)), deadline.Token);
            if (count == 0) return output.ToArray();
            if (output.Length + count > MaxProfileBytes)
                throw new InstallerException("FABRIC_DOWNLOAD", LocalizedText.Get("PinnedFabricProfileTooLarge"));
            await output.WriteAsync(buffer.AsMemory(0, count), deadline.Token);
        }
    }

    private static bool IsRetryableStatus(HttpStatusCode status) => status is HttpStatusCode.RequestTimeout or
        HttpStatusCode.TooManyRequests || (int)status >= 500;

    private TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var retry = response.Headers.RetryAfter;
        return ClampRetryAfter(retry?.Delta, retry?.Date, DateTimeOffset.UtcNow, _maxRetryAfter);
    }

    internal static TimeSpan? ClampRetryAfter(TimeSpan? delta, DateTimeOffset? date, DateTimeOffset now, TimeSpan maximum)
    {
        TimeSpan? delay = delta;
        if (delay is null && date is { } retryDate) delay = retryDate - now;
        if (delay is null) return null;
        if (delay < TimeSpan.Zero) return TimeSpan.Zero;
        return delay > maximum ? maximum : delay;
    }

    private bool IsExpectedClientJar(string path)
    {
        if (!File.Exists(path)) return false;
        using var stream = File.OpenRead(path);
        if (stream.Length == 0) return true;
        return stream.Length == _expectedClientJarSize && CryptographicOperations.FixedTimeEquals(
            SHA512.HashData(stream), Convert.FromHexString(_expectedClientJarSha512));
    }

    public void RemoveOwnProfile(string expectedGameDirectory)
        => OperationGuard.Run(() => RemoveOwnProfileCore(expectedGameDirectory));

    internal LauncherProfileRemovalReceipt? PrepareOwnProfileRemoval(string expectedGameDirectory, string transactionId,
        PackArchive pack, InstallationManifest manifest)
    {
        InstallService.ValidateMatchesRelease(manifest, pack);
        if (!Guid.TryParseExact(transactionId, "N", out _)) throw new ArgumentException("Invalid transaction id.", nameof(transactionId));
        _ensureLauncherClosed();
        if (!Directory.Exists(_launcherRoot) ||
            !new[] { "launcher_profiles.json", "launcher_profiles_microsoft_store.json" }
                .Any(name => File.Exists(Path.Combine(_launcherRoot, name))))
            return null;
        var profilePath = FindProfilePath();
        var original = File.ReadAllBytes(profilePath);
        var candidate = Encoding.UTF8.GetBytes(LauncherProfile.RemoveOwnedProfile(
            Encoding.UTF8.GetString(original), expectedGameDirectory, manifest, pack));
        return new LauncherProfileRemovalReceipt(Path.GetFileName(profilePath), $".minepack-{transactionId}.profile-backup",
            GetLauncherRootSha512(), HashBytes(original), HashBytes(candidate), !original.AsSpan().SequenceEqual(candidate))
        { OriginalBytes = original, CandidateBytes = candidate };
    }

    internal void WriteOwnProfileRemovalBackup(LauncherProfileRemovalReceipt receipt)
    {
        ValidateRemovalReceipt(receipt);
        if (receipt.OriginalBytes.Length == 0 || !HashEquals(HashBytes(receipt.OriginalBytes), receipt.OriginalSha512) ||
            (receipt.ChangesProfile && (receipt.CandidateBytes.Length == 0 ||
                !HashEquals(HashBytes(receipt.CandidateBytes), receipt.CandidateSha512))))
            throw ProfileRecoveryRequired(receipt.BackupFileName);
        ManagedFileTransaction.Checkpoint("before-profile-backup");
        var backup = ProfileBackupPath(receipt);
        if (File.Exists(backup))
        {
            if (!HashEquals(HashFile(backup), receipt.OriginalSha512)) throw ProfileRecoveryRequired(receipt.BackupFileName);
            return;
        }
        EnsureProfileSpace(receipt.ChangesProfile
            ? checked(2L * receipt.OriginalBytes.Length + receipt.CandidateBytes.Length)
            : receipt.OriginalBytes.Length);
        WriteBytesDurable(backup, receipt.OriginalBytes);
    }

    internal void CommitOwnProfileRemoval(string expectedGameDirectory, LauncherProfileRemovalReceipt receipt,
        PackArchive pack, InstallationManifest manifest)
    {
        InstallService.ValidateMatchesRelease(manifest, pack);
        ValidateRemovalReceipt(receipt);
        _ensureLauncherClosed();
        var profilePath = ProfilePath(receipt);
        var original = ReadOwnProfileBackup(receipt);
        var candidate = MakeRemovalCandidate(original, expectedGameDirectory, receipt, pack, manifest);
        if (receipt.ChangesProfile) EnsureProfileSpace(checked((long)original.Length + candidate.Length));
        ManagedFileTransaction.Checkpoint("before-profile-read");
        var current = File.ReadAllBytes(profilePath);
        if (!current.AsSpan().SequenceEqual(original))
            throw new InstallerException("LAUNCHER_PROFILE_CHANGED", LocalizedText.Get("LauncherProfileChanged"));
        if (receipt.ChangesProfile)
        {
            ManagedFileTransaction.Checkpoint("before-profile-replace");
            ReplaceProfileDurably(profilePath, candidate);
            ManagedFileTransaction.Checkpoint("after-profile-replace");
        }
        if (!File.ReadAllBytes(profilePath).AsSpan().SequenceEqual(candidate))
            throw ProfileRecoveryRequired(receipt.BackupFileName);
    }

    internal void RestoreOwnProfileRemoval(string expectedGameDirectory, LauncherProfileRemovalReceipt receipt,
        PackArchive pack, InstallationManifest manifest)
    {
        InstallService.ValidateMatchesRelease(manifest, pack);
        ValidateRemovalReceipt(receipt);
        _ensureLauncherClosed();
        var profilePath = ProfilePath(receipt);
        var original = ReadOwnProfileBackup(receipt);
        var candidate = MakeRemovalCandidate(original, expectedGameDirectory, receipt, pack, manifest);
        var current = File.ReadAllBytes(profilePath);
        if (current.AsSpan().SequenceEqual(original)) return;
        if (!current.AsSpan().SequenceEqual(candidate)) throw ProfileRecoveryRequired(receipt.BackupFileName);
        EnsureProfileSpace(checked((long)original.Length + candidate.Length));
        ReplaceProfileDurably(profilePath, original);
        if (!File.ReadAllBytes(profilePath).AsSpan().SequenceEqual(original))
            throw ProfileRecoveryRequired(receipt.BackupFileName);
    }

    internal void ValidateAndCleanupOwnProfileBackup(string expectedGameDirectory, LauncherProfileRemovalReceipt receipt,
        bool expectCandidate, PackArchive pack, InstallationManifest manifest)
    {
        VerifyOwnProfileRemovalState(expectedGameDirectory, receipt, expectCandidate, pack, manifest);
        var backup = ProfileBackupPath(receipt);
        if (File.Exists(backup)) File.Delete(backup);
    }

    internal void VerifyOwnProfileRemovalState(string expectedGameDirectory, LauncherProfileRemovalReceipt receipt,
        bool expectCandidate, PackArchive pack, InstallationManifest manifest, bool requireBackup = false)
    {
        InstallService.ValidateMatchesRelease(manifest, pack);
        ValidateRemovalReceipt(receipt);
        var profilePath = ProfilePath(receipt);
        var backup = ProfileBackupPath(receipt);
        var current = File.ReadAllBytes(profilePath);
        var expectedCurrentHash = expectCandidate ? receipt.CandidateSha512 : receipt.OriginalSha512;
        if (!HashEquals(HashBytes(current), expectedCurrentHash))
            throw ProfileRecoveryRequired(receipt.BackupFileName);
        if (!File.Exists(backup))
        {
            if (requireBackup) throw ProfileRecoveryRequired(receipt.BackupFileName);
            return;
        }
        var original = ReadOwnProfileBackup(receipt);
        var candidate = MakeRemovalCandidate(original, expectedGameDirectory, receipt, pack, manifest);
        var expectedBytes = expectCandidate ? candidate : original;
        if (!current.AsSpan().SequenceEqual(expectedBytes)) throw ProfileRecoveryRequired(receipt.BackupFileName);
    }

    internal string GetOwnProfileRemovalBackupPath(LauncherProfileRemovalReceipt receipt)
    {
        ValidateRemovalReceipt(receipt);
        return ProfileBackupPath(receipt);
    }

    private byte[] MakeRemovalCandidate(byte[] original, string expectedGameDirectory,
        LauncherProfileRemovalReceipt receipt, PackArchive pack, InstallationManifest manifest)
    {
        if (!HashEquals(HashBytes(original), receipt.OriginalSha512)) throw ProfileRecoveryRequired(receipt.BackupFileName);
        var candidate = Encoding.UTF8.GetBytes(LauncherProfile.RemoveOwnedProfile(
            Encoding.UTF8.GetString(original), expectedGameDirectory, manifest, pack));
        if (!HashEquals(HashBytes(candidate), receipt.CandidateSha512)) throw ProfileRecoveryRequired(receipt.BackupFileName);
        return candidate;
    }

    private byte[] ReadOwnProfileBackup(LauncherProfileRemovalReceipt receipt)
    {
        var backup = ProfileBackupPath(receipt);
        if (!File.Exists(backup)) throw ProfileRecoveryRequired(receipt.BackupFileName);
        var bytes = File.ReadAllBytes(backup);
        if (!HashEquals(HashBytes(bytes), receipt.OriginalSha512)) throw ProfileRecoveryRequired(receipt.BackupFileName);
        return bytes;
    }

    private string ProfilePath(LauncherProfileRemovalReceipt receipt)
    {
        ValidateRemovalReceipt(receipt);
        var path = Path.Combine(_launcherRoot, receipt.ProfileFileName);
        SafePath.EnsureNoReparsePoints(_launcherRoot, path);
        if (!File.Exists(path)) throw ProfileRecoveryRequired(receipt.ProfileFileName);
        return path;
    }

    private string ProfileBackupPath(LauncherProfileRemovalReceipt receipt)
    {
        var path = Path.Combine(_launcherRoot, receipt.BackupFileName);
        SafePath.EnsureNoReparsePoints(_launcherRoot, path);
        return path;
    }

    private void ValidateRemovalReceipt(LauncherProfileRemovalReceipt receipt)
    {
        if (!new[] { "launcher_profiles.json", "launcher_profiles_microsoft_store.json" }.Contains(receipt.ProfileFileName, StringComparer.Ordinal) ||
            !receipt.BackupFileName.StartsWith(".minepack-", StringComparison.Ordinal) ||
            !receipt.BackupFileName.EndsWith(".profile-backup", StringComparison.Ordinal) ||
            receipt.BackupFileName.Length != 57 ||
            !Guid.TryParseExact(receipt.BackupFileName[10..^15], "N", out _) ||
            !PackArchive.IsSha512(receipt.LauncherRootSha512) ||
            !PackArchive.IsSha512(receipt.OriginalSha512) || !PackArchive.IsSha512(receipt.CandidateSha512) ||
            !HashEquals(GetLauncherRootSha512(), receipt.LauncherRootSha512))
            throw ProfileRecoveryRequired(receipt.BackupFileName);
    }

    private string GetLauncherRootSha512()
    {
        var canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_launcherRoot)).ToUpperInvariant();
        return HashBytes(Encoding.UTF8.GetBytes(canonical));
    }

    private static void ReplaceProfileDurably(string profilePath, byte[] bytes)
    {
        var directory = Path.GetDirectoryName(profilePath)!;
        var temp = Path.Combine(directory, ".minepack-profile-" + Guid.NewGuid().ToString("N") + ".tmp");
        var backup = Path.Combine(directory, ".minepack-profile-" + Guid.NewGuid().ToString("N") + ".bak");
        SafePath.EnsureNoReparsePoints(directory, temp);
        SafePath.EnsureNoReparsePoints(directory, backup);
        try
        {
            WriteBytesDurable(temp, bytes);
            File.Replace(temp, profilePath, backup);
            try
            {
                if (!File.ReadAllBytes(profilePath).AsSpan().SequenceEqual(bytes))
                    throw new IOException("Launcher profile replacement verification failed.");
                File.Delete(backup);
            }
            catch
            {
                if (File.Exists(backup)) File.Replace(backup, profilePath, null);
                throw;
            }
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private static void WriteBytesDurable(string path, byte[] bytes)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private void EnsureProfileSpace(long requiredBytes)
    {
        var driveRoot = Path.GetPathRoot(Path.GetFullPath(_launcherRoot))
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

    private static string HashBytes(byte[] bytes) => Convert.ToHexString(SHA512.HashData(bytes));
    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA512.HashData(stream));
    }
    private static bool HashEquals(string first, string second) => PackArchive.FixedTimeHashEquals(first, second);
    private static InstallerException ProfileRecoveryRequired(string path) =>
        new("TRANSACTION_RECOVERY_REQUIRED", LocalizedText.Get("TransactionRecoveryRequired", path));

    private void RemoveOwnProfileCore(string expectedGameDirectory)
    {
        ManagedFileTransaction.EnsureNoPendingForInstance(expectedGameDirectory);
        using var instanceUse = InstanceUseGuard.Acquire(expectedGameDirectory);
        if (!Directory.Exists(_launcherRoot) ||
            !new[] { "launcher_profiles.json", "launcher_profiles_microsoft_store.json" }
                .Any(name => File.Exists(Path.Combine(_launcherRoot, name)))) return;
        var profilePath = FindProfilePath();
        _ensureLauncherClosed();
        instanceUse.Recheck();
        UpdateProfile(profilePath, json => LauncherProfile.RemoveOwnedProfile(json, expectedGameDirectory));
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
