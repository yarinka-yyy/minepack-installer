using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Navigation;
using MinePack.Core;
using Microsoft.Win32;

namespace MinePack.Installer;

public partial class MainWindow : Window
{
    private readonly InstallService _installer = new();
    private readonly MinecraftLauncherController _launcherController;
    private readonly FabricLauncherService _launcher;
    private CancellationTokenSource? _operationCancellation;
    private string? _gameDirectory;

    public MainWindow()
    {
        InitializeComponent();
        _launcherController = new MinecraftLauncherController();
        _launcher = new FabricLauncherService(ensureLauncherClosed: _launcherController.EnsureClosed);
        VanillaPlusOption.Checked += PackChoice_Changed;
        Vanilla2PlusOption.Checked += PackChoice_Changed;
        UpdatePackSelection();
        InstallRootBox.Text = InstallService.DefaultInstallRoot;
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
        var vanillaSaves = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft", "saves");
        var picker = new OpenFolderDialog
        {
            Title = LocalizedText.Get("UiWorldFolderDialog"),
            InitialDirectory = Directory.Exists(vanillaSaves) ? vanillaSaves : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
        };
        if (picker.ShowDialog(this) == true)
            await RunOperationAsync(Operation.ImportWorlds, picker.FolderName);
    }

    private async void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this,
                LocalizedText.Get("UiUninstallConfirmMessage"),
                LocalizedText.Get("UiUninstallConfirmTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        await RunOperationAsync(Operation.Uninstall);
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog
        {
            Title = LocalizedText.Get("UiInstallFolderDialog"),
            InitialDirectory = Directory.Exists(InstallRootBox.Text) ? InstallRootBox.Text : InstallService.DefaultInstallRoot
        };
        if (picker.ShowDialog(this) == true) InstallRootBox.Text = picker.FolderName;
    }

