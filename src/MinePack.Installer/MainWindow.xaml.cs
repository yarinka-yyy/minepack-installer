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
    private readonly FabricLauncherService _launcher = new();
    private CancellationTokenSource? _operationCancellation;
    private string? _gameDirectory;

    public MainWindow()
    {
        InitializeComponent();
        CatalogList.ItemsSource = PackCatalog.Groups;
        InstallRootBox.Text = InstallService.DefaultInstallRoot;
    }

    private string PackPath => Path.Combine(AppContext.BaseDirectory,
        TestPackRelease.ArtifactRelativePath.Replace('/', Path.DirectorySeparatorChar));

    private async void Install_Click(object sender, RoutedEventArgs e) => await RunOperationAsync(Operation.Install);

    private async void Repair_Click(object sender, RoutedEventArgs e) => await RunOperationAsync(Operation.Repair);

    private async void Configure_Click(object sender, RoutedEventArgs e) => await RunOperationAsync(Operation.ConfigureLauncher);

    private async void ImportWorlds_Click(object sender, RoutedEventArgs e)
    {
        var vanillaSaves = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft", "saves");
        var picker = new OpenFolderDialog
        {
            Title = "Выберите папку saves профиля, из которого скопировать миры",
            InitialDirectory = Directory.Exists(vanillaSaves) ? vanillaSaves : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
        };
        if (picker.ShowDialog(this) == true)
            await RunOperationAsync(Operation.ImportWorlds, picker.FolderName);
    }

    private async void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this,
                "Удалить установленные файлы MinePack? Миры, снимки экрана и ваши другие файлы останутся на месте.",
                "Удалить MinePack", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        await RunOperationAsync(Operation.Uninstall);
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog
        {
            Title = "Выберите корневую папку данных MinePack",
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
        OperationProgress.Value = 0;
        InstructionsBox.Text = "";
        DiagnosticText.Text = "";
        StatusBox.Text = "Подождите, выполняем выбранное действие.";
        StateHeading.Text = operation == Operation.Install ? "Устанавливаем сборку" : "Выполняем действие";
        ProgressLabel.Text = operation switch
        {
            Operation.Install => "Подготовка файлов…",
            Operation.Repair => "Поиск установленной сборки…",
            Operation.ConfigureLauncher => "Восстанавливаем профиль Launcher…",
            Operation.ImportWorlds => "Копирование миров…",
            _ => "Подготовка удаления…"
        };

        try
        {
            var root = Path.GetFullPath(InstallRootBox.Text);
            if (operation == Operation.Install && !File.Exists(PackPath))
                throw new InstallerException("PACK_NOT_FOUND", "В опубликованной папке приложения не найден закреплённый .mrpack релиз.");

            if (operation == Operation.ImportWorlds)
            {
                var instance = _installer.GetActiveInstancePath(root)
                    ?? throw new InstallerException("INSTANCE_NOT_FOUND", "Сначала установите сборку, в которую хотите скопировать миры.");
                var worldProgress = new Progress<string>(message => ProgressLabel.Text = message);
                var imported = await WorldImportService.ImportAsync(sourceWorlds!, instance, worldProgress, cancellation.Token);
                StateHeading.Text = "Импорт завершён";
                StatusBox.Text = $"Скопировано миров: {imported.Imported}. Пропущено совпадений имён: {imported.Skipped}.";
                InstructionsBox.Text = "Исходные миры сохранены. Мир из другой версии Minecraft открывайте только после резервной копии: сама игра может преобразовать его формат.";
                ProgressLabel.Text = "Операция завершена";
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
                    ?? throw new InstallerException("INSTANCE_NOT_FOUND", "Сначала установите сборку.");
                filesInstalled = true;
                await ConfigureLauncherAsync(_gameDirectory, cancellation.Token);
                return;
            }

            InstallResult result;
            if (operation == Operation.Install)
            {
                _launcher.CheckReady();
                var active = _installer.GetActiveInstancePath(root);
                var current = active is null ? null : InstallationManifest.Load(active);
                result = current?.PackVersion == TestPackRelease.PackVersion &&
                         current.PackArchiveSha512.Equals(TestPackRelease.ArtifactSha512, StringComparison.OrdinalIgnoreCase)
                    ? await _installer.RepairAsync(active!, PackPath, TestPackRelease.ArtifactSha512, progress, cancellation.Token)
                    : await _installer.InstallAsync(PackPath, TestPackRelease.ArtifactSha512, root, progress, cancellation.Token);
            }
            else
            {
                var instance = _installer.GetActiveInstancePath(root);
                if (instance is null)
                {
                    if (operation != Operation.Uninstall)
                        throw new InstallerException("INSTANCE_NOT_FOUND", "Сборка не найдена в выбранной папке.");
                    _launcher.RemoveOwnProfile();
                    StateHeading.Text = "Профиль удалён";
                    ProgressLabel.Text = "Операция завершена";
                    StatusBox.Text = "Активной сборки уже нет. Собственный профиль MinePack удалён из Launcher.";
                    InstructionsBox.Text = "Другие профили Launcher не изменены.";
                    return;
                }
                var installedVersion = InstallationManifest.Load(instance).PackVersion;
                var (installedPackPath, installedPackHash) = installedVersion switch
                {
                    TestPackRelease.PackVersion => (PackPath, TestPackRelease.ArtifactSha512),
                    "0.7.0" => (Path.Combine(AppContext.BaseDirectory, "releases", "test-pack", TestPackRelease.GraphicsArtifactFileName), TestPackRelease.GraphicsArtifactSha512),
                    "0.6.0" => (Path.Combine(AppContext.BaseDirectory, "releases", "test-pack", TestPackRelease.InventoryArtifactFileName), TestPackRelease.InventoryArtifactSha512),
                    "0.5.0" => (Path.Combine(AppContext.BaseDirectory, "releases", "test-pack", TestPackRelease.VisualArtifactFileName), TestPackRelease.VisualArtifactSha512),
                    "0.4.0" => (Path.Combine(AppContext.BaseDirectory, "releases", "test-pack", TestPackRelease.C2meArtifactFileName), TestPackRelease.C2meArtifactSha512),
                    "0.3.0" => (Path.Combine(AppContext.BaseDirectory, "releases", "test-pack", TestPackRelease.VoxyArtifactFileName), TestPackRelease.VoxyArtifactSha512),
                    "0.2.0" => (Path.Combine(AppContext.BaseDirectory, "releases", "test-pack", TestPackRelease.PreviousArtifactFileName), TestPackRelease.PreviousArtifactSha512),
                    "0.1.0" => (Path.Combine(AppContext.BaseDirectory, "releases", "test-pack", TestPackRelease.LegacyArtifactFileName), TestPackRelease.LegacyArtifactSha512),
                    _ => throw new InstallerException("RELEASE_UNKNOWN", "Для этой установленной версии в приложении нет закреплённого архива.")
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
                    await ConfigureLauncherAsync(result.GameDirectory!, cancellation.Token);
                }
                else
                {
                    filesRemoved = true;
                    StateHeading.Text = "Сборка удалена";
                    ProgressLabel.Text = "Операция завершена";
                    StatusBox.Text = result.Message;
                    InstructionsBox.Text = "Миры, снимки экрана и другие личные файлы сохранены, если они были в папке сборки.";
                }
            }
            else
            {
                StateHeading.Text = result.Code == "CANCELLED" ? "Операция отменена" : "Не удалось завершить действие";
                StatusBox.Text = result.Code == "CANCELLED" ? "Операция остановлена. Уже сохранённые файлы остались на месте." : result.Message;
                DiagnosticText.Text = $"Код: {result.Code}\nЖурнал: {result.LogPath ?? _installer.GetLatestLogPath(root) ?? "не создан"}";
                ProgressLabel.Text = result.Code == "CANCELLED" ? "Операция отменена" : "Операция завершилась с ошибкой";
            }
        }
        catch (InstallerException ex)
        {
            if (operation == Operation.ImportWorlds)
            {
                StateHeading.Text = "Импорт не завершён";
                StatusBox.Text = ex.Message;
                ShowDiagnostic(ex.Code);
                InstructionsBox.Text = "Ранее скопированные миры остаются на месте; исходные миры не изменены.";
                ProgressLabel.Text = "Операция не завершена";
                return;
            }
            StateHeading.Text = filesRemoved ? "Файлы удалены, профиль остался" :
                filesInstalled ? "Сборка есть, профиль ещё не готов" : "Нужен ещё один шаг";
            StatusBox.Text = ex.Message;
            ShowDiagnostic(ex.Code);
            InstructionsBox.Text = filesRemoved
                ? "После устранения причины нажмите «Удалить сборку» ещё раз — останется убрать только профиль MinePack."
                : filesInstalled
                ? "Файлы сборки сохранены. После устранения причины нажмите «Восстановить профиль Launcher» — скачивать моды заново не потребуется."
                : "Исправьте указанную причину и повторите действие.";
            ProgressLabel.Text = "Операция не завершена";
        }
        catch (OperationCanceledException)
        {
            if (operation == Operation.ImportWorlds)
            {
                StateHeading.Text = "Импорт остановлен";
                StatusBox.Text = "Копирование отменено. Уже скопированные миры сохранены, исходные не изменены.";
                ProgressLabel.Text = "Операция отменена";
                return;
            }
            StateHeading.Text = "Операция отменена";
            StatusBox.Text = filesInstalled
                ? "Файлы сборки уже установлены, но профиль Launcher не настроен. Нажмите «Восстановить профиль Launcher», когда будете готовы."
                : "Операция отменена.";
            ProgressLabel.Text = "Операция отменена";
        }
        catch (Exception ex)
        {
            if (operation == Operation.ImportWorlds)
            {
                StateHeading.Text = "Импорт не завершён";
                StatusBox.Text = "Не удалось скопировать миры. Проверьте доступ к папке и свободное место.";
                ShowDiagnostic(ex.GetType().Name);
                InstructionsBox.Text = "Исходные миры не изменены; уже скопированные миры сохранены.";
                ProgressLabel.Text = "Операция завершилась с ошибкой";
                return;
            }
            StateHeading.Text = filesRemoved ? "Файлы удалены, профиль остался" :
                filesInstalled ? "Сборка есть, профиль ещё не готов" : "Не удалось завершить операцию";
            StatusBox.Text = "Не удалось выполнить действие. Проверьте путь к папке, права доступа и журнал.";
            ShowDiagnostic(ex.GetType().Name);
            InstructionsBox.Text = filesRemoved
                ? "Закройте Launcher и нажмите «Удалить сборку» ещё раз."
                : filesInstalled
                ? "Файлы сохранены. Нажмите «Восстановить профиль Launcher» после устранения причины."
                : "Исправьте указанную причину и повторите действие.";
            ProgressLabel.Text = "Операция завершилась с ошибкой";
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
        ProgressLabel.Text = "Загрузка Fabric и создание отдельного профиля Launcher…";
        var manifest = InstallationManifest.Load(gameDirectory);
        if (manifest.MinecraftVersion == TestPackRelease.MinecraftVersion)
            await _launcher.ConfigureAsync(gameDirectory, cancellationToken);
        else
        {
            using var previousLauncher = new FabricLauncherService(minecraftVersion: manifest.MinecraftVersion);
            await previousLauncher.ConfigureAsync(gameDirectory, cancellationToken);
        }
        StateHeading.Text = "Сборка готова";
        ProgressLabel.Text = "Установка завершена";
        StatusBox.Text = "Откройте официальный Minecraft Launcher, выберите профиль MinePack и нажмите «Играть».";
        InstructionsBox.Text = "При первом запуске Launcher сам загрузит базовые файлы Minecraft.";
        DiagnosticText.Text = $"Папка игры: {gameDirectory}";
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = _gameDirectory ?? _installer.GetActiveInstancePath(Path.GetFullPath(InstallRootBox.Text));
            if (path is null || !Directory.Exists(path))
            {
                StatusBox.Text = "Установленная папка игры пока не найдена.";
                return;
            }
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusBox.Text = $"Не удалось открыть папку ({ex.GetType().Name}).";
        }
    }

    private void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = _installer.GetLatestLogPath(Path.GetFullPath(InstallRootBox.Text));
            if (path is null)
            {
                StatusBox.Text = "Журнал появится после первой операции.";
                return;
            }
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusBox.Text = $"Не удалось открыть журнал ({ex.GetType().Name}).";
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
            StatusBox.Text = "Не удалось открыть страницу Modrinth в браузере.";
        }
        e.Handled = true;
    }

    private void ShowDiagnostic(string code)
    {
        string? log = null;
        try { log = _installer.GetLatestLogPath(Path.GetFullPath(InstallRootBox.Text)); }
        catch (Exception) { }
        DiagnosticText.Text = $"Код: {code}\nЖурнал: {log ?? "не создан"}";
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _operationCancellation?.Cancel();

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_operationCancellation is not null)
        {
            e.Cancel = true;
            StatusBox.Text = "Отменяю текущую операцию. Закройте окно после завершения отмены.";
            _operationCancellation.Cancel();
            return;
        }
        base.OnClosing(e);
    }

    private void SetBusy(bool busy)
    {
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
