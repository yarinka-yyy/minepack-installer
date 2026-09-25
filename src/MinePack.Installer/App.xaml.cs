using System.Configuration;
using System.Globalization;
using System.Windows;
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
        new MainWindow().Show();
    }
}
