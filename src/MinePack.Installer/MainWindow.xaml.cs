using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Windows;
using MinePack.Core;
using Microsoft.Win32;

namespace MinePack.Installer;

public partial class MainWindow : Window
{
    private readonly InstallService _installer = new();
    private CancellationTokenSource? _operationCancellation;
    private string? _gameDirectory;

    public MainWindow()
    {
        InitializeComponent();
        InstallRootBox.Text = InstallService.DefaultInstallRoot;
    }

    private string PackPath => Path.Combine(AppContext.BaseDirectory,
        TestPackRelease.ArtifactRelativePath.Replace('/', Path.DirectorySeparatorChar));

    private async void Install_Click(object sender, RoutedEventArgs e) => await RunOperationAsync(Operation.Install);

    private async void Repair_Click(object sender, RoutedEventArgs e) => await RunOperationAsync(Operation.Repair);

    private async void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this,
                "Будут удалены только файлы, записанные в manifest этой сборки. Миры, снимки экрана и неизвестные файлы сохранятся. Продолжить?",
                "Удалить MinePack Test Pack", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
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

    private async Task RunOperationAsync(Operation operation)
    {
        if (_operationCancellation is not null) return;
        var cancellation = new CancellationTokenSource();
        _operationCancellation = cancellation;
        SetBusy(true);
        OperationProgress.Value = 0;
        ProgressLabel.Text = operation switch
        {
            Operation.Install => "Проверка закреплённого релиза…",
            Operation.Repair => "Поиск установленной сборки…",
            _ => "Подготовка удаления…"
        };

        try
        {
            var root = Path.GetFullPath(InstallRootBox.Text);
            if (!File.Exists(PackPath))
                throw new InstallerException("PACK_NOT_FOUND", "В опубликованной папке приложения не найден закреплённый .mrpack релиз.");

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

            InstallResult result;
            if (operation == Operation.Install)
            {
                result = await _installer.InstallAsync(PackPath, TestPackRelease.ArtifactSha512, root, progress, cancellation.Token);
            }
            else
            {
                var instance = _installer.GetActiveInstancePath(root);
                if (instance is null)
                    throw new InstallerException("INSTANCE_NOT_FOUND", "Активная установка тестового релиза не найдена в выбранной папке.");
                result = operation == Operation.Repair
                    ? await _installer.RepairAsync(instance, PackPath, TestPackRelease.ArtifactSha512, progress, cancellation.Token)
                    : await _installer.UninstallAsync(instance, PackPath, TestPackRelease.ArtifactSha512);
            }

            if (result.Success)
            {
                _gameDirectory = result.GameDirectory;
                StatusBox.Text = $"{result.Message}\n\nGame Directory: {result.GameDirectory ?? "удалён"}\nЖурнал: {result.LogPath ?? _installer.GetLatestLogPath(root) ?? "не создан"}";
                ProgressLabel.Text = "Операция завершена";
                if (operation == Operation.Install || operation == Operation.Repair)
                    InstructionsBox.Text = LauncherProfile.GetManualInstructions(result.GameDirectory ?? _gameDirectory ?? root);
                else
                    InstructionsBox.Text = "Управляемые файлы удалены. Миры, снимки экрана и неизвестные пользовательские файлы сохранены.";
            }
            else
            {
                StatusBox.Text = $"{result.Code}: {result.Message}\n\nGame Directory: {result.GameDirectory ?? "не изменён"}\nЖурнал: {result.LogPath ?? _installer.GetLatestLogPath(root) ?? "не создан"}";
                ProgressLabel.Text = result.Code == "CANCELLED" ? "Операция отменена" : "Операция завершилась с ошибкой";
            }
        }
        catch (InstallerException ex)
        {
            StatusBox.Text = $"{ex.Code}: {ex.Message}";
            ProgressLabel.Text = "Операция не запущена";
        }
        catch (OperationCanceledException)
        {
            StatusBox.Text = "Операция отменена.";
            ProgressLabel.Text = "Операция отменена";
        }
        catch (Exception ex)
        {
            StatusBox.Text = $"Не удалось выполнить действие ({ex.GetType().Name}). Проверьте путь и откройте журнал диагностики.";
            ProgressLabel.Text = "Операция завершилась с ошибкой";
        }
        finally
        {
            cancellation.Dispose();
            _operationCancellation = null;
            SetBusy(false);
        }
    }

    private void Instructions_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = _gameDirectory ?? _installer.GetActiveInstancePath(Path.GetFullPath(InstallRootBox.Text));
            InstructionsBox.Text = path is null
                ? "Сначала установите тестовую сборку. После успешной установки здесь появятся точные ручные шаги настройки профиля и Game Directory."
                : LauncherProfile.GetManualInstructions(path);
        }
        catch (Exception ex)
        {
            InstructionsBox.Text = $"Не удалось сформировать инструкцию ({ex.GetType().Name}). Проверьте корневую папку MinePack.";
        }
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
        InstructionsButton.IsEnabled = !busy;
        OpenFolderButton.IsEnabled = !busy;
        OpenLogButton.IsEnabled = !busy;
    }

    protected override void OnClosed(EventArgs e)
    {
        _operationCancellation?.Cancel();
        _installer.Dispose();
        base.OnClosed(e);
    }

    private enum Operation { Install, Repair, Uninstall }
}
