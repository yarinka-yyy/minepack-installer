using System.Configuration;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using MinePack.Core;

namespace MinePack.Installer;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        var uiCulture = LocalizedText.SelectUiCulture(CultureInfo.CurrentUICulture);
        CultureInfo.CurrentUICulture = uiCulture;
        CultureInfo.DefaultThreadCurrentUICulture = uiCulture;
        base.OnStartup(e);
        var mainWindow = new MainWindow();
        if (uiCulture.Name == "zh-CN")
            mainWindow.FontFamily = new FontFamily("Microsoft YaHei UI, Microsoft YaHei, SimSun");
        mainWindow.Show();
    }
}
