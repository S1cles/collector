using System.Windows;
using System.Windows.Threading;
using SpectraCollector.Core;

namespace SpectraCollector.App;

/// <summary>
/// Opens the window with the fields saved by the last successful run. An error that reaches the dispatcher is shown
/// once and ends the app, because the window state after it cannot be trusted.
/// </summary>
public partial class App : Application
{
    private bool _stopping;

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        base.OnStartup(e);
        var settingsPath = AppSettings.DefaultPath;
        new MainWindow(new MainViewModel(MainViewModel.LoadSettings(settingsPath), settingsPath)).Show();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        if (_stopping)
        {
            return;
        }

        _stopping = true;
        MessageBox.Show(
            $"SpectraCollector stopped because of an unexpected error: {e.Exception.Message}",
            "SpectraCollector",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        Shutdown(1);
    }
}
