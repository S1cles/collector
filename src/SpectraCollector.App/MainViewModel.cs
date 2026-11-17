using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SpectraCollector.Core;

namespace SpectraCollector.App;

/// <summary>
/// All state of the window. Every input change re-runs <see cref="FormValidator.Check"/>; a collection runs through
/// <see cref="Collector.RunAsync"/> and every outcome of it, including failures and cancellation, lands in
/// <see cref="Result"/>.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    private const string MonthPlaceholder = "<month>";

    private readonly string _settingsPath;
    private readonly RelayCommand _collect;
    private readonly RelayCommand _cancel;
    private bool _singleInterval;
    private string _csvPath;
    private string _beginText = string.Empty;
    private string _endText = string.Empty;
    private string _outputDir;
    private string _dataRoot;
    private string _belt;
    private bool _isDataEditorOpen;
    private bool _isRunning;
    private bool _isCancelling;
    private CollectPhase _phase;
    private double _bandFill;
    private RunView _result = RunView.Empty;
    private FormCheck _check;
    private CancellationTokenSource? _cancellation;
    private Task _run = Task.CompletedTask;

    /// <summary>Starts from <paramref name="settings"/>; each successful run saves the fields to <paramref name="settingsPath"/>.</summary>
    public MainViewModel(AppSettings settings, string settingsPath)
    {
        _settingsPath = settingsPath;
        _singleInterval = settings.SingleInterval;
        _csvPath = settings.CsvPath ?? string.Empty;
        _outputDir = settings.OutputDir ?? string.Empty;
        _dataRoot = settings.DataRoot;
        _belt = settings.Belt;
        _collect = new RelayCommand(Collect, CanCollect);
        _cancel = new RelayCommand(Cancel, () => _isRunning);
        OpenFolderCommand = new RelayCommand(OpenFolder);
        ChangeDataCommand = new RelayCommand(() => IsDataEditorOpen = true);
        Refresh();
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    public bool SingleInterval
    {
        get => _singleInterval;
        set
        {
            if (SetInput(ref _singleInterval, value))
            {
                Raise(nameof(IsCsvMode));
            }
        }
    }

    /// <summary>The inverse of <see cref="SingleInterval"/> for the "From CSV file" segment; only <c>true</c> is acted on.</summary>
    public bool IsCsvMode
    {
        get => !_singleInterval;
        set
        {
            if (value)
            {
                SingleInterval = false;
            }
        }
    }

    public string CsvPath
    {
        get => _csvPath;
        set => SetInput(ref _csvPath, value);
    }

    public string BeginText
    {
        get => _beginText;
        set => SetInput(ref _beginText, value);
    }

    public string EndText
    {
        get => _endText;
        set => SetInput(ref _endText, value);
    }

    public string OutputDir
    {
        get => _outputDir;
        set => SetInput(ref _outputDir, value);
    }

    public string DataRoot
    {
        get => _dataRoot;
        set
        {
            if (SetInput(ref _dataRoot, value))
            {
                Raise(nameof(DataPathText));
            }
        }
    }

    public string Belt
    {
        get => _belt;
        set
        {
            if (SetInput(ref _belt, value))
            {
                Raise(nameof(DataPathText));
            }
        }
    }

    /// <summary>Where spectra are read from, with the month folder left as a placeholder.</summary>
    public string DataPathText => Path.Join(_dataRoot, MonthPlaceholder, _belt);

    /// <summary>True once "Change" has revealed the data folder and belt fields.</summary>
    public bool IsDataEditorOpen
    {
        get => _isDataEditorOpen;
        private set
        {
            _isDataEditorOpen = value;
            Raise(nameof(IsDataEditorOpen), nameof(IsDataEditorClosed));
        }
    }

    public bool IsDataEditorClosed => !_isDataEditorOpen;

    public IReadOnlyList<string> Problems => _check.Problems;

    public bool HasProblems => _check.Problems.Count > 0;

    public string? WindowHint => _check.WindowHint;

    public bool HasWindowHint => _check.WindowHint is not null;

    public bool IsRunning => _isRunning;

    public bool IsIdle => !_isRunning;

    /// <summary>The Collect button text: what the running collection is doing, or the plain verb when idle.</summary>
    public string CollectLabel =>
        !_isRunning ? "Collect spectra"
        : _isCancelling ? "Cancelling…"
        : _phase == CollectPhase.Searching ? "Searching…"
        : "Collecting…";

    /// <summary>The filled share of the spectrum band, 0 to 1: Done/Total of the current phase while running, 1 after a success.</summary>
    public double BandFill
    {
        get => _bandFill;
        private set
        {
            _bandFill = value;
            Raise(nameof(BandFill));
        }
    }

    public RunView Result
    {
        get => _result;
        private set
        {
            _result = value;
            Raise(nameof(Result));
        }
    }

    /// <summary>Follow-up lines under the result, such as a settings file that could not be written.</summary>
    public ObservableCollection<ResultLine> Notes { get; } = [];

    public ICommand CollectCommand => _collect;

    public ICommand CancelCommand => _cancel;

    public ICommand OpenFolderCommand { get; }

    public ICommand ChangeDataCommand { get; }

    /// <summary>Reads saved settings; an unreadable file gives the defaults because every field can be re-entered.</summary>
    public static AppSettings LoadSettings(string path)
    {
        try
        {
            return AppSettings.Load(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return AppSettings.Default;
        }
    }

    /// <summary>Re-validates the form, e.g. after the CSV file may have been edited outside the window.</summary>
    [MemberNotNull(nameof(_check))]
    public void Refresh()
    {
        _check = FormValidator.Check(
            new FormInput(_singleInterval, _csvPath, _beginText, _endText, _outputDir, _dataRoot, _belt));
        Raise(nameof(Problems), nameof(HasProblems), nameof(WindowHint), nameof(HasWindowHint));
        _collect.RaiseCanExecuteChanged();
    }

    /// <summary>Cancels a running collection; the task completes once the window is idle and the outcome is shown.</summary>
    public Task CancelAndWaitAsync()
    {
        Cancel();
        return _run;
    }

    private bool CanCollect() => !_isRunning && _check.Request is not null;

    /// <summary>The command's UI boundary: anything <see cref="CollectAsync"/> does not map is shown here, never rethrown.</summary>
    private async void Collect()
    {
        try
        {
            Refresh();
            if (_isRunning || _check.Request is not { } request)
            {
                return;
            }

            _run = RunAsync(request);
            await _run;
        }
        catch (Exception e)
        {
            Notes.Add(new ResultLine($"Collection failed: {e.Message}", ResultTone.Error));
        }
    }

    private async Task RunAsync(CollectRequest request)
    {
        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        Notes.Clear();
        Result = RunView.Empty;
        BandFill = 0;
        _phase = CollectPhase.Searching;
        _isCancelling = false;
        SetRunning(true);
        try
        {
            Result = await CollectAsync(request, cancellation.Token);
        }
        finally
        {
            _cancellation = null;
            _isCancelling = false;
            BandFill = Result.HasZip ? 1 : 0;
            SetRunning(false);
        }

        if (Result.HasZip)
        {
            SaveSettings();
        }
    }

    private async Task<RunView> CollectAsync(CollectRequest request, CancellationToken ct)
    {
        var progress = new Progress<CollectProgress>(ShowProgress);
        try
        {
            return RunView.Succeeded(await Collector.RunAsync(request, progress, ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return RunView.Ended(new ResultLine("Cancelled. Nothing was saved.", ResultTone.Quiet));
        }
        catch (NothingFoundException e)
        {
            return RunView.Ended(new ResultLine(e.Message, ResultTone.Warning));
        }
        catch (CollectorException e)
        {
            return RunView.Ended(new ResultLine(e.Message, ResultTone.Error));
        }
        catch (Exception e)
        {
            return RunView.Ended(new ResultLine($"Collection failed: {e.Message}", ResultTone.Error));
        }
    }

    private void ShowProgress(CollectProgress progress)
    {
        if (_phase != progress.Phase)
        {
            _phase = progress.Phase;
            Raise(nameof(CollectLabel));
        }

        BandFill = progress.Total == 0 ? 0 : (double)progress.Done / progress.Total;
    }

    private void Cancel()
    {
        if (_cancellation is null)
        {
            return;
        }

        _cancellation.Cancel();
        _isCancelling = true;
        Raise(nameof(CollectLabel));
    }

    private void SaveSettings()
    {
        var settings = new AppSettings(NullIfBlank(_csvPath), NullIfBlank(_outputDir), _dataRoot, _belt, _singleInterval);
        try
        {
            settings.Save(_settingsPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Notes.Add(new ResultLine($"Settings could not be saved: {e.Message}", ResultTone.Quiet));
        }
    }

    private void OpenFolder()
    {
        if (Result.ZipPath is not { } zip)
        {
            return;
        }

        var explorer = new ProcessStartInfo("explorer.exe", $"/select,\"{zip}\"") { UseShellExecute = false };
        try
        {
            Process.Start(explorer)?.Dispose();
        }
        catch (Win32Exception e)
        {
            Notes.Add(new ResultLine($"Cannot open the folder: {e.Message}", ResultTone.Error));
        }
    }

    private void SetRunning(bool running)
    {
        _isRunning = running;
        Raise(nameof(IsRunning), nameof(IsIdle), nameof(CollectLabel));
        _collect.RaiseCanExecuteChanged();
        _cancel.RaiseCanExecuteChanged();
    }

    private bool SetInput<T>(ref T storage, T value, [CallerMemberName] string name = "")
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
        {
            return false;
        }

        storage = value;
        Raise(name);
        Refresh();
        return true;
    }

    private void Raise(params ReadOnlySpan<string> names)
    {
        foreach (var name in names)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    private static string? NullIfBlank(string text) => string.IsNullOrWhiteSpace(text) ? null : text;
}
