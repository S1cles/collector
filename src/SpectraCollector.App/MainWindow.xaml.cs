using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace SpectraCollector.App;

/// <summary>The only window. State lives in <see cref="MainViewModel"/>; this class wires file dialogs and closing.</summary>
public partial class MainWindow : Window
{
    private const string CsvFilter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*";

    private readonly MainViewModel _viewModel;
    private bool _closeRequested;

    /// <summary>Shows <paramref name="viewModel"/>.</summary>
    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    private void OnBrowseCsv(object? sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Choose the CSV file with sample times", Filter = CsvFilter };
        var folder = Path.GetDirectoryName(_viewModel.CsvPath);
        if (Directory.Exists(folder))
        {
            dialog.InitialDirectory = folder;
        }

        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.CsvPath = dialog.FileName;
        }
    }

    private void OnBrowseOutput(object? sender, RoutedEventArgs e)
    {
        if (PickFolder("Choose where to save the zip", _viewModel.OutputDir) is { } folder)
        {
            _viewModel.OutputDir = folder;
        }
    }

    private void OnBrowseData(object? sender, RoutedEventArgs e)
    {
        if (PickFolder("Choose the LyncisAC data folder", _viewModel.DataRoot) is { } folder)
        {
            _viewModel.DataRoot = folder;
        }
    }

    /// <summary>"Change" hides itself, so keyboard focus moves on to the data folder cell it reveals.</summary>
    private void OnChangeData(object? sender, RoutedEventArgs e)
    {
        _viewModel.OpenDataEditor();
        Dispatcher.InvokeAsync(() => DataRootBox.Focus(), DispatcherPriority.Input);
    }

    private string? PickFolder(string title, string current)
    {
        var dialog = new OpenFolderDialog { Title = title };
        if (Directory.Exists(current))
        {
            dialog.InitialDirectory = current;
        }

        return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
    }

    private void OnActivated(object? sender, EventArgs e) => _viewModel.Refresh();

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_viewModel.IsRunning)
        {
            return;
        }

        e.Cancel = true;
        if (_closeRequested)
        {
            return;
        }

        _closeRequested = true;
        try
        {
            await _viewModel.CancelAndWaitAsync();
        }
        catch (Exception)
        {
            // The Collect handler awaits the same run and shows its failure; here it must not stop the close.
        }

        Close();
    }
}
