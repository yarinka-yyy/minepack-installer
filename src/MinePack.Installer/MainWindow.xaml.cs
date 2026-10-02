using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;
using System.Windows.Threading;
using MinePack.Core;
using Microsoft.Win32;

namespace MinePack.Installer;

public partial class MainWindow : Window
{
    private readonly InstallService _installer = new();
    private readonly MinecraftLauncherController _launcherController;
    private readonly FabricLauncherService _launcher;
    private CancellationTokenSource? _operationCancellation;
    private IInputElement? _uninstallReturnFocus;
    private bool _operationCanBeCancelled = true;
    private string? _gameDirectory;
    private readonly string _installerVersion;
    private OperationLog? _currentOperationLog;
    private IReadOnlyList<InstanceChoice> _instanceChoices = [];
    private string? _selectedInstancePath;
    private bool _changingInstanceSelection;
    private ActiveMarkerInspection? _activeMarker;
    private string? _missingSavedRootPath;

    public MainWindow()
    {
        InitializeComponent();
        _launcherController = new MinecraftLauncherController();
        _launcher = new FabricLauncherService(ensureLauncherClosed: _launcherController.EnsureClosed);
        VanillaPlusOption.Checked += PackChoice_Changed;
        Vanilla2PlusOption.Checked += PackChoice_Changed;
        var assembly = typeof(MainWindow).Assembly;
        _installerVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString(3)
            ?? "0.0.0";
        InstallerVersionText.Text = LocalizedText.Get("UiInstallerVersion", _installerVersion);
        UpdatePackSelection();
        var preferences = InstallerPreferences.Load(InstallerPreferences.DefaultPath);
        InstallRootBox.Text = preferences.LastValidatedRoot ?? InstallService.DefaultInstallRoot;
        if (preferences.LastValidatedRoot is not null && !Directory.Exists(preferences.LastValidatedRoot))
            _missingSavedRootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(preferences.LastValidatedRoot));
        if (preferences.IsCorrupt) StatusBox.Text = LocalizedText.Get("UiPreferencesCorrupt");
        var root = RefreshRootState(persist: false);
        if (root is null && !preferences.IsCorrupt) StatusBox.Text = LocalizedText.Get("UiSavedRootUnavailable");
    }

    private static string ReleasePath(string relativePath) => Path.Combine(AppContext.BaseDirectory,
        relativePath.Replace('/', Path.DirectorySeparatorChar));

    private string VanillaPlusPackPath => ReleasePath(TestPackRelease.ArtifactRelativePath);

    private string Vanilla2PlusPackPath => ReleasePath(Vanilla2PlusRelease.ArtifactRelativePath);

    private (string Path, string Hash, string Version) SelectedPack => Vanilla2PlusOption.IsChecked == true
        ? (Vanilla2PlusPackPath, Vanilla2PlusRelease.ArtifactSha512, Vanilla2PlusRelease.PackVersion)
        : (VanillaPlusPackPath, TestPackRelease.ArtifactSha512, TestPackRelease.PackVersion);

    private void PackChoice_Changed(object sender, RoutedEventArgs e) => UpdatePackSelection();

    private void UpdatePackSelection()
    {
        var vanilla2Plus = Vanilla2PlusOption.IsChecked == true;
        CatalogList.ItemsSource = vanilla2Plus ? PackCatalog.Vanilla2PlusGroups : PackCatalog.VanillaPlusGroups;
        PackVersionText.Text = LocalizedText.Get("UiPackVersion",
            vanilla2Plus ? Vanilla2PlusRelease.PackVersion : TestPackRelease.PackVersion);
        PackCountsText.Text = LocalizedText.Get(vanilla2Plus ? "UiPackCountsVanilla2Plus" : "UiPackCountsVanillaPlus");
    }

    private async void Install_Click(object sender, RoutedEventArgs e) => await RunOperationAsync(Operation.Install);

    private async void Repair_Click(object sender, RoutedEventArgs e) => await RunOperationAsync(Operation.Repair);

    private async void Configure_Click(object sender, RoutedEventArgs e) => await RunOperationAsync(Operation.ConfigureLauncher);

    private async void ImportWorlds_Click(object sender, RoutedEventArgs e)
    {
        var root = RefreshRootState(persist: true);
        if (root is null) return;
        var target = ResolveImportTarget(root);
        if (target is null)
        {
            StatusBox.Text = LocalizedText.Get("UiSelectTrustedInstance");
            return;
        }
        var vanillaSaves = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft", "saves");
        var picker = new OpenFolderDialog
        {
            Title = LocalizedText.Get("UiWorldFolderDialogTarget", target.Release?.PackName ?? LocalizedText.Get("UiInstalledInstance"),
                target.Release?.PackVersion ?? Path.GetFileName(target.Path)),
            InitialDirectory = Directory.Exists(vanillaSaves) ? vanillaSaves : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
        };
        if (picker.ShowDialog(this) == true)
            await RunOperationAsync(Operation.ImportWorlds, picker.FolderName);
    }

    private void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        var root = RefreshRootState(persist: true);
        var selected = GetSelectedEntry();
        if (root is null || selected is not { IsTrusted: true, Release: not null })
        {
            StatusBox.Text = LocalizedText.Get("UiSelectTrustedInstance");
            return;
        }
        var pending = _installer.GetPendingOperation(root);
        if (pending is not null && !SamePath(pending.Value.InstancePath, selected.Path))
        {
            StatusBox.Text = LocalizedText.Get("TransactionRecoveryRequired", pending.Value.InstancePath);
            return;
        }
        UninstallConfirmDetails.Text = LocalizedText.Get("UiUninstallConfirmTarget", selected.Release.PackName,
            selected.Release.PackVersion, selected.Path);
        _uninstallReturnFocus = UninstallButton;
        MainContentScrollViewer.IsEnabled = false;
        UninstallConfirmOverlay.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => UninstallCancelButton.Focus()));
    }

    private void UninstallConfirmOverlay_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        CloseUninstallConfirmation();
    }

    private void UninstallCancel_Click(object sender, RoutedEventArgs e) => CloseUninstallConfirmation();

    private async void UninstallConfirm_Click(object sender, RoutedEventArgs e)
    {
        CloseUninstallConfirmation();
        await RunOperationAsync(Operation.Uninstall);
    }

    private void CloseUninstallConfirmation()
    {
        UninstallConfirmOverlay.Visibility = Visibility.Collapsed;
        MainContentScrollViewer.IsEnabled = true;
        var returnFocus = _uninstallReturnFocus ?? UninstallButton;
        _uninstallReturnFocus = null;
        Keyboard.Focus(returnFocus);
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog
        {
            Title = LocalizedText.Get("UiInstallFolderDialog"),
            InitialDirectory = Directory.Exists(InstallRootBox.Text) ? InstallRootBox.Text : InstallService.DefaultInstallRoot
        };
        if (picker.ShowDialog(this) == true)
        {
            InstallRootBox.Text = picker.FolderName;
            _ = RefreshRootState(persist: true);
        }
    }

    private void InstallRootBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _selectedInstancePath = null;
        _instanceChoices = [];
        _activeMarker = null;
        _gameDirectory = null;
        _currentOperationLog = null;
        InstalledInstanceComboBox.ItemsSource = null;
        InstalledInstanceStatusText.Text = LocalizedText.Get("UiRootChangedSelectInstance");
        RootAvailabilityText.Text = LocalizedText.Get("UiRootNeedsValidation");
    }

    private void InstallRootBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        _ = RefreshRootState(persist: true);

    private void InstalledInstanceComboBox_SelectionChanged(object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_changingInstanceSelection) return;
        var selected = GetSelectedEntry();
        _selectedInstancePath = selected?.Path;
        UpdateSelectedInstanceStatus();
    }

    private string? RefreshRootState(bool persist)
    {
        try
        {
            var root = Path.TrimEndingDirectorySeparator(InstallService.ValidateInstallRoot(InstallRootBox.Text));
            var volumeRoot = Path.GetPathRoot(root);
            if (volumeRoot is null || !Directory.Exists(volumeRoot))
                throw new InstallerException("ROOT_UNAVAILABLE", LocalizedText.Get("UiSavedRootUnavailable"));
            if (!Directory.Exists(root) && _missingSavedRootPath is not null && SamePath(root, _missingSavedRootPath))
                throw new InstallerException("ROOT_UNAVAILABLE", LocalizedText.Get("UiSavedRootUnavailable"));

            if (persist)
            {
                try
                {
                    InstallerPreferences.SaveLastValidatedRoot(InstallerPreferences.DefaultPath, root);
                    _missingSavedRootPath = null;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InstallerException)
                {
                    StatusBox.Text = LocalizedText.Get("UiPreferencesNotSaved");
                }
            }

            _activeMarker = null;
            var pending = _installer.GetPendingOperation(root);
            if (Directory.Exists(root) && pending is null) _activeMarker = _installer.InspectActiveMarker(root);
            _instanceChoices = Directory.Exists(root)
                ? InstalledInstanceCatalog.Enumerate(root, AppContext.BaseDirectory)
                    .Select(entry => new InstanceChoice(entry, FormatInstanceChoice(entry)))
                    .ToArray()
                : [];
            _changingInstanceSelection = true;
            InstalledInstanceComboBox.ItemsSource = _instanceChoices;
            RootAvailabilityText.Text = pending is not null
                ? LocalizedText.Get("UiPendingRecoveryAt", pending.Value.InstancePath)
                : Directory.Exists(root) ? LocalizedText.Get("UiRootAvailable", root) : LocalizedText.Get("UiRootReady", root);
            if (_activeMarker is { State: not (ActiveMarkerState.Absent or ActiveMarkerState.Valid) } markerState)
                RootAvailabilityText.Text += Environment.NewLine + LocalizedText.Get("UiActiveMarkerStatus",
                    LocalizedText.Get(markerState.State switch
                    {
                        ActiveMarkerState.Malformed => "UiMarkerMalformed",
                        ActiveMarkerState.MissingTarget => "UiMarkerTargetMissing",
                        ActiveMarkerState.UnsupportedSchema => "ActiveMarkerUnsupported",
                        _ => "ActiveMarkerUnsafe"
                    }));

            var preferredPath = _selectedInstancePath;
            var activePath = _activeMarker?.InstancePath;
            var choice = _instanceChoices.FirstOrDefault(item => SamePath(item.Entry.Path, preferredPath)) ??
                _instanceChoices.FirstOrDefault(item => SamePath(item.Entry.Path, activePath)) ??
                _instanceChoices.FirstOrDefault(item => item.Entry.IsTrusted) ?? _instanceChoices.FirstOrDefault(item => item.Entry.IsOpenable);
            InstalledInstanceComboBox.SelectedItem = choice;
            _changingInstanceSelection = false;
            _selectedInstancePath = choice?.Entry.Path;
            UpdateSelectedInstanceStatus();
            return root;
        }
        catch (InstallerException ex)
        {
            _instanceChoices = [];
            _activeMarker = null;
            _selectedInstancePath = null;
            InstalledInstanceComboBox.ItemsSource = null;
            InstalledInstanceStatusText.Text = LocalizedText.Get("UiNoTrustedInstances");
            RootAvailabilityText.Text = ex.Code == "ROOT_UNSAFE" || ex.Code == "VANILLA_PATH_BLOCKED"
                ? ex.Message : LocalizedText.Get("UiSavedRootUnavailable");
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _instanceChoices = [];
            _activeMarker = null;
            _selectedInstancePath = null;
            InstalledInstanceComboBox.ItemsSource = null;
            InstalledInstanceStatusText.Text = LocalizedText.Get("UiNoTrustedInstances");
            RootAvailabilityText.Text = LocalizedText.Get("UiSavedRootUnavailable");
            return null;
        }
    }

    private string FormatInstanceChoice(InstalledInstanceEntry entry)
    {
        var release = entry.Release;
        var name = release is null ? Path.GetFileName(entry.Path) : $"{release.PackName} {release.PackVersion}";
        var state = SamePath(entry.Path, _activeMarker?.InstancePath)
            ? LocalizedText.Get("UiInstanceActive") : LocalizedText.Get("UiInstanceInactive");
        state += " · " + LocalizedText.Get(entry.State switch
        {
            InstalledInstanceState.Trusted => "UiInstanceTrusted",
            InstalledInstanceState.PackageMissing => "UiInstancePackageMissing",
            InstalledInstanceState.Residue => "UiInstanceResidue",
            InstalledInstanceState.UnknownRelease => "UiInstanceUnknown",
            InstalledInstanceState.Invalid => "UiInstanceInvalid",
            _ => "UiInstanceUnsafe"
        });
        return $"{name} — {state}";
    }

    private void UpdateSelectedInstanceStatus()
    {
        var selected = GetSelectedEntry();
        if (selected is null)
        {
            InstalledInstanceStatusText.Text = LocalizedText.Get("UiNoTrustedInstances");
            return;
        }
        var releaseLabel = selected.Release is null
            ? Path.GetFileName(selected.Path)
            : $"{selected.Release.PackName} {selected.Release.PackVersion}";
        InstalledInstanceStatusText.Text = LocalizedText.Get("UiSelectedInstanceStatus", releaseLabel, selected.Path,
            SamePath(selected.Path, _activeMarker?.InstancePath)
                ? LocalizedText.Get("UiInstanceActive") : LocalizedText.Get("UiInstanceInactive"));
    }

    private InstalledInstanceEntry? GetSelectedEntry() =>
        (InstalledInstanceComboBox.SelectedItem as InstanceChoice)?.Entry;

    private InstalledInstanceEntry RequireTrustedSelected(string root)
    {
        var selected = GetSelectedEntry();
        if (selected is not { IsTrusted: true } || !IsUnderSelectedRoot(root, selected.Path))
            throw new InstallerException("INSTANCE_UNTRUSTED", LocalizedText.Get("UiSelectTrustedInstance"));
        return selected;
    }

    private bool ConfirmMarkerRecovery(string root, ActiveMarkerInspection marker)
    {
        if (marker.State == ActiveMarkerState.UnsupportedSchema)
            throw new InstallerException("ACTIVE_MARKER_UNSUPPORTED", LocalizedText.Get("ActiveMarkerUnsupported"));
        if (marker.State == ActiveMarkerState.UnsafePath)
            throw new InstallerException("ACTIVE_MARKER_UNSAFE", LocalizedText.Get("ActiveMarkerUnsafe"));
        if (marker.State is not (ActiveMarkerState.Malformed or ActiveMarkerState.MissingTarget)) return false;
        var hasTrustedInstance = InstalledInstanceCatalog.Enumerate(root, AppContext.BaseDirectory).Any(entry => entry.IsTrusted);
        var confirmation = hasTrustedInstance
            ? LocalizedText.Get("UiActiveMarkerRecoveryConfirm", root,
                LocalizedText.Get(marker.State == ActiveMarkerState.Malformed ? "UiMarkerMalformed" : "UiMarkerTargetMissing"))
            : LocalizedText.Get("UiEmptyRootMarkerRecoveryConfirm", root);
        return MessageBox.Show(this, confirmation,
            LocalizedText.Get("UiActiveMarkerRecoveryTitle"), MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    private InstalledInstanceEntry? ResolveImportTarget(string root)
    {
        var selected = GetSelectedEntry();
        if (selected is { IsTrusted: true } && IsUnderSelectedRoot(root, selected.Path)) return selected;
        var activePath = _installer.GetActiveInstancePath(root);
        return _instanceChoices.Select(item => item.Entry)
            .FirstOrDefault(entry => entry.IsTrusted && SamePath(entry.Path, activePath));
    }

    private static bool IsUnderSelectedRoot(string root, string path) =>
        Path.GetDirectoryName(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)))) is { } parent &&
        SamePath(parent, root);

    private static bool SamePath(string? first, string? second) => first is not null && second is not null &&
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)).Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)), StringComparison.OrdinalIgnoreCase);

    private async Task RunOperationAsync(Operation operation, string? sourceWorlds = null)
    {
        if (_operationCancellation is not null) return;
        var cancellation = new CancellationTokenSource();
        _operationCancellation = cancellation;
        _operationCanBeCancelled = operation != Operation.Uninstall;
        var selectedPackForLog = SelectedPack;
        var installedReleaseForLog = operation == Operation.Install ? null : GetSelectedEntry()?.Release;
        var operationLog = new OperationLog(operation.ToString().ToLowerInvariant(), _installerVersion,
            InstallRootBox.Text, installedReleaseForLog?.PackVersion ?? selectedPackForLog.Version,
            installedReleaseForLog?.MinecraftVersion ?? TestPackRelease.MinecraftVersion,
            installedReleaseForLog?.FabricLoaderVersion ?? TestPackRelease.FabricLoaderVersion,
            installedReleaseForLog?.ArchiveSha512 ?? selectedPackForLog.Hash);
        _currentOperationLog = operationLog;
        SetBusy(true);
        var filesInstalled = false;
        var filesRemoved = false;
        var operationOutcome = "failed";
        string? operationFailureCode = null;
        MinecraftLauncherTarget? launcherTarget = null;
        OperationProgress.Value = 0;
        OperationProgress.IsIndeterminate = true;
        InstructionsBox.Text = "";
        DiagnosticText.Text = "";
        StatusBox.Text = LocalizedText.Get("OperationWait");
        StateHeading.Text = LocalizedText.Get(operation == Operation.Install ? "OperationInstalling" : "OperationWorking");
        ProgressLabel.Text = operation switch
        {
            Operation.Install => LocalizedText.Get("PreparingFiles"),
            Operation.Repair => LocalizedText.Get("SearchingPack"),
            Operation.ConfigureLauncher => LocalizedText.Get("RestoringProfile"),
            Operation.ImportWorlds => LocalizedText.Get("CopyingWorlds"),
            _ => LocalizedText.Get("PreparingUninstall")
        };

        try
        {
            operationLog.Write("preflight", "started", "ui_operation_started");
            await OperationGuard.RunAsync(async () =>
            {
            var root = RefreshRootState(persist: true)
                ?? throw new InstallerException("ROOT_UNAVAILABLE", LocalizedText.Get("UiSavedRootUnavailable"));
            var selectedPack = SelectedPack;
            if (operation == Operation.Install && !File.Exists(selectedPack.Path))
                throw new InstallerException("PACK_NOT_FOUND", LocalizedText.Get("PublishedPackMissing"));

            if (operation == Operation.ImportWorlds)
            {
                var importTarget = ResolveImportTarget(root)
                    ?? throw new InstallerException("INSTANCE_UNTRUSTED", LocalizedText.Get("UiSelectTrustedInstance"));
                var worldProgress = new Progress<string>(message => ProgressLabel.Text = message);
                var imported = await Task.Run(async () =>
                {
                    return await WorldImportService.ImportAsync(sourceWorlds!, importTarget.Path, worldProgress, cancellation.Token, operationLog);
                }, cancellation.Token);
                StateHeading.Text = LocalizedText.Get("WorldImportComplete");
                StatusBox.Text = FormatWorldImportSummary(imported);
                InstructionsBox.Text = LocalizedText.Get("UiWorldImportSafetyTarget", importTarget.Release!.PackName,
                    importTarget.Release.PackVersion, importTarget.Path);
                ProgressLabel.Text = LocalizedText.Get("OperationComplete");
                operationOutcome = "completed";
                return;
            }

            var lastByteUpdate = 0L;
            var fileProgressPhase = "files";
            var progressProjector = new InstallProgressProjector();
            var progress = new Progress<InstallProgress>(item =>
            {
                if (item.Stage == "download" && item.BytesReceived > 0)
                {
                    var now = Stopwatch.GetTimestamp();
                    if (now - lastByteUpdate < Stopwatch.Frequency / 10) return;
                    lastByteUpdate = now;
                }
                var phase = item.Stage is "download" or "override" or "prepare" or "repair" or "complete"
                    ? fileProgressPhase : item.Stage;
                var view = progressProjector.Update(phase, item);
                OperationProgress.IsIndeterminate = view.IsIndeterminate;
                OperationProgress.Maximum = Math.Max(1, view.TotalFiles);
                if (!view.IsIndeterminate) OperationProgress.Value = view.CompletedFiles;
                ProgressLabel.Text = item.ExpectedBytes is > 0 && item.BytesReceived > 0
                    ? item.Message + " " + LocalizedText.Get("ProgressBytes", item.BytesReceived, item.ExpectedBytes.Value)
                    : item.Message;
            });

            if (operation == Operation.ConfigureLauncher)
            {
                var target = RequireTrustedSelected(root);
                var release = target.Release!;
                var (archivePath, archiveHash) = InstalledInstanceCatalog.PinnedArchive(release.PackVersion, AppContext.BaseDirectory);
                if (!File.Exists(archivePath))
                    throw new InstallerException("PACK_NOT_FOUND", LocalizedText.Get("SelectedPackUnavailable"));

                var pending = _installer.GetPendingOperation(root);
                if (pending is not null)
                {
                    var selectedPath = target.Path;
                    fileProgressPhase = "pending-recovery";
                    OperationProgress.Value = 0;
                    OperationProgress.IsIndeterminate = true;
                    ProgressLabel.Text = LocalizedText.Get("SearchingPack");
                    var (pendingArchive, pendingHash) = InstalledInstanceCatalog.PinnedArchive(pending.Value.PackVersion, AppContext.BaseDirectory);
                    var recovery = await Task.Run(() => _installer.RepairAsync(pending.Value.InstancePath,
                        pendingArchive, pendingHash, progress, cancellation.Token, _launcher, operationLog), cancellation.Token);
                    if (!recovery.Success)
                    {
                        StateHeading.Text = LocalizedText.Get("OperationFailedHeading");
                        StatusBox.Text = recovery.Message;
                        operationFailureCode = recovery.Code;
                        operationOutcome = "failed";
                        return;
                    }
                    fileProgressPhase = "selected-files";
                    OperationProgress.Value = 0;
                    OperationProgress.IsIndeterminate = true;
                    ProgressLabel.Text = LocalizedText.Get("PreparingFiles");
                    _ = RefreshRootState(persist: false);
                    target = RequireTrustedSelected(root);
                    if (!SamePath(target.Path, selectedPath))
                        throw new InstallerException("INSTANCE_UNTRUSTED", LocalizedText.Get("UiSelectTrustedInstance"));
                }

                var activePath = _installer.GetActiveInstancePath(root);
                var marker = _installer.InspectActiveMarker(root);
                if (marker.State is ActiveMarkerState.UnsupportedSchema or ActiveMarkerState.UnsafePath)
                    throw new InstallerException(marker.State == ActiveMarkerState.UnsupportedSchema
                        ? "ACTIVE_MARKER_UNSUPPORTED" : "ACTIVE_MARKER_UNSAFE",
                        LocalizedText.Get(marker.State == ActiveMarkerState.UnsupportedSchema
                            ? "ActiveMarkerUnsupported" : "ActiveMarkerUnsafe"));
                var allowMarkerRecovery = marker.State is ActiveMarkerState.Malformed or ActiveMarkerState.MissingTarget;
                if (!SamePath(activePath, target.Path) || allowMarkerRecovery)
                {
                    var markerNote = allowMarkerRecovery
                        ? LocalizedText.Get("UiMarkerWillBeBackedUp")
                        : LocalizedText.Get("UiMarkerWillSwitch");
                    if (MessageBox.Show(this,
                            LocalizedText.Get("UiActivateInstanceConfirm", release.PackName,
                                release.PackVersion, target.Path) + Environment.NewLine + markerNote,
                            LocalizedText.Get("UiActivateInstanceTitle"), MessageBoxButton.YesNo,
                            MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    {
                        operationOutcome = "cancelled";
                        operationFailureCode = "CANCELLED";
                        StatusBox.Text = LocalizedText.Get("OperationStoppedStatus");
                        return;
                    }
                }

                await Task.Run(() => _launcherController.CloseBeforeInstallAsync(cancellation.Token, operationLog), cancellation.Token);
                FabricLauncherService? activationLauncher = null;
                var releaseLauncher = _launcher;
                if (release.MinecraftVersion != TestPackRelease.MinecraftVersion)
                    releaseLauncher = activationLauncher = new FabricLauncherService(
                        minecraftVersion: release.MinecraftVersion,
                        ensureLauncherClosed: _launcherController.EnsureClosed);
                try
                {
                    var markerBackup = await Task.Run(() => _installer.ActivateExistingInstanceAsync(root, target.Path,
                        archivePath, archiveHash, releaseLauncher, allowMarkerRecovery, cancellation.Token, operationLog), cancellation.Token);
                    filesInstalled = true;
                    if (markerBackup is not null) StatusBox.Text = LocalizedText.Get("ActiveMarkerBackupSaved", markerBackup);
                }
                finally { activationLauncher?.Dispose(); }
                _gameDirectory = target.Path;
                _ = RefreshRootState(persist: false);
                operationOutcome = "completed";
                return;
            }

            InstallResult result;
            var configureRepairedTarget = false;
            if (operation == Operation.Install)
            {
                var pending = _installer.GetPendingOperation(root);
                var allowMarkerRecovery = false;
                if (pending is null)
                {
                    var marker = _installer.InspectActiveMarker(root);
                    if (marker.State is ActiveMarkerState.UnsupportedSchema or ActiveMarkerState.UnsafePath)
                        throw new InstallerException(marker.State == ActiveMarkerState.UnsupportedSchema
                            ? "ACTIVE_MARKER_UNSUPPORTED" : "ACTIVE_MARKER_UNSAFE",
                            LocalizedText.Get(marker.State == ActiveMarkerState.UnsupportedSchema
                                ? "ActiveMarkerUnsupported" : "ActiveMarkerUnsafe"));
                    if (marker.State is ActiveMarkerState.Malformed or ActiveMarkerState.MissingTarget)
                    {
                        allowMarkerRecovery = ConfirmMarkerRecovery(root, marker);
                        if (!allowMarkerRecovery)
                        {
                            operationOutcome = "cancelled";
                            operationFailureCode = "CANCELLED";
                            StatusBox.Text = LocalizedText.Get("OperationStoppedStatus");
                            return;
                        }
                    }
                }
                await Task.Run(_launcher.CheckProfileReady, cancellation.Token);
                ProgressLabel.Text = LocalizedText.Get("ClosingLauncherProgress");
                launcherTarget = await Task.Run(() => _launcherController.CloseBeforeInstallAsync(cancellation.Token, operationLog), cancellation.Token);
                await Task.Run(_launcher.CheckReady, cancellation.Token);
                if (pending is not null)
                {
                    fileProgressPhase = "pending-recovery";
                    OperationProgress.Value = 0;
                    OperationProgress.IsIndeterminate = true;
                    ProgressLabel.Text = LocalizedText.Get("SearchingPack");
                    var (pendingPack, pendingHash) = PinnedArchive(pending.Value.PackVersion);
                    var recovery = await Task.Run(() => _installer.RepairAsync(pending.Value.InstancePath,
                        pendingPack, pendingHash, progress, cancellation.Token, _launcher, operationLog), cancellation.Token);
                    if (!recovery.Success) result = recovery;
                    else
                    {
                        fileProgressPhase = "selected-files";
                        OperationProgress.Value = 0;
                        OperationProgress.IsIndeterminate = true;
                        ProgressLabel.Text = LocalizedText.Get("PreparingFiles");
                        var marker = _installer.InspectActiveMarker(root);
                        allowMarkerRecovery = marker.State is ActiveMarkerState.Malformed or ActiveMarkerState.MissingTarget
                            ? ConfirmMarkerRecovery(root, marker) : false;
                        if ((marker.State is ActiveMarkerState.Malformed or ActiveMarkerState.MissingTarget) && !allowMarkerRecovery)
                        {
                            operationOutcome = "cancelled";
                            operationFailureCode = "CANCELLED";
                            StatusBox.Text = LocalizedText.Get("OperationStoppedStatus");
                            return;
                        }
                        var (active, current) = await Task.Run(() =>
                        {
                            var instance = _installer.GetActiveInstancePath(root);
                            return (instance, instance is null ? null : InstallationManifest.Load(instance));
                        }, cancellation.Token);
                        result = current?.PackVersion == selectedPack.Version &&
                                 current.PackArchiveSha512.Equals(selectedPack.Hash, StringComparison.OrdinalIgnoreCase)
                            ? await Task.Run(() => _installer.RepairAsync(active!, selectedPack.Path, selectedPack.Hash,
                                progress, cancellation.Token, _launcher, operationLog), cancellation.Token)
                            : await Task.Run(() => _installer.InstallAsync(selectedPack.Path, selectedPack.Hash, root,
                                progress, cancellation.Token, _launcher, operationLog, allowMarkerRecovery), cancellation.Token);
                    }
                }
                else
                {
                    var (active, current) = await Task.Run(() =>
                    {
                        var instance = _installer.GetActiveInstancePath(root);
                        return (instance, instance is null ? null : InstallationManifest.Load(instance));
                    }, cancellation.Token);
                    result = current?.PackVersion == selectedPack.Version &&
                             current.PackArchiveSha512.Equals(selectedPack.Hash, StringComparison.OrdinalIgnoreCase)
                        ? await Task.Run(() => _installer.RepairAsync(active!, selectedPack.Path, selectedPack.Hash,
                            progress, cancellation.Token, _launcher, operationLog), cancellation.Token)
                        : await Task.Run(() => _installer.InstallAsync(selectedPack.Path, selectedPack.Hash, root,
                            progress, cancellation.Token, _launcher, operationLog, allowMarkerRecovery), cancellation.Token);
                }
            }
            else if (operation == Operation.Uninstall)
            {
                result = await Task.Run(async () =>
                {
                    var pending = _installer.GetPendingOperation(root);
                    var target = RequireTrustedSelected(root);
                    if (pending is not null && !SamePath(pending.Value.InstancePath, target.Path))
                        throw new InstallerException("TRANSACTION_RECOVERY_REQUIRED",
                            LocalizedText.Get("TransactionRecoveryRequired", pending.Value.InstancePath));
                    var (packPath, packHash) = InstalledInstanceCatalog.PinnedArchive(target.Release!.PackVersion, AppContext.BaseDirectory);
                    var active = _installer.GetActiveInstancePath(root);
                    var launcher = SamePath(active, target.Path) ? _launcher : null;
                    return await _installer.UninstallAsync(root, target.Path, packPath, packHash, launcher, operationLog);
                }, cancellation.Token);
            }
            else
            {
                var pending = _installer.GetPendingOperation(root);
                var selectedTargetPath = _selectedInstancePath;
                if (pending is not null)
                {
                    fileProgressPhase = "pending-recovery";
                    OperationProgress.Value = 0;
                    OperationProgress.IsIndeterminate = true;
                    ProgressLabel.Text = LocalizedText.Get("SearchingPack");
                    var (pendingArchive, pendingHash) = InstalledInstanceCatalog.PinnedArchive(pending.Value.PackVersion, AppContext.BaseDirectory);
                    var recovered = await Task.Run(() => _installer.RepairAsync(pending.Value.InstancePath,
                        pendingArchive, pendingHash, progress, cancellation.Token, _launcher, operationLog), cancellation.Token);
                    if (!recovered.Success) result = recovered;
                    else
                    {
                        fileProgressPhase = "selected-files";
                        OperationProgress.Value = 0;
                        OperationProgress.IsIndeterminate = true;
                        ProgressLabel.Text = LocalizedText.Get("PreparingFiles");
                        _ = RefreshRootState(persist: false);
                        var selected = RequireTrustedSelected(root);
                        if (selectedTargetPath is null || !SamePath(selected.Path, selectedTargetPath))
                            throw new InstallerException("INSTANCE_UNTRUSTED", LocalizedText.Get("UiSelectTrustedInstance"));
                        var (installedPackPath, installedPackHash) = InstalledInstanceCatalog.PinnedArchive(selected.Release!.PackVersion, AppContext.BaseDirectory);
                        var active = _installer.GetActiveInstancePath(root);
                        configureRepairedTarget = SamePath(active, selected.Path);
                        result = await Task.Run(() => _installer.RepairAsync(selected.Path, installedPackPath, installedPackHash,
                            progress, cancellation.Token, configureRepairedTarget ? _launcher : null, operationLog), cancellation.Token);
                    }
                }
                else
                {
                    var selected = RequireTrustedSelected(root);
                    var (installedPackPath, installedPackHash) = InstalledInstanceCatalog.PinnedArchive(selected.Release!.PackVersion, AppContext.BaseDirectory);
                    var active = _installer.GetActiveInstancePath(root);
                    configureRepairedTarget = SamePath(active, selected.Path);
                    result = await Task.Run(() => _installer.RepairAsync(selected.Path, installedPackPath, installedPackHash,
                        progress, cancellation.Token, configureRepairedTarget ? _launcher : null, operationLog), cancellation.Token);
                }
            }

            if (result.Success)
            {
                _gameDirectory = result.GameDirectory;
                if (operation != Operation.Uninstall)
                {
                    filesInstalled = true;
                    if (operation == Operation.Install)
                    {
                        _selectedInstancePath = result.GameDirectory;
                        var launch = await _launcherController.ConfigureAndStartAsync(launcherTarget,
                            () => ConfigureLauncherAsync(result.GameDirectory!, cancellation.Token, operationLog), operationLog);
                        if (launch.Status == MinecraftLauncherStartStatus.Requested)
                            StatusBox.Text = LocalizedText.Get("LauncherStartRequestedStatus");
                        else
                        {
                            operationOutcome = "profile_pending";
                            StatusBox.Text = LocalizedText.Get(launch.Status == MinecraftLauncherStartStatus.Failed
                                ? "LauncherStartFailedStatus" : "LauncherStartUnavailableStatus");
                            InstructionsBox.Text = LocalizedText.Get("LaunchInstruction");
                            DiagnosticText.Text = launch.Diagnostic;
                        }
                    }
                    else if (operation == Operation.Repair && configureRepairedTarget)
                        await ConfigureLauncherAsync(result.GameDirectory!, cancellation.Token, operationLog);
                    else if (operation == Operation.Repair)
                    {
                        StateHeading.Text = LocalizedText.Get("RepairCompleteInactiveHeading");
                        StatusBox.Text = result.Message;
                        InstructionsBox.Text = LocalizedText.Get("RepairInactiveHint");
                        ProgressLabel.Text = LocalizedText.Get("OperationComplete");
                    }
                    _ = RefreshRootState(persist: false);
                }
                else
                {
                    filesRemoved = true;
                    StateHeading.Text = LocalizedText.Get("PackUninstalled");
                    ProgressLabel.Text = LocalizedText.Get("OperationComplete");
                    StatusBox.Text = result.Message;
                    InstructionsBox.Text = LocalizedText.Get("UserFilesPreserved");
                    _ = RefreshRootState(persist: false);
                }
                if (operationOutcome != "profile_pending") operationOutcome = "completed";
            }
            else
            {
                operationOutcome = result.Code == "CANCELLED" ? "cancelled" : "failed";
                operationFailureCode = result.Code;
                StateHeading.Text = LocalizedText.Get(result.Code == "CANCELLED" ? "OperationCancelledHeading" : "OperationFailedHeading");
                StatusBox.Text = result.Code == "CANCELLED" ? LocalizedText.Get("OperationStoppedStatus") : result.Message;
                DiagnosticText.Text = FormatDiagnostic(result.Code, operationLog.CurrentLogPath);
                ProgressLabel.Text = LocalizedText.Get(result.Code == "CANCELLED" ? "OperationCancelledHeading" : "OperationFailedProgress");
            }
            }, cancellation.Token);
        }
        catch (InstallerException ex)
        {
            operationFailureCode = ex.Code;
            operationOutcome = filesInstalled ? "profile_pending" : "failed";
            operationLog.WriteException("end", "ui_operation_failed", ex);
            if (operation == Operation.ImportWorlds)
            {
                StateHeading.Text = LocalizedText.Get("WorldImportFailedHeading");
                StatusBox.Text = ex is WorldImportFailureException partial
                    ? ex.Message + Environment.NewLine + FormatWorldImportSummary(partial.PartialResult)
                    : ex.Message;
                ShowDiagnostic(ex.Code);
                InstructionsBox.Text = LocalizedText.Get("WorldImportPartialSafety");
                ProgressLabel.Text = LocalizedText.Get("OperationIncomplete");
                return;
            }
            StateHeading.Text = LocalizedText.Get(filesRemoved ? "FilesRemovedProfileRemains" :
                filesInstalled ? "PackInstalledProfilePending" : "NeedAnotherStep");
            StatusBox.Text = ex.Message;
            ShowDiagnostic(ex.Code);
            InstructionsBox.Text = filesRemoved
                ? LocalizedText.Get("RetryUninstallProfile")
                : filesInstalled
                ? LocalizedText.Get("RetryConfigureProfile")
                : LocalizedText.Get("FixAndRetry");
            ProgressLabel.Text = LocalizedText.Get("OperationIncomplete");
        }
        catch (OperationCanceledException ex) when (cancellation.IsCancellationRequested)
        {
            operationFailureCode = "CANCELLED";
            operationOutcome = "cancelled";
            operationLog.WriteException("end", "ui_operation_cancelled", ex, "cancelled");
            if (operation == Operation.ImportWorlds)
            {
                StateHeading.Text = LocalizedText.Get("WorldImportStopped");
                var partial = ex is WorldImportCancelledException canceled
                    ? canceled.PartialResult : new WorldImportResult(0, 0, 0, 0);
                StatusBox.Text = LocalizedText.Get("WorldCopyCancelled", FormatWorldImportSummary(partial));
                InstructionsBox.Text = LocalizedText.Get("WorldImportPartialSafety");
                ProgressLabel.Text = LocalizedText.Get("OperationCancelledHeading");
                return;
            }
            StateHeading.Text = LocalizedText.Get("OperationCancelledHeading");
            StatusBox.Text = filesInstalled
                ? LocalizedText.Get("InstalledProfilePendingStatus")
                : LocalizedText.Get("OperationCancelledStatus");
            ProgressLabel.Text = LocalizedText.Get("OperationCancelledHeading");
        }
        catch (Exception ex)
        {
            operationOutcome = filesInstalled ? "profile_pending" : "failed";
            operationLog.WriteException("end", "ui_operation_failed", ex);
            if (operation == Operation.ImportWorlds)
            {
                StateHeading.Text = LocalizedText.Get("WorldImportFailedHeading");
                StatusBox.Text = LocalizedText.Get("WorldImportFailedStatus");
                ShowDiagnostic(ex.GetType().Name);
                InstructionsBox.Text = LocalizedText.Get("WorldImportFailureSafety");
                ProgressLabel.Text = LocalizedText.Get("OperationFailedProgress");
                return;
            }
            StateHeading.Text = LocalizedText.Get(filesRemoved ? "FilesRemovedProfileRemains" :
                filesInstalled ? "PackInstalledProfilePending" : "OperationUnexpectedFailure");
            StatusBox.Text = LocalizedText.Get("ActionFailedHelp");
            ShowDiagnostic(ex.GetType().Name);
            InstructionsBox.Text = filesRemoved
                ? LocalizedText.Get("RetryUninstall")
                : filesInstalled
                ? LocalizedText.Get("RetryConfigure")
                : LocalizedText.Get("FixAndRetry");
            ProgressLabel.Text = LocalizedText.Get("OperationFailedProgress");
        }
        finally
        {
            operationLog.Complete(operationOutcome, operationFailureCode);
            OperationProgress.IsIndeterminate = false;
            cancellation.Dispose();
            _operationCancellation = null;
            _operationCanBeCancelled = true;
            SetBusy(false);
        }
    }

    private async Task ConfigureLauncherAsync(string gameDirectory, CancellationToken cancellationToken,
        OperationLog operationLog)
    {
        operationLog.Write("profile", "started", "ui_profile_setup_started");
        OperationProgress.Maximum = Math.Max(1, OperationProgress.Maximum);
        OperationProgress.IsIndeterminate = true;
        ProgressLabel.Text = LocalizedText.Get("ConfigureLauncherProgress");
        await Task.Run(async () =>
        {
            var manifest = InstallationManifest.Load(gameDirectory);
            operationLog.SetRelease(manifest.PackVersion, manifest.MinecraftVersion,
                manifest.FabricLoaderVersion, manifest.PackArchiveSha512);
            if (manifest.MinecraftVersion == TestPackRelease.MinecraftVersion)
                await _launcher.ConfigureAsync(gameDirectory, cancellationToken, operationLog);
            else
            {
                using var previousLauncher = new FabricLauncherService(minecraftVersion: manifest.MinecraftVersion,
                    ensureLauncherClosed: _launcherController.EnsureClosed);
                await previousLauncher.ConfigureAsync(gameDirectory, cancellationToken, operationLog);
            }
        }, cancellationToken);
        OperationProgress.IsIndeterminate = false;
        if (OperationProgress.Value == 0 && OperationProgress.Maximum == 1) OperationProgress.Value = 1;
        operationLog.Write("profile", "completed", "ui_profile_setup_completed");
        StateHeading.Text = LocalizedText.Get("PackReady");
        ProgressLabel.Text = LocalizedText.Get("InstallComplete");
        StatusBox.Text = LocalizedText.Get("LaunchInstruction");
        InstructionsBox.Text = LocalizedText.Get("FirstLaunchDownload");
        DiagnosticText.Text = LocalizedText.Get("GameFolderLabel", gameDirectory);
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var root = RefreshRootState(persist: false);
            var selected = GetSelectedEntry();
            var path = root is not null && selected is { IsOpenable: true } && IsUnderSelectedRoot(root, selected.Path)
                ? selected.Path : null;
            if (path is null || !Directory.Exists(path))
            {
                StatusBox.Text = LocalizedText.Get("InstalledFolderNotFound");
                return;
            }
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusBox.Text = LocalizedText.Get("OpenFolderFailed", ex.GetType().Name);
        }
    }

    private void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = _currentOperationLog?.CurrentLogPath;
            if (path is null)
            {
                StatusBox.Text = LocalizedText.Get("LogUnavailable");
                return;
            }
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusBox.Text = LocalizedText.Get("OpenLogFailed", ex.GetType().Name);
        }
    }

    private void ExportLog_Click(object sender, RoutedEventArgs e)
    {
        var operationLog = _currentOperationLog;
        if (operationLog?.CurrentLogPath is null)
        {
            StatusBox.Text = LocalizedText.Get("LogUnavailable");
            return;
        }

        var dialog = new SaveFileDialog
        {
            FileName = Path.GetFileName(operationLog.CurrentLogPath),
            DefaultExt = ".jsonl",
            AddExtension = true,
            Filter = "JSONL diagnostics (*.jsonl)|*.jsonl",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true) return;
        StatusBox.Text = operationLog.TryExportCurrent(dialog.FileName)
            ? LocalizedText.Get("DiagnosticExported")
            : LocalizedText.Get("DiagnosticExportFailed");
    }

    private void ModrinthLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        if (e.Uri.Scheme != Uri.UriSchemeHttps || e.Uri.Host != "modrinth.com" ||
            !PackCatalog.Items.Any(item => item.ModrinthUrl == e.Uri))
            return;
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch
        {
            StatusBox.Text = LocalizedText.Get("OpenModrinthFailed");
        }
        e.Handled = true;
    }

    private void ShowDiagnostic(string code)
    {
        DiagnosticText.Text = FormatDiagnostic(code, _currentOperationLog?.CurrentLogPath);
    }

    private static string FormatDiagnostic(string code, string? logPath) =>
        $"{LocalizedText.Get("DiagnosticCode", code)}\n{LocalizedText.Get("DiagnosticLog", logPath ?? LocalizedText.Get("LogUnavailable"))}";

    private static string FormatWorldImportSummary(WorldImportResult result) =>
        LocalizedText.Get("WorldImportSummary", result.Imported, result.SkippedExisting,
            result.SkippedMissingLock, result.SkippedLockedOrUnverified);

    private static (string Path, string Hash) PinnedArchive(string version) =>
        InstalledInstanceCatalog.PinnedArchive(version, AppContext.BaseDirectory);

    private void Cancel_Click(object sender, RoutedEventArgs e) => _operationCancellation?.Cancel();

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_operationCancellation is not null)
        {
            e.Cancel = true;
            if (_operationCanBeCancelled)
            {
                StatusBox.Text = LocalizedText.Get("CancelCurrentOperation");
                _operationCancellation.Cancel();
            }
            else StatusBox.Text = LocalizedText.Get("OperationWait");
            return;
        }
        base.OnClosing(e);
    }

    private void SetBusy(bool busy)
    {
        VanillaPlusOption.IsEnabled = !busy;
        Vanilla2PlusOption.IsEnabled = !busy;
        BrowseButton.IsEnabled = !busy;
        InstallRootBox.IsEnabled = !busy;
        InstallButton.IsEnabled = !busy;
        RepairButton.IsEnabled = !busy;
        UninstallButton.IsEnabled = !busy;
        CancelButton.IsEnabled = busy && _operationCanBeCancelled;
        CancelButton.Visibility = busy && _operationCanBeCancelled ? Visibility.Visible : Visibility.Collapsed;
        RetryLauncherButton.IsEnabled = !busy;
        ImportWorldsButton.IsEnabled = !busy;
        InstalledInstanceComboBox.IsEnabled = !busy;
        OpenFolderButton.IsEnabled = !busy;
        OpenLogButton.IsEnabled = !busy;
        ExportLogButton.IsEnabled = !busy;
    }


    protected override void OnClosed(EventArgs e)
    {
        _operationCancellation?.Cancel();
        _installer.Dispose();
        _launcher.Dispose();
        base.OnClosed(e);
    }

    private sealed record InstanceChoice(InstalledInstanceEntry Entry, string Display);

    private enum Operation { Install, Repair, Uninstall, ConfigureLauncher, ImportWorlds }
}
