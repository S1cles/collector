using System.Windows;
using SpectraCollector.Core;

namespace SpectraCollector.App;

/// <summary>Opens the window with the fields saved by the last successful run.</summary>
public partial class App : Application
{
    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var settingsPath = AppSettings.DefaultPath;
        new MainWindow(new MainViewModel(MainViewModel.LoadSettings(settingsPath), settingsPath)).Show();
    }
}
