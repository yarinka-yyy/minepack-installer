using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Threading;
using MinePack.Core;
using Microsoft.Win32;

[assembly: InternalsVisibleTo("MinePack.UiSmoke")]

namespace MinePack.Installer;

public partial class MainWindow : Window
{
    private readonly InstallService _installer = new();
    private readonly MinecraftLauncherController _launcherController;
    private readonly PrismLauncherController _prismLauncherController;
    private readonly FabricLauncherService _launcher;
    private readonly string _preferencesPath;
    private CancellationTokenSource? _operationCancellation;
    private IInputElement? _uninstallReturnFocus;
    private bool _operationCanBeCancelled = true;
    private string? _gameDirectory;
    private readonly string _installerVersion;
    private OperationLog? _currentOperationLog;
    private IReadOnlyList<InstanceChoice> _instanceChoices = [];
    private InstalledInstanceEntry? _operationTargetEntry;
    private InstalledInstanceEntry? _deleteSelectedEntry;
    private InstalledInstanceEntry? _pendingDeleteEntry;
    private InstanceCleanupRequest? _pendingCleanupRequest;
    private int _cleanupCheckGeneration;
    private PendingDeleteAction _pendingDeleteAction;
    private ActiveMarkerInspection? _activeMarker;
    private string? _missingSavedRootPath;
    private IReadOnlyList<MinecraftLauncherTarget> _officialTargets = [];
    private bool _officialDiscoveryUnknown;
    private IReadOnlyList<PrismLauncherTarget> _prismTargets = [];
    private IReadOnlyList<InstalledInstanceEntry> _prismInstanceEntries = [];
    private HashSet<string> _activePrismInstancePaths = new(StringComparer.OrdinalIgnoreCase);
    private LauncherScanResult _prismScan = new(LauncherDiscoveryState.Unknown, [], "PRISM_DISCOVERY_PENDING");
    private IReadOnlyList<PrismTargetHint> _savedPrismHints = [];
    private IReadOnlyList<LauncherTargetChoice> _launcherTargetChoices = [];
    private TaskCompletionSource<LauncherInstallSelection?>? _launcherChoiceCompletion;
    private IInputElement? _launcherChoiceReturnFocus;
    private IInputElement? _operationProgressReturnFocus;
    private int _launcherChoiceGeneration;
    private int _uninstallConfirmationGeneration;
    private bool _operationProgressPresented;
    private bool _operationProgressTerminal;

    public MainWindow() : this(null)
    {
    }