    private async Task RunOperationAsync(Operation operation, string? sourceWorlds = null)
    {
        if (_operationCancellation is not null) return;
        var cancellation = new CancellationTokenSource();
        _operationCancellation = cancellation;
        SetBusy(true);
        var filesInstalled = false;
        var filesRemoved = false;
        MinecraftLauncherTarget? launcherTarget = null;
        OperationProgress.Value = 0;
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
            var root = Path.GetFullPath(InstallRootBox.Text);
            var selectedPack = SelectedPack;
            if (operation == Operation.Install && !File.Exists(selectedPack.Path))
                throw new InstallerException("PACK_NOT_FOUND", LocalizedText.Get("PublishedPackMissing"));

            if (operation == Operation.ImportWorlds)
            {
                var instance = _installer.GetActiveInstancePath(root)
                    ?? throw new InstallerException("INSTANCE_NOT_FOUND", LocalizedText.Get("InstallBeforeImport"));
                var worldProgress = new Progress<string>(message => ProgressLabel.Text = message);
                var imported = await WorldImportService.ImportAsync(sourceWorlds!, instance, worldProgress, cancellation.Token);
                StateHeading.Text = LocalizedText.Get("WorldImportComplete");
                StatusBox.Text = LocalizedText.Get("WorldImportSummary", imported.Imported, imported.Skipped);
                InstructionsBox.Text = LocalizedText.Get("WorldImportSafety");
                ProgressLabel.Text = LocalizedText.Get("OperationComplete");
                return;
            }

            var progress = new Progress<InstallProgress>(item =>
            {
                if (item.ExpectedBytes is > 0 && item.BytesReceived > 0)
                {
                    OperationProgress.Maximum = item.ExpectedBytes.Value;
                    OperationProgress.Value = Math.Min(item.BytesReceived, item.ExpectedBytes.Value);
                }
                else
                {
                    OperationProgress.Maximum = Math.Max(1, item.TotalFiles);
                    OperationProgress.Value = Math.Clamp(item.CompletedFiles, 0, OperationProgress.Maximum);
                }
                ProgressLabel.Text = item.Message;
            });

            if (operation == Operation.ConfigureLauncher)
            {
                _gameDirectory = _installer.GetActiveInstancePath(root)
                    ?? throw new InstallerException("INSTANCE_NOT_FOUND", LocalizedText.Get("InstallBeforeLauncherRepair"));
                filesInstalled = true;
                await ConfigureLauncherAsync(_gameDirectory, cancellation.Token);
                return;
            }

            InstallResult result;
            if (operation == Operation.Install)
            {
                _launcher.CheckProfileReady();
                ProgressLabel.Text = LocalizedText.Get("ClosingLauncherProgress");
                launcherTarget = await _launcherController.CloseBeforeInstallAsync(cancellation.Token);
                _launcher.CheckReady();
                var active = _installer.GetActiveInstancePath(root);
                var current = active is null ? null : InstallationManifest.Load(active);
                result = current?.PackVersion == selectedPack.Version &&
                         current.PackArchiveSha512.Equals(selectedPack.Hash, StringComparison.OrdinalIgnoreCase)
                    ? await _installer.RepairAsync(active!, selectedPack.Path, selectedPack.Hash, progress, cancellation.Token)
                    : await _installer.InstallAsync(selectedPack.Path, selectedPack.Hash, root, progress, cancellation.Token);
            }
            else
            {
                var instance = _installer.GetActiveInstancePath(root);
                if (instance is null)
                {
                    if (operation != Operation.Uninstall)
                        throw new InstallerException("INSTANCE_NOT_FOUND", LocalizedText.Get("PackNotFoundInFolder"));
                    _launcher.RemoveOwnProfile();
                    StateHeading.Text = LocalizedText.Get("ProfileRemoved");
                    ProgressLabel.Text = LocalizedText.Get("OperationComplete");
                    StatusBox.Text = LocalizedText.Get("NoActivePackProfileRemoved");
                    InstructionsBox.Text = LocalizedText.Get("OtherProfilesUnchanged");
                    return;
                }
                var installedVersion = InstallationManifest.Load(instance).PackVersion;
                var (installedPackPath, installedPackHash) = installedVersion switch
                {
                    TestPackRelease.PackVersion => (VanillaPlusPackPath, TestPackRelease.ArtifactSha512),
                    Vanilla2PlusRelease.PackVersion => (Vanilla2PlusPackPath, Vanilla2PlusRelease.ArtifactSha512),
                    "0.11.0" => (Path.Combine(AppContext.BaseDirectory, "releases", "vanilla-2-plus", Vanilla2PlusRelease.PreviousArtifactFileName), Vanilla2PlusRelease.PreviousArtifactSha512),
                    "0.9.0" => (Path.Combine(AppContext.BaseDirectory, "releases", "test-pack", TestPackRelease.MapArtifactFileName), TestPackRelease.MapArtifactSha512),
                    "0.8.0" => (Path.Combine(AppContext.BaseDirectory, "releases", "test-pack", TestPackRelease.AnimationArtifactFileName), TestPackRelease.AnimationArtifactSha512),
                    "0.7.0" => (Path.Combine(AppContext.BaseDirectory, "releases", "test-pack", TestPackRelease.GraphicsArtifactFileName), TestPackRelease.GraphicsArtifactSha512),
                    "0.6.0" => (Path.Combine(AppContext.BaseDirectory, "releases", "test-pack", TestPackRelease.InventoryArtifactFileName), TestPackRelease.InventoryArtifactSha512),
                    "0.5.0" => (Path.Combine(AppContext.BaseDirectory, "releases", "test-pack", TestPackRelease.VisualArtifactFileName), TestPackRelease.VisualArtifactSha512),
                    "0.4.0" => (Path.Combine(AppContext.BaseDirectory, "releases", "test-pack", TestPackRelease.C2meArtifactFileName), TestPackRelease.C2meArtifactSha512),
                    "0.3.0" => (Path.Combine(AppContext.BaseDirectory, "releases", "test-pack", TestPackRelease.VoxyArtifactFileName), TestPackRelease.VoxyArtifactSha512),
                    "0.2.0" => (Path.Combine(AppContext.BaseDirectory, "releases", "test-pack", TestPackRelease.PreviousArtifactFileName), TestPackRelease.PreviousArtifactSha512),
                    "0.1.0" => (Path.Combine(AppContext.BaseDirectory, "releases", "test-pack", TestPackRelease.LegacyArtifactFileName), TestPackRelease.LegacyArtifactSha512),
                    _ => throw new InstallerException("RELEASE_UNKNOWN", LocalizedText.Get("PinnedArchiveUnavailable"))
                };
                if (operation == Operation.Uninstall)
                    _launcher.RemoveOwnProfile(instance);
                result = operation == Operation.Repair
                    ? await _installer.RepairAsync(instance, installedPackPath, installedPackHash, progress, cancellation.Token)
                    : await _installer.UninstallAsync(instance, installedPackPath, installedPackHash);
            }

            if (result.Success)
            {
                _gameDirectory = result.GameDirectory;
                if (operation != Operation.Uninstall)
                {
                    filesInstalled = true;
                    if (operation == Operation.Install)
                    {
                        var launch = await _launcherController.ConfigureAndStartAsync(launcherTarget,
                            () => ConfigureLauncherAsync(result.GameDirectory!, cancellation.Token));
                        if (launch.Status == MinecraftLauncherStartStatus.Requested)
                            StatusBox.Text = LocalizedText.Get("LauncherStartRequestedStatus");
                        else
                        {
                            StatusBox.Text = LocalizedText.Get(launch.Status == MinecraftLauncherStartStatus.Failed
                                ? "LauncherStartFailedStatus" : "LauncherStartUnavailableStatus");
                            InstructionsBox.Text = LocalizedText.Get("LaunchInstruction");
                            DiagnosticText.Text = launch.Diagnostic;
                        }
                    }
                    else
                        await ConfigureLauncherAsync(result.GameDirectory!, cancellation.Token);
                }
                else
                {
                    filesRemoved = true;
                    StateHeading.Text = LocalizedText.Get("PackUninstalled");
                    ProgressLabel.Text = LocalizedText.Get("OperationComplete");
                    StatusBox.Text = result.Message;
                    InstructionsBox.Text = LocalizedText.Get("UserFilesPreserved");
                }
            }
            else
            {
                StateHeading.Text = LocalizedText.Get(result.Code == "CANCELLED" ? "OperationCancelledHeading" : "OperationFailedHeading");
                StatusBox.Text = result.Code == "CANCELLED" ? LocalizedText.Get("OperationStoppedStatus") : result.Message;
                DiagnosticText.Text = FormatDiagnostic(result.Code, result.LogPath ?? _installer.GetLatestLogPath(root));
                ProgressLabel.Text = LocalizedText.Get(result.Code == "CANCELLED" ? "OperationCancelledHeading" : "OperationFailedProgress");
            }
        }
        catch (InstallerException ex)
        {
            if (operation == Operation.ImportWorlds)
            {
                StateHeading.Text = LocalizedText.Get("WorldImportFailedHeading");
                StatusBox.Text = ex.Message;
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
        catch (OperationCanceledException)
        {
            if (operation == Operation.ImportWorlds)
            {
                StateHeading.Text = LocalizedText.Get("WorldImportStopped");
                StatusBox.Text = LocalizedText.Get("WorldCopyCancelled");
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
            cancellation.Dispose();
            _operationCancellation = null;
            SetBusy(false);
        }
    }

    private async Task ConfigureLauncherAsync(string gameDirectory, CancellationToken cancellationToken)
    {
        ProgressLabel.Text = LocalizedText.Get("ConfigureLauncherProgress");
        var manifest = InstallationManifest.Load(gameDirectory);
        if (manifest.MinecraftVersion == TestPackRelease.MinecraftVersion)
            await _launcher.ConfigureAsync(gameDirectory, cancellationToken);
        else
        {
            using var previousLauncher = new FabricLauncherService(minecraftVersion: manifest.MinecraftVersion,
                ensureLauncherClosed: _launcherController.EnsureClosed);
            await previousLauncher.ConfigureAsync(gameDirectory, cancellationToken);
        }
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
            var path = _gameDirectory ?? _installer.GetActiveInstancePath(Path.GetFullPath(InstallRootBox.Text));
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
            var path = _installer.GetLatestLogPath(Path.GetFullPath(InstallRootBox.Text));
            if (path is null)
            {
                StatusBox.Text = LocalizedText.Get("LogAfterFirstOperation");
                return;
            }
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusBox.Text = LocalizedText.Get("OpenLogFailed", ex.GetType().Name);
        }
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
        string? log = null;
        try { log = _installer.GetLatestLogPath(Path.GetFullPath(InstallRootBox.Text)); }
        catch (Exception) { }
        DiagnosticText.Text = FormatDiagnostic(code, log);
    }

    private static string FormatDiagnostic(string code, string? logPath) =>
        $"{LocalizedText.Get("DiagnosticCode", code)}\n{LocalizedText.Get("DiagnosticLog", logPath ?? LocalizedText.Get("NotCreated"))}";

    private void Cancel_Click(object sender, RoutedEventArgs e) => _operationCancellation?.Cancel();

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_operationCancellation is not null)
        {
            e.Cancel = true;
            StatusBox.Text = LocalizedText.Get("CancelCurrentOperation");
            _operationCancellation.Cancel();
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
        CancelButton.IsEnabled = busy;
        CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        RetryLauncherButton.IsEnabled = !busy;
        ImportWorldsButton.IsEnabled = !busy;
        OpenFolderButton.IsEnabled = !busy;
        OpenLogButton.IsEnabled = !busy;
    }

    protected override void OnClosed(EventArgs e)
    {
        _operationCancellation?.Cancel();
        _installer.Dispose();
        _launcher.Dispose();
        base.OnClosed(e);
    }

    private enum Operation { Install, Repair, Uninstall, ConfigureLauncher, ImportWorlds }
}