    internal MainWindow(string? fixtureRoot)
    {
        InitializeComponent();
        _preferencesPath = fixtureRoot is null
            ? InstallerPreferences.DefaultPath
            : Path.Combine(fixtureRoot, "installer-preferences.json");
        _launcherController = new MinecraftLauncherController();
        _prismLauncherController = new PrismLauncherController();
        _launcher = new FabricLauncherService(fixtureRoot, ensureLauncherClosed: _launcherController.EnsureClosed);
        VanillaPlusOption.Checked += PackChoice_Changed;
        Vanilla2PlusOption.Checked += PackChoice_Changed;
        var assembly = typeof(MainWindow).Assembly;
        _installerVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString(3)
            ?? "0.0.0";
        InstallerVersionText.Text = LocalizedText.Get("UiInstallerVersion", _installerVersion);
        UpdatePackSelection();
        if (fixtureRoot is not null)
        {
            InstallRootBox.Text = fixtureRoot;
            RootAvailabilityText.Text = LocalizedText.Get("UiRootNeedsValidation");
            return;
        }

        Loaded += MainWindow_Loaded;
        var preferences = InstallerPreferences.Load(_preferencesPath);
        _savedPrismHints = preferences.PrismTargetHints;
        InstallRootBox.Text = preferences.LastValidatedRoot ?? InstallService.DefaultInstallRoot;
        if (preferences.LastValidatedRoot is not null && !Directory.Exists(preferences.LastValidatedRoot))
            _missingSavedRootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(preferences.LastValidatedRoot));
        if (preferences.IsCorrupt) ShowInlineNotice(LocalizedText.Get("UiPreferencesCorrupt"));
        var root = RefreshRootState(persist: false);
        if (root is null && !preferences.IsCorrupt) ShowInlineNotice(LocalizedText.Get("UiSavedRootUnavailable"));
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshLauncherDiscoveryAsync();
    }

    private async Task<LauncherInventory> ScanLaunchersAsync()
    {
        return await Task.Run(() =>
        {
            IReadOnlyList<MinecraftLauncherTarget> official = [];
            var officialUnknown = false;
            try { official = _launcherController.FindTargets(); }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            { officialUnknown = true; }
            var prism = LauncherDiscovery.DiscoverPrism(_savedPrismHints);
            var prismEntries = new List<InstalledInstanceEntry>();
            var activePrismPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var target in prism.PrismTargets)
            {
                try
                {
                    var layout = InstallationLayout.Prism(target, "minepack-target-validation");
                    var entries = InstalledInstanceCatalog.Enumerate(layout, AppContext.BaseDirectory);
                    prismEntries.AddRange(entries);
                    var activePath = _installer.GetActiveInstancePath(layout);
                    if (activePath is not null) activePrismPaths.Add(Path.GetFullPath(activePath));
                }
                catch (Exception ex) when (ex is InstallerException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                { }
            }
            return new LauncherInventory(official, prism, officialUnknown, prismEntries, activePrismPaths);
        }).ConfigureAwait(true);
    }

    private async Task RefreshLauncherDiscoveryAsync()
    {
        try
        {
            var inventory = await ScanLaunchersAsync();
            ApplyLauncherInventory(inventory);
            _ = RefreshRootState(persist: false);
        }
        catch (Exception ex) when (ex is InstallerException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            _prismScan = new LauncherScanResult(LauncherDiscoveryState.Unknown, [], "PRISM_DISCOVERY_UNKNOWN");
        }
    }

    private void ApplyLauncherInventory(LauncherInventory inventory)
    {
        _officialTargets = inventory.OfficialTargets.Distinct().ToArray();
        _officialDiscoveryUnknown = inventory.OfficialUnknown;
        _prismScan = inventory.PrismScan;
        _prismTargets = inventory.PrismScan.PrismTargets;
        _prismInstanceEntries = inventory.PrismInstanceEntries;
        _activePrismInstancePaths = inventory.ActivePrismInstancePaths;
    }

    private async Task<LauncherInstallSelection?> ResolveLauncherInstallSelectionAsync()
    {
        var inventory = await ScanLaunchersAsync();
        ApplyLauncherInventory(inventory);
        var officialState = inventory.OfficialUnknown ? LauncherDiscoveryState.Unknown :
            inventory.OfficialTargets.Count > 0 ? LauncherDiscoveryState.Found : LauncherDiscoveryState.Absent;
        var decision = inventory.OfficialUnknown
            ? new LauncherDecision(LauncherDecisionKind.LocateOrRetry)
            : LauncherDiscovery.Decide(officialState, inventory.OfficialTargets.Count,
                inventory.PrismScan.State, inventory.PrismScan.PrismTargets.Count);

        if (decision.Kind == LauncherDecisionKind.Automatic)
        {
            var automatic = MakeAutomaticSelection(decision.LauncherKind!.Value, inventory);
            if (automatic?.Kind == LauncherKind.Prism) SavePrismTargetHints();
            return automatic;
        }

        var status = decision.Kind == LauncherDecisionKind.LocateOrRetry
            ? LocalizedText.Get("UiLauncherChoiceUnknown") + Environment.NewLine +
              (inventory.PrismScan.DiagnosticCode ?? (inventory.OfficialUnknown ? "OFFICIAL_DISCOVERY_UNKNOWN" : "LAUNCHER_NOT_FOUND"))
            : string.Empty;
        return await ShowLauncherChoiceAsync(inventory, decision.LauncherKind, status);
    }

    private static LauncherInstallSelection? MakeAutomaticSelection(LauncherKind kind, LauncherInventory inventory)
    {
        if (kind == LauncherKind.Official && inventory.OfficialTargets.Count == 1)
            return new LauncherInstallSelection(kind, inventory.OfficialTargets[0], null);
        if (kind == LauncherKind.Prism && inventory.PrismScan.State == LauncherDiscoveryState.Found &&
            inventory.PrismScan.PrismTargets.Count == 1)
            return new LauncherInstallSelection(kind, null, inventory.PrismScan.PrismTargets[0]);
        return null;
    }

    private async Task<LauncherInstallSelection?> ShowLauncherChoiceAsync(LauncherInventory inventory,
        LauncherKind? preferredKind, string status)
    {
        _launcherChoiceReturnFocus = Keyboard.FocusedElement;
        var completion = new TaskCompletionSource<LauncherInstallSelection?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var generation = ++_launcherChoiceGeneration;
        _launcherChoiceCompletion = completion;
        LauncherChoiceStatus.Text = status;
        _settingLauncherChoices = true;
        OfficialLauncherChoice.IsEnabled = !inventory.OfficialUnknown && inventory.OfficialTargets.Count > 0;
        PrismLauncherChoice.IsEnabled = !inventory.OfficialUnknown && inventory.PrismScan.State == LauncherDiscoveryState.Found &&
                                        inventory.PrismScan.PrismTargets.Count > 0;
        OfficialLauncherChoice.IsChecked = preferredKind == LauncherKind.Official;
        PrismLauncherChoice.IsChecked = preferredKind == LauncherKind.Prism;
        _settingLauncherChoices = false;
        UpdateLauncherTargetChoices();
        MainContentScrollViewer.IsEnabled = false;
        LauncherChoiceOverlay.Visibility = Visibility.Visible;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (!IsCurrentLauncherChoiceSession(completion, generation)) return;
            if (OfficialLauncherChoice.IsEnabled && preferredKind != LauncherKind.Prism) OfficialLauncherChoice.Focus();
            else if (PrismLauncherChoice.IsEnabled) PrismLauncherChoice.Focus();
            else LocatePrismButton.Focus();
        }));
        LauncherInstallSelection? result;
        try
        {
            result = await completion.Task;
        }
        finally
        {
            if (IsSameLauncherChoiceSession(completion, generation))
            {
                LauncherChoiceOverlay.Visibility = Visibility.Collapsed;
                _launcherChoiceCompletion = null;
                MainContentScrollViewer.IsEnabled = true;
                var returnFocus = _launcherChoiceReturnFocus;
                _launcherChoiceReturnFocus = null;
                if (returnFocus is not null) Keyboard.Focus(returnFocus);
            }
        }
        if (result?.Kind == LauncherKind.Prism) SavePrismTargetHints();
        return result;
    }

    private bool IsSameLauncherChoiceSession(TaskCompletionSource<LauncherInstallSelection?> completion,
        int generation) => generation == _launcherChoiceGeneration &&
                           ReferenceEquals(completion, _launcherChoiceCompletion) &&
                           LauncherChoiceOverlay.Visibility == Visibility.Visible;

    private bool IsCurrentLauncherChoiceSession(TaskCompletionSource<LauncherInstallSelection?> completion,
        int generation) => IsSameLauncherChoiceSession(completion, generation) && !completion.Task.IsCompleted;

    private LauncherChoiceSession? CaptureLauncherChoiceSession() =>
        _launcherChoiceCompletion is { } completion && IsCurrentLauncherChoiceSession(completion, _launcherChoiceGeneration)
            ? new LauncherChoiceSession(completion, _launcherChoiceGeneration)
            : null;

    internal LauncherChoiceSession? CaptureLauncherChoiceSessionForUiSmoke() => CaptureLauncherChoiceSession();

    internal Task<LauncherInstallSelection?> ShowLauncherChoiceForUiSmokeAsync(LauncherInventory inventory)
    {
        ApplyLauncherInventory(inventory);
        return ShowLauncherChoiceAsync(inventory, null, string.Empty);
    }

    private void UpdateLauncherTargetChoices()
    {
        var kind = OfficialLauncherChoice.IsChecked == true ? LauncherKind.Official :
            PrismLauncherChoice.IsChecked == true ? LauncherKind.Prism : (LauncherKind?)null;
        if (kind is null)
        {
            _launcherTargetChoices = [];
        }
        else if (kind == LauncherKind.Official)
        {
            _launcherTargetChoices = _officialTargets.Select(target => new LauncherTargetChoice(kind.Value,
                LocalizedText.Get("UiLauncherTargetPath", LocalizedText.Get("UiLauncherOptionOfficial"), target.Identity),
                target, null)).ToArray();
        }
        else
        {
            _launcherTargetChoices = _prismTargets.Select(target => new LauncherTargetChoice(kind.Value,
                LocalizedText.Get("UiLauncherTargetPath", LocalizedText.Get("UiLauncherOptionPrism"), target.DataRoot),
                null, target)).ToArray();
        }

        _settingLauncherChoices = true;
        LauncherTargetComboBox.ItemsSource = _launcherTargetChoices;
        var needsTargetChoice = _launcherTargetChoices.Count > 1;
        LauncherTargetLabel.Visibility = needsTargetChoice ? Visibility.Visible : Visibility.Collapsed;
        LauncherTargetComboBox.Visibility = needsTargetChoice ? Visibility.Visible : Visibility.Collapsed;
        LauncherTargetComboBox.SelectedItem = _launcherTargetChoices.Count == 1 ? _launcherTargetChoices[0] : null;
        LauncherChoiceContinueButton.IsEnabled = _launcherTargetChoices.Count == 1;
        _settingLauncherChoices = false;
    }

    private void LauncherChoiceRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (!_settingLauncherChoices) UpdateLauncherTargetChoices();
    }

    private void LauncherTargetComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!_settingLauncherChoices)
            LauncherChoiceContinueButton.IsEnabled = LauncherTargetComboBox.SelectedItem is LauncherTargetChoice;
    }

    private void LauncherChoiceContinue_Click(object sender, RoutedEventArgs e)
    {
        if (_launcherChoiceCompletion is not { } completion || completion.Task.IsCompleted ||
            LauncherTargetComboBox.SelectedItem is not LauncherTargetChoice choice) return;
        _launcherChoiceCompletion?.TrySetResult(choice.ToSelection());
    }

    private void LauncherChoiceCancel_Click(object sender, RoutedEventArgs e) =>
        _launcherChoiceCompletion?.TrySetResult(null);

    private void LauncherChoiceOverlay_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || IsWithinCard(e.OriginalSource as DependencyObject, LauncherChoiceCard)) return;
        _launcherChoiceCompletion?.TrySetResult(null);
        e.Handled = true;
    }

    private void LauncherChoiceOverlay_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _launcherChoiceCompletion?.TrySetResult(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && LauncherChoiceContinueButton.IsEnabled)
        {
            _launcherChoiceCompletion?.TrySetResult((LauncherTargetComboBox.SelectedItem as LauncherTargetChoice)?.ToSelection());
            e.Handled = true;
        }
    }

    private async void RescanLauncherButton_Click(object sender, RoutedEventArgs e)
    {
        var session = CaptureLauncherChoiceSession();
        if (session is null) return;
        await RescanLauncherChoicesAsync(session, ScanLaunchersAsync());
    }

    private async Task<bool> RescanLauncherChoicesAsync(LauncherChoiceSession session, Task<LauncherInventory> scan)
    {
        var completion = session.Completion;
        var generation = session.Generation;
        if (!IsCurrentLauncherChoiceSession(completion, generation)) return false;
        LauncherChoiceStatus.Text = LocalizedText.Get("UiLauncherChoiceUnknown");
        var preferred = OfficialLauncherChoice.IsChecked == true ? LauncherKind.Official :
            PrismLauncherChoice.IsChecked == true ? LauncherKind.Prism : (LauncherKind?)null;
        var inventory = await scan;
        if (!IsCurrentLauncherChoiceSession(completion, generation)) return false;
        ApplyLauncherInventory(inventory);
        var status = inventory.PrismScan.State == LauncherDiscoveryState.Unknown || inventory.OfficialUnknown
            ? LocalizedText.Get("UiLauncherChoiceUnknown") + Environment.NewLine +
              (inventory.PrismScan.DiagnosticCode ?? "OFFICIAL_DISCOVERY_UNKNOWN") : string.Empty;
        _settingLauncherChoices = true;
        OfficialLauncherChoice.IsChecked = false;
        PrismLauncherChoice.IsChecked = false;
        _settingLauncherChoices = false;
        LauncherChoiceStatus.Text = status;
        OfficialLauncherChoice.IsEnabled = !inventory.OfficialUnknown && inventory.OfficialTargets.Count > 0;
        PrismLauncherChoice.IsEnabled = !inventory.OfficialUnknown && inventory.PrismScan.State == LauncherDiscoveryState.Found &&
                                        inventory.PrismScan.PrismTargets.Count > 0;
        if (preferred == LauncherKind.Official && OfficialLauncherChoice.IsEnabled) OfficialLauncherChoice.IsChecked = true;
        else if (preferred == LauncherKind.Prism && PrismLauncherChoice.IsEnabled) PrismLauncherChoice.IsChecked = true;
        UpdateLauncherTargetChoices();
        return true;
    }

    internal Task<bool> RescanLauncherChoicesForUiSmokeAsync(Task<LauncherInventory> scan)
    {
        var session = CaptureLauncherChoiceSession();
        return session is null ? Task.FromResult(false) : RescanLauncherChoicesAsync(session, scan);
    }

    private async void LocatePrismButton_Click(object sender, RoutedEventArgs e)
    {
        var session = CaptureLauncherChoiceSession();
        if (session is null) return;
        var executablePicker = new OpenFileDialog
        {
            Title = LocalizedText.Get("UiLauncherLocatePrism"),
            Filter = "Prism Launcher (prismlauncher.exe)|prismlauncher.exe",
            CheckFileExists = true,
            Multiselect = false
        };
        if (executablePicker.ShowDialog(this) != true) return;
        if (!IsCurrentLauncherChoiceSession(session.Completion, session.Generation)) return;
        var result = await Task.Run(() => LauncherDiscovery.LocatePrismExecutable(executablePicker.FileName));
        if (!IsCurrentLauncherChoiceSession(session.Completion, session.Generation)) return;
        if (result.State != LauncherDiscoveryState.Found && result.DiagnosticCode is "PRISM_CONFIG_UNKNOWN" or "PRISM_DATA_ROOT_UNKNOWN")
        {
            var folderPicker = new OpenFolderDialog { Title = LocalizedText.Get("PrismConfigurationUnknown") };
            if (folderPicker.ShowDialog(this) != true) return;
            if (!IsCurrentLauncherChoiceSession(session.Completion, session.Generation)) return;
            result = await Task.Run(() => LauncherDiscovery.LocatePrismExecutable(executablePicker.FileName, folderPicker.FolderName));
        }
        ApplyLocatedPrismResult(result, session.Completion, session.Generation);
    }

    private bool ApplyLocatedPrismResult(LauncherScanResult result,
        TaskCompletionSource<LauncherInstallSelection?> completion, int generation)
    {
        if (!IsCurrentLauncherChoiceSession(completion, generation)) return false;
        _prismScan = result;
        _prismTargets = result.PrismTargets;
        var status = result.State == LauncherDiscoveryState.Found && !_officialDiscoveryUnknown
            ? string.Empty : LocalizedText.Get("UiLauncherChoiceUnknown") + Environment.NewLine + (result.DiagnosticCode ?? "PRISM_DISCOVERY_UNKNOWN");
        LauncherChoiceStatus.Text = status;
        PrismLauncherChoice.IsEnabled = !_officialDiscoveryUnknown && result.State == LauncherDiscoveryState.Found && result.PrismTargets.Count > 0;
        OfficialLauncherChoice.IsEnabled = !_officialDiscoveryUnknown && _officialTargets.Count > 0;
        if (PrismLauncherChoice.IsEnabled)
        {
            _settingLauncherChoices = true;
            PrismLauncherChoice.IsChecked = true;
            OfficialLauncherChoice.IsChecked = false;
            _settingLauncherChoices = false;
        }
        UpdateLauncherTargetChoices();
        return true;
    }

    internal bool ApplyLocatedPrismResultForUiSmoke(LauncherChoiceSession session, LauncherScanResult result) =>
        ApplyLocatedPrismResult(result, session.Completion, session.Generation);

    private void SavePrismTargetHints()
    {
        try
        {
            var root = InstallService.ValidateInstallRoot(InstallRootBox.Text);
            InstallerPreferences.SaveLastValidatedRoot(_preferencesPath, root, _prismTargets);
            _savedPrismHints = InstallerPreferences.Load(_preferencesPath).PrismTargetHints;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InstallerException)
        {
            ShowInlineNotice(LocalizedText.Get("UiPreferencesNotSaved"));
        }
    }

    private bool _settingLauncherChoices;

    private static string ReleasePath(string relativePath) => Path.Combine(AppContext.BaseDirectory,
        relativePath.Replace('/', Path.DirectorySeparatorChar));

    private string VanillaPlusPackPath => ReleasePath(TestPackRelease.ArtifactRelativePath);

    private string Vanilla2PlusPackPath => ReleasePath(Vanilla2PlusRelease.ArtifactRelativePath);

    private (string Path, string Hash, string Version, string MinecraftVersion) SelectedPack => Vanilla2PlusOption.IsChecked == true
        ? (Vanilla2PlusPackPath, Vanilla2PlusRelease.ArtifactSha512, Vanilla2PlusRelease.PackVersion, Vanilla2PlusRelease.MinecraftVersion)
        : (VanillaPlusPackPath, TestPackRelease.ArtifactSha512, TestPackRelease.PackVersion, TestPackRelease.MinecraftVersion);

    private string SelectedPackName => Vanilla2PlusOption.IsChecked == true ? "Frontier" : "Vanilla Plus";

    private void PackChoice_Changed(object sender, RoutedEventArgs e) => UpdatePackSelection();

    private void UpdatePackSelection()
    {
        var vanilla2Plus = Vanilla2PlusOption.IsChecked == true;
        CatalogList.ItemsSource = vanilla2Plus ? PackCatalog.Vanilla2PlusGroups : PackCatalog.VanillaPlusGroups;
        PackVersionText.Text = LocalizedText.Get("UiPackVersion",
            vanilla2Plus ? Vanilla2PlusRelease.PackVersion : TestPackRelease.PackVersion);
        PackCountsText.Text = LocalizedText.Get(vanilla2Plus ? "UiPackCountsVanilla2Plus" : "UiPackCountsVanillaPlus");
    }

    private void BeginOperationPresentation(Operation operation)
    {
        _operationProgressPresented = false;
        _operationProgressTerminal = false;
        _operationProgressReturnFocus = Keyboard.FocusedElement;
        OperationProgressOverlay.Visibility = Visibility.Collapsed;
        MainContentScrollViewer.IsEnabled = true;
        InstallButton.IsDefault = true;
        CancelButton.IsDefault = false;
        ClearInlineNotice();
        OperationProgress.Value = 0;
        OperationProgress.IsIndeterminate = true;
        InstructionsBox.Text = string.Empty;
        DiagnosticText.Text = string.Empty;
        StatusBox.Text = LocalizedText.Get("OperationWait");
        StateHeading.Text = LocalizedText.Get(operation == Operation.Install ? "OperationInstalling" : "OperationWorking");
        ProgressLabel.Text = operation switch
        {
            Operation.Install => LocalizedText.Get("PreparingFiles"),
            Operation.Repair => LocalizedText.Get("SearchingPack"),
            Operation.ConfigureLauncher => LocalizedText.Get("RestoringProfile"),
            Operation.ImportWorlds => LocalizedText.Get("CopyingWorlds"),
            Operation.RemoveResidue => LocalizedText.Get("UiDeleteCleanupChecking"),
            _ => LocalizedText.Get("PreparingUninstall")
        };
        SetBusy(true);
    }

    private void PresentOperationProgress()
    {
        _operationProgressPresented = true;
        ShowOperationProgress();
    }

    private void ShowOperationProgress()
    {
        if (!_operationProgressPresented) return;
        OperationProgressOverlay.Visibility = Visibility.Visible;
        MainContentScrollViewer.IsEnabled = false;
        InstallButton.IsDefault = false;
        CancelButton.Content = LocalizedText.Get(_operationProgressTerminal ? "UiCloseProgress" : "UiCancel");
        CancelButton.IsEnabled = _operationProgressTerminal ||
                                 (_operationCancellation is not null && _operationCanBeCancelled);
        CancelButton.Visibility = CancelButton.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.IsDefault = _operationProgressTerminal;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (OperationProgressOverlay.Visibility != Visibility.Visible) return;
            if (_operationProgressTerminal && CancelButton.IsEnabled) CancelButton.Focus();
            else OperationProgressCard.Focus();
        }));
    }

    private void HideOperationProgress()
    {
        if (_operationProgressTerminal || _operationCancellation is null) return;
        OperationProgressOverlay.Visibility = Visibility.Collapsed;
        MainContentScrollViewer.IsEnabled = true;
        InstallButton.IsDefault = true;
        var returnFocus = _operationProgressReturnFocus;
        _operationProgressReturnFocus = null;
        if (returnFocus is not null) Keyboard.Focus(returnFocus);
        else InstallButton.Focus();
    }

    private void CompleteOperationPresentation(Operation operation, string outcome)
    {
        if (operation == Operation.Install && outcome == "completed")
        {
            StateHeading.Text = LocalizedText.Get("UiOperationInstalledHeading");
            ProgressLabel.Text = LocalizedText.Get("InstallComplete");
        }
        else if (operation == Operation.Install && outcome == "profile_pending")
            StateHeading.Text = LocalizedText.Get("PackInstalledProfilePending");

        if (!_operationProgressPresented)
        {
            if (outcome != "completed") ShowInlineNotice(StatusBox.Text);
            return;
        }

        _operationProgressTerminal = true;
        ShowOperationProgress();
    }

    private void CloseCompletedOperationProgress()
    {
        if (!_operationProgressTerminal) return;
        var notice = string.IsNullOrWhiteSpace(StatusBox.Text) ? StateHeading.Text :
            $"{StateHeading.Text}: {StatusBox.Text}";
        OperationProgressOverlay.Visibility = Visibility.Collapsed;
        MainContentScrollViewer.IsEnabled = true;
        InstallButton.IsDefault = true;
        CancelButton.IsDefault = false;
        _operationProgressPresented = false;
        _operationProgressTerminal = false;
        _operationProgressReturnFocus = null;
        ShowInlineNotice(notice);
        InstallButton.Focus();
    }

    private void ShowInlineNotice(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        InlineNoticeText.Text = message;
        InlineNoticeText.Visibility = Visibility.Visible;
        InlineNoticeText.BringIntoView();
    }

    private void ClearInlineNotice()
    {
        InlineNoticeText.Text = string.Empty;
        InlineNoticeText.Visibility = Visibility.Collapsed;
    }

    private void OperationProgressOverlay_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || IsWithinCard(e.OriginalSource as DependencyObject, OperationProgressCard)) return;
        if (_operationProgressTerminal) CloseCompletedOperationProgress();
        else HideOperationProgress();
        e.Handled = true;
    }

    private void OperationProgressOverlay_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_operationProgressTerminal) CloseCompletedOperationProgress();
            else HideOperationProgress();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && !_operationProgressTerminal &&
                 !ReferenceEquals(Keyboard.FocusedElement, CancelButton))
            e.Handled = true;
    }

    internal CancellationTokenSource BeginOperationForUiSmoke()
    {
        if (_operationCancellation is not null) throw new InvalidOperationException("A fixture operation is already active.");
        _operationCancellation = new CancellationTokenSource();
        _operationCanBeCancelled = true;
        BeginOperationPresentation(Operation.Install);
        return _operationCancellation;
    }

    internal void PresentOperationProgressForUiSmoke() => PresentOperationProgress();

    internal void FinishOperationForUiSmoke(string outcome)
    {
        PrepareOperationFinish();
        ReleaseOperationBusy();
        CompleteOperationPresentation(Operation.Install, outcome);
    }

    internal void PrepareOperationFinishForUiSmoke() => PrepareOperationFinish();

    internal CancellationTokenSource? OperationCancellationForUiSmoke => _operationCancellation;

    internal static LauncherInstallSelection? MakeAutomaticSelectionForUiSmoke(LauncherKind kind,
        LauncherInventory inventory) => MakeAutomaticSelection(kind, inventory);

    private void PrepareOperationFinish()
    {
        _operationCanBeCancelled = false;
        CancelButton.IsEnabled = false;
        CancelButton.Visibility = Visibility.Collapsed;
    }

    private void ReleaseOperationBusy()
    {
        _operationCancellation?.Dispose();
        _operationCancellation = null;
        _operationTargetEntry = null;
        _operationCanBeCancelled = true;
        SetBusy(false);
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (_operationCancellation is not null)
        {
            if (_operationProgressPresented) ShowOperationProgress();
            return;
        }
        await RunOperationAsync(Operation.Install);
    }

    private async void Repair_Click(object sender, RoutedEventArgs e) => await RunOperationAsync(Operation.Repair);

    private async void Configure_Click(object sender, RoutedEventArgs e) => await RunOperationAsync(Operation.ConfigureLauncher);

    private async void ImportWorlds_Click(object sender, RoutedEventArgs e)
    {
        var target = await ResolveActiveOperationTargetAsync();
        if (target is null) return;
        var vanillaSaves = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft", "saves");
        var picker = new OpenFolderDialog
        {
            Title = LocalizedText.Get("UiWorldFolderDialogTarget", target.Release?.PackName ?? LocalizedText.Get("UiInstalledInstance"),
                target.Release?.PackVersion ?? Path.GetFileName(target.Path)),
            InitialDirectory = Directory.Exists(vanillaSaves) ? vanillaSaves : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
        };
        if (picker.ShowDialog(this) == true)
            await RunOperationAsync(Operation.ImportWorlds, picker.FolderName, target);
    }

    private void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        _ = ShowDeleteInstanceOverlayAsync();
    }

    private async Task ShowDeleteInstanceOverlayAsync()
    {
        try
        {
            await RefreshLauncherDiscoveryAsync();
            _ = RefreshRootState(persist: false);
            _deleteSelectedEntry = null;
            _pendingDeleteEntry = null;
            _pendingCleanupRequest = null;
            _pendingDeleteAction = PendingDeleteAction.None;
            _cleanupCheckGeneration++;
            DeleteInstanceListBox.ItemsSource = _instanceChoices;
            DeleteInstanceListBox.SelectedIndex = -1;
            DeleteManagedFilesButton.IsEnabled = false;
            DeleteRemainingDataButton.IsEnabled = false;
            DeleteInstanceDetails.Text = _instanceChoices.Count == 0
                ? LocalizedText.Get("UiNoInstancesToDelete")
                : string.Empty;
        }
        catch (Exception ex) when (ex is InstallerException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            _deleteSelectedEntry = null;
            _pendingDeleteEntry = null;
            _pendingCleanupRequest = null;
            _pendingDeleteAction = PendingDeleteAction.None;
            _cleanupCheckGeneration++;
            DeleteInstanceListBox.ItemsSource = null;
            DeleteManagedFilesButton.IsEnabled = false;
            DeleteRemainingDataButton.IsEnabled = false;
            DeleteInstanceDetails.Text = LocalizedText.Get("UiNoInstancesToDelete") + Environment.NewLine + ex.Message;
        }
        DisplayDeleteInstanceOverlay();
    }

    private void DisplayDeleteInstanceOverlay()
    {
        MainContentScrollViewer.IsEnabled = false;
        DeleteInstanceOverlay.IsEnabled = true;
        DeleteInstanceOverlay.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (DeleteInstanceOverlay.Visibility == Visibility.Visible && DeleteInstanceOverlay.IsEnabled)
                DeleteInstanceListBox.Focus();
        }));
    }

    internal void ShowDeleteOverlayForUiSmoke(params InstalledInstanceEntry[] entries)
    {
        _instanceChoices = entries.Select(entry => new InstanceChoice(entry, entry.DirectoryName)).ToArray();
        DeleteInstanceListBox.ItemsSource = _instanceChoices;
        DeleteInstanceListBox.SelectedIndex = -1;
        _deleteSelectedEntry = entries.FirstOrDefault();
        DeleteManagedFilesButton.IsEnabled = _deleteSelectedEntry is { IsTrusted: true, Release: not null };
        DeleteRemainingDataButton.IsEnabled = false;
        DisplayDeleteInstanceOverlay();
    }

    internal int BeginCleanupPreparationForUiSmoke(InstalledInstanceEntry entry)
    {
        _deleteSelectedEntry = entry;
        return ++_cleanupCheckGeneration;
    }

    internal bool ApplyCleanupFailureForUiSmoke(InstalledInstanceEntry entry, int generation, string message) =>
        ApplyCleanupPreparationFailure(entry, generation, message);

    internal void ShowResidueConfirmationForUiSmoke(InstalledInstanceEntry entry)
    {
        _deleteSelectedEntry = entry;
        _pendingDeleteEntry = entry;
        _pendingDeleteAction = PendingDeleteAction.RemoveResidue;
        _pendingCleanupRequest = null;
        ShowUninstallConfirmation();
    }

    internal bool HasPendingDeleteActionForUiSmoke => _pendingDeleteAction != PendingDeleteAction.None;

    private void DeleteInstanceOverlay_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape || UninstallConfirmOverlay.Visibility == Visibility.Visible) return;
        e.Handled = true;
        CloseDeleteInstanceOverlay();
    }

    private void DeleteInstanceOverlay_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || UninstallConfirmOverlay.Visibility == Visibility.Visible ||
            IsWithinCard(e.OriginalSource as DependencyObject, DeleteInstanceCard)) return;
        CloseDeleteInstanceOverlay();
        e.Handled = true;
    }

    private void DeleteOverlayCancel_Click(object sender, RoutedEventArgs e) => CloseDeleteInstanceOverlay();

    private void CloseDeleteInstanceOverlay()
    {
        DeleteInstanceOverlay.Visibility = Visibility.Collapsed;
        DeleteInstanceOverlay.IsEnabled = true;
        MainContentScrollViewer.IsEnabled = true;
        _deleteSelectedEntry = null;
        _pendingDeleteEntry = null;
        _pendingCleanupRequest = null;
        _pendingDeleteAction = PendingDeleteAction.None;
        _cleanupCheckGeneration++;
        DeleteInstanceListBox.SelectedIndex = -1;
        Keyboard.Focus(UninstallButton);
    }

    private bool IsCurrentDeleteSelection(InstalledInstanceEntry selected, int generation) =>
        generation == _cleanupCheckGeneration && DeleteInstanceOverlay.Visibility == Visibility.Visible &&
        ReferenceEquals(selected, _deleteSelectedEntry);

    private bool ApplyCleanupPreparationFailure(InstalledInstanceEntry selected, int generation, string message)
    {
        if (!IsCurrentDeleteSelection(selected, generation) || UninstallConfirmOverlay.Visibility == Visibility.Visible)
            return false;
        DeleteRemainingDataButton.IsEnabled = false;
        var targetPath = selected.Layout?.IsPrism == true ? selected.Layout.InstanceDirectory : selected.Path;
        var displayRelease = GetDisplayRelease(selected);
        var release = displayRelease is null ? LocalizedText.Get("UiUnknownInstance") :
            $"{displayRelease.PackName} {displayRelease.PackVersion} ({displayRelease.MinecraftVersion})";
        DeleteInstanceDetails.Text = LocalizedText.Get("UiDeleteEntryDetails", release,
            LocalizedText.Get(selected.Layout?.IsPrism == true ? "UiLauncherOptionPrism" : "UiLauncherOptionOfficial"),
            LocalizedText.Get("UiInstanceResidue"), targetPath, message);
        return true;
    }

    private async void DeleteInstanceListBox_SelectionChanged(object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _deleteSelectedEntry = (DeleteInstanceListBox.SelectedItem as InstanceChoice)?.Entry;
        var selected = _deleteSelectedEntry;
        DeleteManagedFilesButton.IsEnabled = selected is { IsTrusted: true, Release: not null };
        DeleteRemainingDataButton.IsEnabled = false;
        var generation = ++_cleanupCheckGeneration;
        if (selected is null)
        {
            DeleteInstanceDetails.Text = _instanceChoices.Count == 0
                ? LocalizedText.Get("UiNoInstancesToDelete") : string.Empty;
            return;
        }
        var state = LocalizedText.Get(selected.State switch
        {
            InstalledInstanceState.Trusted => IsActiveInstance(selected) ? "UiInstanceActive" : "UiInstanceInactive",
            InstalledInstanceState.PackageMissing => "UiInstancePackageMissing",
            InstalledInstanceState.Residue => "UiInstanceResidue",
            InstalledInstanceState.UnknownRelease => "UiInstanceUnknown",
            InstalledInstanceState.Invalid => "UiInstanceInvalid",
            _ => "UiInstanceUnsafe"
        });
        var launcher = LocalizedText.Get(selected.Layout?.IsPrism == true
            ? "UiLauncherOptionPrism" : "UiLauncherOptionOfficial");
        var displayRelease = GetDisplayRelease(selected);
        var release = displayRelease is null ? LocalizedText.Get("UiUnknownInstance") :
            $"{displayRelease.PackName} {displayRelease.PackVersion} ({displayRelease.MinecraftVersion})";
        var targetPath = selected.Layout?.IsPrism == true ? selected.Layout.InstanceDirectory : selected.Path;
        DeleteInstanceDetails.Text = LocalizedText.Get("UiDeleteEntryDetails", release, launcher, state,
            targetPath, LocalizedText.Get("UiDeleteCleanupChecking"));
        try
        {
            var request = await Task.Run(() => InstanceRemovalService.PrepareCleanup(selected,
                GetEntryRoot(selected), AppContext.BaseDirectory, _launcher.LauncherRoot));
            if (!IsCurrentDeleteSelection(selected, generation)) return;
            DeleteRemainingDataButton.IsEnabled = true;
            DeleteInstanceDetails.Text = LocalizedText.Get("UiDeleteEntryDetails", release, launcher, state,
                targetPath, LocalizedText.Get("UiDeleteCleanupEligible"));
        }
        catch (Exception ex) when (ex is InstallerException or IOException or UnauthorizedAccessException or
                                       ArgumentException or NotSupportedException or System.Text.Json.JsonException or
                                       FormatException or InvalidOperationException or KeyNotFoundException)
        {
            if (!IsCurrentDeleteSelection(selected, generation)) return;
            DeleteInstanceDetails.Text = LocalizedText.Get("UiDeleteEntryDetails", release, launcher, state,
                targetPath, ex.Message);
        }
    }

    private void DeleteManagedFiles_Click(object sender, RoutedEventArgs e)
    {
        var selected = _deleteSelectedEntry;
        if (selected is not { IsTrusted: true, Release: not null }) return;
        _pendingDeleteEntry = selected;
        _pendingDeleteAction = PendingDeleteAction.Uninstall;
        _pendingCleanupRequest = null;
        UninstallConfirmTitle.Text = LocalizedText.Get("UiUninstallConfirmTitle");
        UninstallConfirmMessage.Text = LocalizedText.Get("UiUninstallConfirmMessage");
        UninstallConfirmDetails.Text = LocalizedText.Get("UiUninstallConfirmTarget", selected.Release.PackName,
            selected.Release.PackVersion, selected.Path);
        UninstallConfirmButton.Content = LocalizedText.Get("UiUninstall");
        ShowUninstallConfirmation();
    }

    private async void DeleteRemainingData_Click(object sender, RoutedEventArgs e)
    {
        var selected = _deleteSelectedEntry;
        if (selected is null) return;
        var generation = ++_cleanupCheckGeneration;
        DeleteRemainingDataButton.IsEnabled = false;
        DeleteInstanceDetails.Text = LocalizedText.Get("UiDeleteCleanupChecking");
        InstanceCleanupRequest request;
        try
        {
            request = await Task.Run(() => InstanceRemovalService.PrepareCleanup(selected,
                GetEntryRoot(selected), AppContext.BaseDirectory, _launcher.LauncherRoot));
        }
        catch (Exception ex) when (ex is InstallerException or IOException or UnauthorizedAccessException or
                                       ArgumentException or NotSupportedException or System.Text.Json.JsonException or
                                       FormatException or InvalidOperationException or KeyNotFoundException)
        {
            ApplyCleanupPreparationFailure(selected, generation, ex.Message);
            return;
        }
        if (!IsCurrentDeleteSelection(selected, generation)) return;
        _pendingDeleteEntry = selected;
        _pendingCleanupRequest = request;
        _pendingDeleteAction = PendingDeleteAction.RemoveResidue;
        UninstallConfirmTitle.Text = LocalizedText.Get("UiDeleteRemainingConfirmTitle");
        UninstallConfirmMessage.Text = LocalizedText.Get("UiDeleteRemainingConfirmMessage");
        UninstallConfirmDetails.Text = LocalizedText.Get("UiDeleteRemainingConfirmTarget",
            LocalizedText.Get(selected.Layout?.IsPrism == true ? "UiLauncherOptionPrism" : "UiLauncherOptionOfficial"),
            GetDisplayRelease(selected) is not { } displayRelease ? LocalizedText.Get("UiUnknownInstance") :
                $"{displayRelease.PackName} {displayRelease.PackVersion}",
            FormatCleanupCategories(request.Categories), request.TargetPath);
        UninstallConfirmButton.Content = LocalizedText.Get("UiDeleteRemainingData");
        ShowUninstallConfirmation();
    }

    private void ShowUninstallConfirmation()
    {
        var generation = ++_uninstallConfirmationGeneration;
        _uninstallReturnFocus = _pendingDeleteAction == PendingDeleteAction.RemoveResidue
            ? DeleteRemainingDataButton.IsEnabled ? DeleteRemainingDataButton : DeleteInstanceListBox
            : DeleteManagedFilesButton.IsEnabled ? DeleteManagedFilesButton : DeleteInstanceListBox;
        DeleteInstanceOverlay.IsEnabled = false;
        MainContentScrollViewer.IsEnabled = false;
        UninstallConfirmOverlay.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (generation == _uninstallConfirmationGeneration &&
                UninstallConfirmOverlay.Visibility == Visibility.Visible)
                UninstallCancelButton.Focus();
        }));
    }

    private static string FormatCleanupCategories(IReadOnlyList<string> categories) => categories.Count == 0
        ? LocalizedText.Get("UiCleanupCategoryNone")
        : string.Join(", ", categories.Select(key => LocalizedText.Get(key)));

    private void UninstallConfirmOverlay_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        CloseUninstallConfirmation();
    }

    private void UninstallConfirmOverlay_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || IsWithinCard(e.OriginalSource as DependencyObject, UninstallConfirmCard)) return;
        CloseUninstallConfirmation();
        e.Handled = true;
    }

    private void UninstallCancel_Click(object sender, RoutedEventArgs e) => CloseUninstallConfirmation();

    private async void UninstallConfirm_Click(object sender, RoutedEventArgs e)
    {
        var selected = _pendingDeleteEntry;
        var action = _pendingDeleteAction;
        var cleanupRequest = _pendingCleanupRequest;
        _pendingCleanupRequest = null;
        CloseUninstallConfirmation();
        DeleteInstanceOverlay.Visibility = Visibility.Collapsed;
        MainContentScrollViewer.IsEnabled = true;
        _deleteSelectedEntry = null;
        if (selected is null) return;
        if (action == PendingDeleteAction.Uninstall)
            await RunOperationAsync(Operation.Uninstall, operationTarget: selected);
        else
            await RunOperationAsync(Operation.RemoveResidue, operationTarget: selected, cleanupRequest: cleanupRequest);
    }

    private void CloseUninstallConfirmation()
    {
        _uninstallConfirmationGeneration++;
        UninstallConfirmOverlay.Visibility = Visibility.Collapsed;
        _pendingCleanupRequest = null;
        _pendingDeleteEntry = null;
        _pendingDeleteAction = PendingDeleteAction.None;
        DeleteInstanceOverlay.IsEnabled = true;
        var deleteOverlayVisible = DeleteInstanceOverlay.Visibility == Visibility.Visible;
        MainContentScrollViewer.IsEnabled = !deleteOverlayVisible;
        var returnFocus = _uninstallReturnFocus ?? UninstallButton;
        _uninstallReturnFocus = null;
        Keyboard.Focus(returnFocus);
    }

    private static bool IsWithinCard(DependencyObject? source, DependencyObject card)
    {
        if (source is null) return false;
        var pending = new Stack<DependencyObject>();
        var visited = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);
        pending.Push(source);
        while (pending.TryPop(out var current))
        {
            if (!visited.Add(current)) continue;
            if (ReferenceEquals(current, card)) return true;
            if (current is ComboBoxItem item && ItemsControl.ItemsControlFromItemContainer(item) is { } owner)
                pending.Push(owner);
            if (current is Popup popup && popup.PlacementTarget is { } placementTarget)
                pending.Push(placementTarget);
            if (current is ContentElement content)
            {
                if (ContentOperations.GetParent(content) is { } contentParent) pending.Push(contentParent);
                if ((content as FrameworkContentElement)?.Parent is { } frameworkParent) pending.Push(frameworkParent);
                continue;
            }
            DependencyObject? visualParent = null;
            try { visualParent = VisualTreeHelper.GetParent(current); }
            catch (InvalidOperationException) { }
            if (visualParent is not null) pending.Push(visualParent);
            if (LogicalTreeHelper.GetParent(current) is { } logicalParent) pending.Push(logicalParent);
        }
        return false;
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
        _activeMarker = null;
        _gameDirectory = null;
        _currentOperationLog = null;
        RootAvailabilityText.Text = LocalizedText.Get("UiRootNeedsValidation");
    }

    private void InstallRootBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        _ = RefreshRootState(persist: true);

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
                    InstallerPreferences.SaveLastValidatedRoot(_preferencesPath, root,
                        _prismScan.State == LauncherDiscoveryState.Unknown ? null : _prismTargets,
                        _prismScan.State == LauncherDiscoveryState.Unknown ? _savedPrismHints : null);
                    _missingSavedRootPath = null;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InstallerException)
                {
                    ShowInlineNotice(LocalizedText.Get("UiPreferencesNotSaved"));
                }
            }

            _activeMarker = null;
            var pending = _installer.GetPendingOperation(root);
            if (Directory.Exists(root) && pending is null) _activeMarker = _installer.InspectActiveMarker(root);
            var allEntries = new List<InstalledInstanceEntry>(_prismInstanceEntries);
            if (Directory.Exists(root)) allEntries.AddRange(InstalledInstanceCatalog.Enumerate(root, AppContext.BaseDirectory));
            _instanceChoices = BuildInstanceChoices(allEntries);
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

            if (DeleteInstanceOverlay.Visibility == Visibility.Visible)
                DeleteInstanceListBox.ItemsSource = _instanceChoices;
            return root;
        }
        catch (InstallerException ex)
        {
            _activeMarker = null;
            ApplyPrismEntriesWithoutOfficialRoot();
            RootAvailabilityText.Text = ex.Code == "ROOT_UNSAFE" || ex.Code == "VANILLA_PATH_BLOCKED"
                ? ex.Message : LocalizedText.Get("UiSavedRootUnavailable");
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _activeMarker = null;
            ApplyPrismEntriesWithoutOfficialRoot();
            RootAvailabilityText.Text = LocalizedText.Get("UiSavedRootUnavailable");
            return null;
        }
    }

    private void ApplyPrismEntriesWithoutOfficialRoot()
    {
        _instanceChoices = BuildInstanceChoices(_prismInstanceEntries);
        if (DeleteInstanceOverlay.Visibility == Visibility.Visible)
            DeleteInstanceListBox.ItemsSource = _instanceChoices;
    }

    private IReadOnlyList<InstanceChoice> BuildInstanceChoices(IEnumerable<InstalledInstanceEntry> entries)
    {
        var unique = entries.DistinctBy(entry => Path.GetFullPath(entry.Path), StringComparer.OrdinalIgnoreCase).ToArray();
        var duplicateGroups = unique.GroupBy(entry =>
        {
            var launcher = entry.Layout is { IsPrism: true } layout ? layout.Fingerprint : "official";
            var release = GetDisplayRelease(entry)?.PackVersion ??
                          Path.GetFileName(entry.Layout?.InstanceDirectory ?? entry.Path);
            return launcher + "|" + release;
        }, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1)
          .SelectMany(group => group.OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
              .Select((entry, index) => (Path: entry.Path, Ordinal: index + 1)))
          .ToDictionary(item => item.Path, item => item.Ordinal, StringComparer.OrdinalIgnoreCase);
        return unique.Select(entry => new InstanceChoice(entry,
            FormatInstanceChoice(entry, duplicateGroups.TryGetValue(entry.Path, out var ordinal) ? ordinal : null))).ToArray();
    }

    private string FormatInstanceChoice(InstalledInstanceEntry entry, int? duplicateOrdinal)
    {
        var release = GetDisplayRelease(entry);
        var name = release is null ? LocalizedText.Get("UiUnknownInstance") :
            $"{release.PackName} {release.PackVersion} · {release.MinecraftVersion}";
        var launcherName = LocalizedText.Get(entry.Layout?.IsPrism == true
            ? "PrismInstanceTargetDisplay" : "OfficialInstanceTargetDisplay", name);
        var state = entry.State switch
        {
            InstalledInstanceState.Trusted => IsActiveInstance(entry) ? "UiInstanceActive" : "UiInstanceInactive",
            InstalledInstanceState.PackageMissing => "UiInstancePackageMissing",
            InstalledInstanceState.Residue => "UiInstanceResidue",
            InstalledInstanceState.UnknownRelease => "UiInstanceUnknown",
            InstalledInstanceState.Invalid => "UiInstanceInvalid",
            _ => "UiInstanceUnsafe"
        };
        state = LocalizedText.Get(state);
        var ordinal = duplicateOrdinal is null ? "" : " · " + LocalizedText.Get("UiInstanceOrdinal", duplicateOrdinal.Value);
        return $"{launcherName}{ordinal} — {state}";
    }

    private static KnownPackRelease? GetDisplayRelease(InstalledInstanceEntry entry)
    {
        if (entry.Release is not null) return entry.Release;
        var directoryName = Path.GetFileName(entry.Layout?.InstanceDirectory ?? entry.Path);
        return InstanceDirectoryNaming.TryGetReleaseFromName(directoryName, out var release) ? release : null;
    }

    private InstalledInstanceEntry? GetSelectedEntry()
    {
        if (_operationTargetEntry is not null) return _operationTargetEntry;
        var active = _instanceChoices.Select(choice => choice.Entry)
            .Where(entry => entry.IsTrusted && IsActiveInstance(entry)).ToArray();
        return active.Length == 1 ? active[0] : null;
    }

    private InstalledInstanceEntry RequireTrustedSelected(string root)
    {
        var requested = _operationTargetEntry;
        var selected = requested is null ? GetSelectedEntry() : _instanceChoices.Select(choice => choice.Entry)
            .SingleOrDefault(entry => SamePath(entry.Path, requested.Path) &&
                (entry.Layout?.Fingerprint ?? "official").Equals(requested.Layout?.Fingerprint ?? "official",
                    StringComparison.OrdinalIgnoreCase));
        if (selected is not { IsTrusted: true } || !IsUnderSelectedRoot(root, selected))
            throw new InstallerException("INSTANCE_UNTRUSTED", LocalizedText.Get("UiSelectTrustedInstance"));
        if (selected.Layout is { IsPrism: true } prismLayout)
        {
            prismLayout.Validate();
            LauncherDiscovery.RevalidatePrismTarget(ToPrismTarget(prismLayout));
        }
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

    private string GetEntryRoot(InstalledInstanceEntry entry) => entry.Layout?.IsPrism == true
        ? entry.Layout.StateRoot
        : Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(entry.Path)))!;

    private async Task<InstalledInstanceEntry?> ResolveActiveOperationTargetAsync(bool allowSingleTrustedFallback = false)
    {
        LauncherInventory inventory;
        try
        {
            inventory = await ScanLaunchersAsync();
            ApplyLauncherInventory(inventory);
            _ = RefreshRootState(persist: false);
        }
        catch (Exception ex) when (ex is InstallerException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            ShowInlineNotice(ex.Message);
            return null;
        }

        var hasUnknown = inventory.OfficialUnknown || inventory.PrismScan.State == LauncherDiscoveryState.Unknown;
        LauncherInstallSelection? selection;
        if (!hasUnknown && inventory.OfficialTargets.Count == 1 &&
            inventory.PrismScan.State == LauncherDiscoveryState.Absent)
            selection = MakeAutomaticSelection(LauncherKind.Official, inventory);
        else if (!hasUnknown && inventory.PrismScan.State == LauncherDiscoveryState.Found &&
                 inventory.PrismScan.PrismTargets.Count == 1 && inventory.OfficialTargets.Count == 0)
            selection = MakeAutomaticSelection(LauncherKind.Prism, inventory);
        else if (!hasUnknown && inventory.OfficialTargets.Count == 0 && inventory.PrismScan.State == LauncherDiscoveryState.Absent)
        {
            ShowInlineNotice(LocalizedText.Get("UiNoActiveInstallation"));
            return null;
        }
        else
            selection = await ShowLauncherChoiceAsync(inventory, null, LocalizedText.Get("UiChooseActiveLauncher"));

        if (selection is null) return null;
        string? activePath;
        if (selection.Kind == LauncherKind.Prism && selection.PrismTarget is { } prismTarget)
        {
            var layout = InstallationLayout.Prism(prismTarget, "minepack-target-validation");
            var marker = _installer.InspectActiveMarker(layout);
            activePath = marker.State == ActiveMarkerState.Valid ? marker.InstancePath : null;
            var entry = activePath is null ? null : _instanceChoices.Select(choice => choice.Entry).SingleOrDefault(candidate =>
                    candidate.IsTrusted && candidate.Layout is { IsPrism: true } installedLayout &&
                    installedLayout.Fingerprint.Equals(layout.Fingerprint, StringComparison.OrdinalIgnoreCase) &&
                    SamePath(candidate.Path, activePath));
            if (entry is not null) return entry;
            if (allowSingleTrustedFallback && marker.State is
                (ActiveMarkerState.Absent or ActiveMarkerState.Malformed or ActiveMarkerState.MissingTarget))
            {
                var candidates = _instanceChoices.Select(choice => choice.Entry)
                    .Where(candidate => candidate.IsTrusted && candidate.Layout is { IsPrism: true } candidateLayout &&
                        candidateLayout.Fingerprint.Equals(layout.Fingerprint, StringComparison.OrdinalIgnoreCase) &&
                        candidate.Release!.PackName.Equals(SelectedPackName, StringComparison.Ordinal))
                    .ToArray();
                if (candidates.Length == 1) return candidates[0];
            }
        }
        else
        {
            var root = RefreshRootState(persist: false);
            if (root is not null)
            {
                var marker = _installer.InspectActiveMarker(root);
                activePath = marker.State == ActiveMarkerState.Valid ? marker.InstancePath : null;
                var entry = activePath is null ? null : _instanceChoices.Select(choice => choice.Entry).SingleOrDefault(candidate =>
                        candidate.IsTrusted && candidate.Layout is null && SamePath(candidate.Path, activePath) &&
                        IsUnderSelectedRoot(root, candidate));
                if (entry is not null) return entry;
                if (allowSingleTrustedFallback && marker.State is
                    (ActiveMarkerState.Absent or ActiveMarkerState.Malformed or ActiveMarkerState.MissingTarget))
                {
                    var candidates = _instanceChoices.Select(choice => choice.Entry)
                        .Where(candidate => candidate.IsTrusted && candidate.Layout is null &&
                            IsUnderSelectedRoot(root, candidate) &&
                            candidate.Release!.PackName.Equals(SelectedPackName, StringComparison.Ordinal)).ToArray();
                    if (candidates.Length == 1) return candidates[0];
                }
            }
        }

        ShowInlineNotice(LocalizedText.Get("UiNoActiveInstallation"));
        return null;
    }

    private static bool IsUnderSelectedRoot(string root, InstalledInstanceEntry entry)
    {
        if (entry.Layout is { IsPrism: true } layout)
        {
            try
            {
                layout.Validate();
                return SamePath(entry.Path, layout.GameDirectory) &&
                       SamePath(Path.GetDirectoryName(layout.GameDirectory), layout.InstanceDirectory);
            }
            catch (InstallerException) { return false; }
        }
        return Path.GetDirectoryName(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(entry.Path)))) is { } parent &&
               SamePath(parent, root);
    }

    private bool IsActiveInstance(InstalledInstanceEntry entry) => entry.Layout?.IsPrism == true
        ? _activePrismInstancePaths.Any(path => SamePath(path, entry.Path))
        : SamePath(entry.Path, _activeMarker?.InstancePath);

    private static PrismLauncherTarget ToPrismTarget(InstallationLayout layout) =>
        new(layout.LauncherIdentity, layout.PrismDataRoot!, layout.InstancesRoot, layout.Fingerprint, "owned-instance");

    private static bool SamePath(string? first, string? second) => first is not null && second is not null &&
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)).Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)), StringComparison.OrdinalIgnoreCase);

    private async Task RunOperationAsync(Operation operation, string? sourceWorlds = null,
        InstalledInstanceEntry? operationTarget = null, InstanceCleanupRequest? cleanupRequest = null)
    {
        if (_operationCancellation is not null) return;
        if ((operation is Operation.Repair or Operation.ConfigureLauncher or Operation.ImportWorlds) && operationTarget is null)
            operationTarget = await ResolveActiveOperationTargetAsync(allowSingleTrustedFallback: operation == Operation.ConfigureLauncher);
        if (operation is not Operation.Install && operationTarget is null)
        {
            if (operation is Operation.Uninstall or Operation.RemoveResidue)
                ShowInlineNotice(LocalizedText.Get("UiSelectTrustedInstance"));
            return;
        }
        _operationTargetEntry = operationTarget;
        var cancellation = new CancellationTokenSource();
        _operationCancellation = cancellation;
        _operationCanBeCancelled = operation is not (Operation.Uninstall or Operation.RemoveResidue);
        var selectedPackForLog = SelectedPack;
        var installedReleaseForLog = operation == Operation.Install ? null : operationTarget?.Release;
        var operationLog = new OperationLog(operation.ToString().ToLowerInvariant(), _installerVersion,
            InstallRootBox.Text, installedReleaseForLog?.PackVersion ?? selectedPackForLog.Version,
            installedReleaseForLog?.MinecraftVersion ?? TestPackRelease.MinecraftVersion,
            installedReleaseForLog?.FabricLoaderVersion ?? TestPackRelease.FabricLoaderVersion,
            installedReleaseForLog?.ArchiveSha512 ?? selectedPackForLog.Hash);
        _currentOperationLog = operationLog;
        BeginOperationPresentation(operation);
        var filesInstalled = false;
        var filesRemoved = false;
        var operationOutcome = "failed";
        string? operationFailureCode = null;
        MinecraftLauncherTarget? launcherTarget = null;
        PrismLauncherTarget? selectedPrismTarget = null;
        var prismOperationTarget = false;
        var prismRepairTarget = false;

        try
        {
            await OperationGuard.RunAsync(async () =>
            {
            var root = operationTarget is not null
                ? GetEntryRoot(operationTarget)
                : operation == Operation.Install ? null : RefreshRootState(persist: true);
            var selectedPack = SelectedPack;
            if (operation == Operation.Install && !File.Exists(selectedPack.Path))
                throw new InstallerException("PACK_NOT_FOUND", LocalizedText.Get("PublishedPackMissing"));

            LauncherInstallSelection? installSelection = null;
            if (operation == Operation.Install)
            {
                installSelection = await ResolveLauncherInstallSelectionAsync();
                if (installSelection is null)
                {
                    operationOutcome = "cancelled";
                    operationFailureCode = "CANCELLED";
                    StatusBox.Text = LocalizedText.Get("OperationStoppedStatus");
                    return;
                }
                selectedPrismTarget = installSelection.PrismTarget;
                prismOperationTarget = selectedPrismTarget is not null;
                if (selectedPrismTarget is not null)
                    root = InstallationLayout.Prism(selectedPrismTarget,
                        $"test-pack-{selectedPack.Version}-{selectedPack.Hash[..12].ToLowerInvariant()}").StateRoot;
                else
                    root = RefreshRootState(persist: false);
            }
            if (root is null) throw new InstallerException("ROOT_UNAVAILABLE", LocalizedText.Get("UiSavedRootUnavailable"));
            PresentOperationProgress();
            operationLog.Write("preflight", "started", "ui_operation_started");

            if (operation == Operation.ImportWorlds)
            {
                var importTarget = operationTarget;
                if (importTarget is not { IsTrusted: true } || !IsUnderSelectedRoot(root, importTarget))
                    throw new InstallerException("INSTANCE_UNTRUSTED", LocalizedText.Get("UiSelectTrustedInstance"));
                var worldProgress = new Progress<string>(message => ProgressLabel.Text = message);
                var imported = await Task.Run(async () =>
                {
                    if (importTarget.Layout is { IsPrism: true } prismLayout)
                    {
                        prismOperationTarget = true;
                        await _prismLauncherController.CloseBeforeInstallAsync(ToPrismTarget(prismLayout), cancellation.Token);
                        return await WorldImportService.ImportAsync(sourceWorlds!, prismLayout, worldProgress, cancellation.Token, operationLog);
                    }
                    return await WorldImportService.ImportAsync(sourceWorlds!, importTarget.Path, worldProgress, cancellation.Token, operationLog);
                }, cancellation.Token);
                StateHeading.Text = LocalizedText.Get("WorldImportComplete");
                StatusBox.Text = FormatWorldImportSummary(imported);
                InstructionsBox.Text = LocalizedText.Get("UiWorldImportSafetyTarget", importTarget.Release!.PackName,
                    importTarget.Release.PackVersion, importTarget.Path);
                ProgressLabel.Text = LocalizedText.Get("OperationComplete");
                if (importTarget.Layout?.IsPrism == true) await RefreshLauncherDiscoveryAsync();
                operationOutcome = "completed";
                return;
            }

            if (operation == Operation.RemoveResidue)
            {
                if (cleanupRequest is null || operationTarget is null ||
                    !SamePath(cleanupRequest.TargetPath, operationTarget.Layout?.InstanceDirectory ?? operationTarget.Path))
                    throw new InstallerException("INSTANCE_CLEANUP_CHANGED", LocalizedText.Get("InstanceCleanupChanged"));
                if (cleanupRequest.IsPrism && operationTarget.Layout is { IsPrism: true } prismLayout)
                {
                    LauncherDiscovery.RevalidatePrismTarget(ToPrismTarget(prismLayout));
                    await _prismLauncherController.CloseBeforeInstallAsync(ToPrismTarget(prismLayout), cancellation.Token);
                    prismOperationTarget = true;
                }
                var cleanupResult = await InstanceRemovalService.RemoveAsync(cleanupRequest,
                    RecycleBinService.MoveToRecycleBinAsync);
                filesRemoved = true;
                StateHeading.Text = LocalizedText.Get("UiDeleteRemainingComplete");
                StatusBox.Text = cleanupResult.OwnershipRecordRetained
                    ? LocalizedText.Get("UiDeleteOwnershipRecordRetained")
                    : LocalizedText.Get("UiDeleteRemainingComplete");
                InstructionsBox.Text = "";
                ProgressLabel.Text = LocalizedText.Get("OperationComplete");
                await RefreshLauncherDiscoveryAsync();
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

            InstallResult result;
            if (operation == Operation.ConfigureLauncher)
            {
                var target = RequireTrustedSelected(root);
                var release = target.Release!;
                var (archivePath, archiveHash) = InstalledInstanceCatalog.PinnedArchive(release.PackVersion, AppContext.BaseDirectory);
                if (!File.Exists(archivePath))
                    throw new InstallerException("PACK_NOT_FOUND", LocalizedText.Get("SelectedPackUnavailable"));

                if (target.Layout is { IsPrism: true } prismLayout)
                {
                    prismOperationTarget = true;
                    await Task.Run(() => _prismLauncherController.CloseBeforeInstallAsync(ToPrismTarget(prismLayout), cancellation.Token), cancellation.Token);
                    result = await Task.Run(() => _installer.InstallAsync(archivePath, archiveHash, prismLayout,
                        progress, cancellation.Token, operationLog), cancellation.Token);
                    if (result.Success)
                    {
                        filesInstalled = true;
                        _gameDirectory = result.GameDirectory;
                        StateHeading.Text = LocalizedText.Get("PackInstalled");
                        StatusBox.Text = result.Message;
                        InstructionsBox.Text = LocalizedText.Get("PrismLaunchInstruction", LauncherProfile.ProfileName(release.MinecraftVersion));
                        ProgressLabel.Text = LocalizedText.Get("OperationComplete");
                        await RefreshLauncherDiscoveryAsync();
                        operationOutcome = "completed";
                    }
                    else
                    {
                        operationFailureCode = result.Code;
                        operationOutcome = result.Code == "CANCELLED" ? "cancelled" : "failed";
                        StateHeading.Text = LocalizedText.Get(result.Code == "CANCELLED" ? "OperationCancelledHeading" : "OperationFailedHeading");
                        StatusBox.Text = result.Code == "CANCELLED" ? LocalizedText.Get("OperationStoppedStatus") : result.Message;
                        DiagnosticText.Text = FormatDiagnostic(result.Code, operationLog.CurrentLogPath);
                        ProgressLabel.Text = LocalizedText.Get(result.Code == "CANCELLED" ? "OperationCancelledHeading" : "OperationFailedProgress");
                    }
                    return;
                }

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
                        StateHeading.Text = LocalizedText.Get("OperationCancelledHeading");
                        StatusBox.Text = LocalizedText.Get("OperationStoppedStatus");
                        ProgressLabel.Text = LocalizedText.Get("OperationCancelledHeading");
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

            var configureRepairedTarget = false;
            if (operation == Operation.Install)
            {
                if (installSelection?.PrismTarget is { } prismTarget)
                {
                    operationLog.Write("preflight", "started", "launcher_choice_confirmed",
                        new { launcherKind = "Prism", targetFingerprint = prismTarget.Fingerprint });
                    await Task.Run(() => _prismLauncherController.CloseBeforeInstallAsync(prismTarget, cancellation.Token), cancellation.Token);
                    result = await Task.Run(() => _installer.InstallAsync(selectedPack.Path, selectedPack.Hash, prismTarget,
                        progress, cancellation.Token, operationLog), cancellation.Token);
                }
                else
                {
                var officialTarget = installSelection?.OfficialTarget
                    ?? throw new InstallerException("LAUNCHER_TARGET_UNAVAILABLE", LocalizedText.Get("LauncherDiscoveryFailed"));
                operationLog.Write("preflight", "started", "launcher_choice_confirmed",
                    new { launcherKind = "Official", targetIdentity = officialTarget.Identity });
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
                launcherTarget = await Task.Run(() => _launcherController.CloseBeforeInstallAsync(officialTarget, cancellation.Token, operationLog), cancellation.Token);
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
            }
            else if (operation == Operation.Uninstall)
            {
                var uninstallTarget = RequireTrustedSelected(root);
                var uninstallLayout = uninstallTarget.Layout;
                if (uninstallLayout is { IsPrism: true } prismLayout)
                {
                    prismOperationTarget = true;
                    await Task.Run(() => _prismLauncherController.CloseBeforeInstallAsync(ToPrismTarget(prismLayout), cancellation.Token), cancellation.Token);
                }
                result = await Task.Run(async () =>
                {
                    var pending = uninstallLayout is { IsPrism: true } ? _installer.GetPendingOperation(uninstallLayout) : _installer.GetPendingOperation(root);
                    if (pending is not null && !SamePath(pending.Value.InstancePath, uninstallTarget.Path))
                        throw new InstallerException("TRANSACTION_RECOVERY_REQUIRED",
                            LocalizedText.Get("TransactionRecoveryRequired", pending.Value.InstancePath));
                    var (packPath, packHash) = InstalledInstanceCatalog.PinnedArchive(uninstallTarget.Release!.PackVersion, AppContext.BaseDirectory);
                    if (uninstallLayout is { IsPrism: true })
                        return await _installer.UninstallAsync(uninstallLayout, packPath, packHash, operationLog);
                    var active = _installer.GetActiveInstancePath(root);
                    var launcher = SamePath(active, uninstallTarget.Path) ? _launcher : null;
                    return await _installer.UninstallAsync(root, uninstallTarget.Path, packPath, packHash, launcher, operationLog);
                }, cancellation.Token);
            }
            else
            {
                var selectedBeforeRecovery = GetSelectedEntry();
                var selectedLayout = selectedBeforeRecovery?.Layout;
                if (selectedLayout is { IsPrism: true } prismLayout)
                {
                    prismOperationTarget = true;
                    await Task.Run(() => _prismLauncherController.CloseBeforeInstallAsync(ToPrismTarget(prismLayout), cancellation.Token), cancellation.Token);
                }
                else if (root is null)
                    throw new InstallerException("ROOT_UNAVAILABLE", LocalizedText.Get("UiSavedRootUnavailable"));

                var pending = selectedLayout is { IsPrism: true }
                    ? _installer.GetPendingOperation(selectedLayout)
                    : _installer.GetPendingOperation(root!);
                var selectedTargetPath = selectedBeforeRecovery?.Path;
                if (pending is not null)
                {
                    if (selectedTargetPath is not null && !SamePath(pending.Value.InstancePath, selectedTargetPath))
                        throw new InstallerException("TRANSACTION_RECOVERY_REQUIRED",
                            LocalizedText.Get("TransactionRecoveryRequired", pending.Value.InstancePath));
                    if (selectedLayout is { IsPrism: true } pendingPrismLayout &&
                        !SamePath(pending.Value.InstancePath, pendingPrismLayout.GameDirectory))
                        throw new InstallerException("TRANSACTION_RECOVERY_REQUIRED",
                            LocalizedText.Get("TransactionRecoveryRequired", pending.Value.InstancePath));

                    fileProgressPhase = "pending-recovery";
                    OperationProgress.Value = 0;
                    OperationProgress.IsIndeterminate = true;
                    ProgressLabel.Text = LocalizedText.Get("SearchingPack");
                    var (pendingArchive, pendingHash) = InstalledInstanceCatalog.PinnedArchive(pending.Value.PackVersion, AppContext.BaseDirectory);
                    var recovered = selectedLayout is { IsPrism: true }
                        ? await Task.Run(() => _installer.RepairAsync(selectedLayout, pendingArchive, pendingHash,
                            progress, cancellation.Token, operationLog), cancellation.Token)
                        : await Task.Run(() => _installer.RepairAsync(pending.Value.InstancePath,
                            pendingArchive, pendingHash, progress, cancellation.Token, _launcher, operationLog), cancellation.Token);
                    if (!recovered.Success) result = recovered;
                    else
                    {
                        selectedTargetPath = pending.Value.InstancePath;
                        fileProgressPhase = "selected-files";
                        OperationProgress.Value = 0;
                        OperationProgress.IsIndeterminate = true;
                        ProgressLabel.Text = LocalizedText.Get("PreparingFiles");
                        if (selectedLayout is { IsPrism: true }) await RefreshLauncherDiscoveryAsync();
                        else _ = RefreshRootState(persist: false);
                        var selectedRoot = selectedLayout is { IsPrism: true } ? selectedLayout.StateRoot : root!;
                        var refreshed = RequireTrustedSelected(selectedRoot);
                        if (!SamePath(refreshed.Path, selectedTargetPath))
                            throw new InstallerException("INSTANCE_UNTRUSTED", LocalizedText.Get("UiSelectTrustedInstance"));
                        if (selectedLayout is { IsPrism: true } &&
                            (refreshed.Layout is not { IsPrism: true } refreshedLayout ||
                             !refreshedLayout.Fingerprint.Equals(selectedLayout.Fingerprint, StringComparison.OrdinalIgnoreCase)))
                            throw new InstallerException("PRISM_TARGET_CHANGED", LocalizedText.Get("PrismIdentityChanged"));
                        var (installedPackPath, installedPackHash) = InstalledInstanceCatalog.PinnedArchive(refreshed.Release!.PackVersion, AppContext.BaseDirectory);
                        if (refreshed.Layout is { IsPrism: true } repairedLayout)
                        {
                            prismRepairTarget = true;
                            result = await Task.Run(() => _installer.RepairAsync(repairedLayout, installedPackPath, installedPackHash,
                                progress, cancellation.Token, operationLog), cancellation.Token);
                        }
                        else
                        {
                            var active = _installer.GetActiveInstancePath(root);
                            configureRepairedTarget = SamePath(active, refreshed.Path);
                            result = await Task.Run(() => _installer.RepairAsync(refreshed.Path, installedPackPath, installedPackHash,
                                progress, cancellation.Token, configureRepairedTarget ? _launcher : null, operationLog), cancellation.Token);
                        }
                    }
                }
                else
                {
                    var selected = RequireTrustedSelected(selectedLayout is { IsPrism: true } prismSelectedLayout
                        ? prismSelectedLayout.StateRoot
                        : root!);
                    selectedLayout = selected.Layout;
                    var (installedPackPath, installedPackHash) = InstalledInstanceCatalog.PinnedArchive(selected.Release!.PackVersion, AppContext.BaseDirectory);
                    if (selectedLayout is { IsPrism: true })
                    {
                        prismRepairTarget = true;
                        result = await Task.Run(() => _installer.RepairAsync(selectedLayout, installedPackPath, installedPackHash,
                            progress, cancellation.Token, operationLog), cancellation.Token);
                    }
                    else
                    {
                        var active = _installer.GetActiveInstancePath(root);
                        configureRepairedTarget = SamePath(active, selected.Path);
                        result = await Task.Run(() => _installer.RepairAsync(selected.Path, installedPackPath, installedPackHash,
                            progress, cancellation.Token, configureRepairedTarget ? _launcher : null, operationLog), cancellation.Token);
                    }
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
                        if (selectedPrismTarget is not null)
                        {
                            try
                            {
                                await Task.Run(() => _prismLauncherController.Start(selectedPrismTarget), cancellation.Token);
                                StatusBox.Text = LocalizedText.Get("PrismStartRequestedStatus",
                                    LauncherProfile.ProfileName(selectedPack.MinecraftVersion));
                                InstructionsBox.Text = LocalizedText.Get("PrismLaunchInstruction",
                                    LauncherProfile.ProfileName(selectedPack.MinecraftVersion));
                            }
                            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or Win32Exception)
                            {
                                operationOutcome = "profile_pending";
                                StatusBox.Text = LocalizedText.Get("PrismStartFailedStatus",
                                    LauncherProfile.ProfileName(selectedPack.MinecraftVersion));
                                InstructionsBox.Text = LocalizedText.Get("PrismLaunchInstruction",
                                    LauncherProfile.ProfileName(selectedPack.MinecraftVersion));
                                DiagnosticText.Text = FormatDiagnostic("PRISM_START_FAILED", operationLog.CurrentLogPath);
                            }
                        }
                        else
                        {
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
                    }
                    else if (operation == Operation.Repair && prismRepairTarget)
                    {
                        StateHeading.Text = LocalizedText.Get("PrismRepairCompleteStatus");
                        StatusBox.Text = result.Message;
                        InstructionsBox.Text = "";
                        ProgressLabel.Text = LocalizedText.Get("OperationComplete");
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
                    if (prismOperationTarget) await RefreshLauncherDiscoveryAsync();
                    else _ = RefreshRootState(persist: false);
                }
                else
                {
                    filesRemoved = true;
                    StateHeading.Text = LocalizedText.Get("PackUninstalled");
                    ProgressLabel.Text = LocalizedText.Get("OperationComplete");
                    StatusBox.Text = result.Message;
                    InstructionsBox.Text = LocalizedText.Get("UserFilesPreserved");
                    if (prismOperationTarget) await RefreshLauncherDiscoveryAsync();
                    else _ = RefreshRootState(persist: false);
                }
                if (operationOutcome != "profile_pending")
                {
                    operationOutcome = "completed";
                    if (operation == Operation.Install)
                        StatusBox.Text = LocalizedText.Get("UiOperationInstallSuccess", SelectedPackName,
                            selectedPack.Version, LocalizedText.Get(selectedPrismTarget is null
                                ? "UiLauncherOptionOfficial" : "UiLauncherOptionPrism"));
                }
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
            PrepareOperationFinish();
            try { await RefreshLauncherDiscoveryAsync(); }
            catch (Exception ex) when (ex is InstallerException or IOException or UnauthorizedAccessException or ArgumentException) { }
            ReleaseOperationBusy();
            CompleteOperationPresentation(operation, operationOutcome);
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

    private async void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var selected = await ResolveActiveOperationTargetAsync();
            var root = selected is null ? null : GetEntryRoot(selected);
            var path = selected is { IsOpenable: true } && root is not null && IsUnderSelectedRoot(root, selected)
                ? selected.Path : null;
            if (path is null || !Directory.Exists(path))
            {
                ShowInlineNotice(LocalizedText.Get("InstalledFolderNotFound"));
                return;
            }
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowInlineNotice(LocalizedText.Get("OpenFolderFailed", ex.GetType().Name));
        }
    }

    private void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = _currentOperationLog?.CurrentLogPath;
            if (path is null)
            {
                ShowInlineNotice(LocalizedText.Get("LogUnavailable"));
                return;
            }
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowInlineNotice(LocalizedText.Get("OpenLogFailed", ex.GetType().Name));
        }
    }

    private void ExportLog_Click(object sender, RoutedEventArgs e)
    {
        var operationLog = _currentOperationLog;
        if (operationLog?.CurrentLogPath is null)
        {
            ShowInlineNotice(LocalizedText.Get("LogUnavailable"));
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
        ShowInlineNotice(operationLog.TryExportCurrent(dialog.FileName)
            ? LocalizedText.Get("DiagnosticExported")
            : LocalizedText.Get("DiagnosticExportFailed"));
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
            ShowInlineNotice(LocalizedText.Get("OpenModrinthFailed"));
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

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_operationProgressTerminal)
        {
            CloseCompletedOperationProgress();
            return;
        }
        _operationCancellation?.Cancel();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_operationCancellation is not null)
        {
            e.Cancel = true;
            if (_operationCanBeCancelled)
            {
                if (OperationProgressOverlay.Visibility == Visibility.Visible)
                    StatusBox.Text = LocalizedText.Get("CancelCurrentOperation");
                else ShowInlineNotice(LocalizedText.Get("CancelCurrentOperation"));
                _operationCancellation.Cancel();
            }
            else if (OperationProgressOverlay.Visibility == Visibility.Visible)
                StatusBox.Text = LocalizedText.Get("OperationWait");
            else ShowInlineNotice(LocalizedText.Get("OperationWait"));
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
        InstallButton.IsEnabled = true;
        InstallButton.Content = LocalizedText.Get(busy ? "UiShowProgress" : "UiInstall");
        InstallButton.IsDefault = OperationProgressOverlay.Visibility != Visibility.Visible;
        RepairButton.IsEnabled = !busy;
        UninstallButton.IsEnabled = !busy;
        CancelButton.IsEnabled = busy && _operationCanBeCancelled;
        CancelButton.Visibility = busy && _operationCanBeCancelled ? Visibility.Visible : Visibility.Collapsed;
        RetryLauncherButton.IsEnabled = !busy;
        ImportWorldsButton.IsEnabled = !busy;
        OpenFolderButton.IsEnabled = !busy;
        OpenLogButton.IsEnabled = !busy;
        ExportLogButton.IsEnabled = !busy;
    }


    protected override void OnClosed(EventArgs e)
    {
        _launcherChoiceCompletion?.TrySetResult(null);
        _cleanupCheckGeneration++;
        _operationCancellation?.Cancel();
        _installer.Dispose();
        _launcher.Dispose();
        base.OnClosed(e);
    }

    private sealed record InstanceChoice(InstalledInstanceEntry Entry, string Display);
    internal sealed record LauncherInventory(IReadOnlyList<MinecraftLauncherTarget> OfficialTargets,
        LauncherScanResult PrismScan, bool OfficialUnknown,
        IReadOnlyList<InstalledInstanceEntry> PrismInstanceEntries, HashSet<string> ActivePrismInstancePaths);
    internal sealed record LauncherInstallSelection(LauncherKind Kind, MinecraftLauncherTarget? OfficialTarget,
        PrismLauncherTarget? PrismTarget);
    internal sealed record LauncherChoiceSession(TaskCompletionSource<LauncherInstallSelection?> Completion,
        int Generation);
    private sealed record LauncherTargetChoice(LauncherKind Kind, string Display,
        MinecraftLauncherTarget? OfficialTarget, PrismLauncherTarget? PrismTarget)
    {
        public LauncherInstallSelection ToSelection() => new(Kind, OfficialTarget, PrismTarget);
    }

    private enum Operation { Install, Repair, Uninstall, ConfigureLauncher, ImportWorlds, RemoveResidue }
    private enum PendingDeleteAction { None, Uninstall, RemoveResidue }
}
