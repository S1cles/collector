using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SpectraCollector.Core;

namespace SpectraCollector.App;

/// <summary>
/// The form and the run. Every input change re-runs <see cref="FormValidator.Check"/> and previews the result on
/// <see cref="Sheet"/>; a collection runs through <see cref="Collector.RunAsync"/>, its progress reaches the sheet
/// through a <see cref="ProgressPump"/>, and every outcome of it, including failures and cancellation, lands there.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    private const string MonthPlaceholder = "<month>";
    private const string IdleLabel = "Collect spectra";
    private const string NotSaved = "Not saved";

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

    /// <summary>True once "Change" has revealed the data folder and belt cells.</summary>
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

    public bool IsRunning => _isRunning;

    public bool IsIdle => !_isRunning;

    /// <summary>The primary button text: what the running collection is doing, or the plain verb when idle.</summary>
    public string CollectLabel =>
        !_isRunning ? IdleLabel
        : _isCancelling ? "Cancelling…"
        : _phase == CollectPhase.Searching ? "Searching…"
        : "Collecting…";

    /// <summary>The report sheet: preview, live fill and result.</summary>
    public ReportSheet Sheet { get; } = new();

    public ICommand CollectCommand => _collect;

    public ICommand CancelCommand => _cancel;

    public ICommand OpenFolderCommand { get; }

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
        Sheet.ShowDraft(_singleInterval ? SheetText.SingleIntervalTitle : SheetText.CsvTitle(_csvPath), _check);
        _collect.RaiseCanExecuteChanged();
    }

    /// <summary>Reveals the data folder and belt cells ("Change").</summary>
    public void OpenDataEditor() => IsDataEditorOpen = true;

    /// <summary>Cancels a running collection; the task completes once the window is idle and the outcome is shown.</summary>
    public Task CancelAndWaitAsync()
    {
        Cancel();
        return _run;
    }

    private bool CanCollect() => !_isRunning && _check.Request is not null;

    /// <summary>The command's UI boundary: anything <see cref="RunAsync"/> does not map is shown here, never rethrown.</summary>
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
            var line = new ResultLine($"Collection failed: {e.Message}", ResultTone.Error);
            if (Sheet.HasSaved)
            {
                Sheet.AddNote(line);
            }
            else
            {
                Sheet.ShowEnded(line, new SheetStamp(NotSaved, StampTone.Error));
            }
        }
    }

    private async Task RunAsync(CollectRequest request)
    {
        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        _phase = CollectPhase.Searching;
        _isCancelling = false;
        var title = _singleInterval ? SheetText.SingleIntervalTitle : request.CsvName;
        SetRunning(true);
        bool saved;
        try
        {
            Sheet.BeginRun(title, _check.WindowHint, request.Samples, CollectLabel);
            saved = await CollectAsync(request, cancellation.Token);
        }
        finally
        {
            _cancellation = null;
            _isCancelling = false;
            SetRunning(false);
        }

        if (saved)
        {
            SaveSettings();
        }
    }

    /// <summary>Runs the collector and shows its result on the sheet; true when a zip was saved.</summary>
    private async Task<bool> CollectAsync(CollectRequest request, CancellationToken ct)
    {
        using var pump = new ProgressPump(ShowProgress);
        CollectResult result;
        try
        {
            result = await Collector.RunAsync(request, pump, ct);
        }
        catch (Exception e)
        {
            var (outcome, stamp) = Ending(e, ct);
            Sheet.ShowEnded(outcome, stamp);
            return false;
        }

        pump.Stop();
        Sheet.ShowSaved(result, request.CsvName);
        return true;
    }

    /// <summary>How a run that saved nothing is shown: Graphite for a cancel, warning colour when nothing was found.</summary>
    private static (ResultLine Outcome, SheetStamp Stamp) Ending(Exception e, CancellationToken ct) => e switch
    {
        OperationCanceledException when ct.IsCancellationRequested => (
            new ResultLine("Cancelled. Nothing was saved.", ResultTone.Quiet), new SheetStamp("Cancelled", StampTone.Quiet)),
        NothingFoundException => (
            new ResultLine(e.Message, ResultTone.Warning), new SheetStamp(NotSaved, StampTone.Warning)),
        CollectorException => (
            new ResultLine(e.Message, ResultTone.Error), new SheetStamp(NotSaved, StampTone.Error)),
        _ => (
            new ResultLine($"Collection failed: {e.Message}", ResultTone.Error), new SheetStamp(NotSaved, StampTone.Error)),
    };

    private void ShowProgress(CollectProgress latest, CollectionReport? report)
    {
        if (_isCancelling)
        {
            return;
        }

        if (_phase != latest.Phase)
        {
            _phase = latest.Phase;
            ShowRunLabel();
        }

        Sheet.ShowProgress(latest, report);
    }

    private void Cancel()
    {
        if (_cancellation is null)
        {
            return;
        }

        _cancellation.Cancel();
        _isCancelling = true;
        ShowRunLabel();
    }

    private void ShowRunLabel()
    {
        Raise(nameof(CollectLabel));
        if (_isRunning)
        {
            Sheet.SetRunLabel(CollectLabel);
        }
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
            Sheet.AddNote(new ResultLine($"Settings could not be saved: {e.Message}", ResultTone.Quiet));
        }
    }

    private void OpenFolder()
    {
        if (Sheet.SavedZipPath is not { } zip)
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
            Sheet.AddNote(new ResultLine($"Cannot open the folder: {e.Message}", ResultTone.Error));
        }
    }

    private void SetRunning(bool running)
    {
        _isRunning = running;
        Raise(nameof(IsRunning), nameof(IsIdle), nameof(CollectLabel));
        _collect.RaiseCanExecuteChanged();
        _cancel.RaiseCanExecuteChanged();
    }

    /// <summary>Stores a changed input, drops a shown run result and re-validates; false when nothing changed.</summary>
    private bool SetInput<T>(ref T storage, T value, [CallerMemberName] string name = "")
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
        {
            return false;
        }

        storage = value;
        Raise(name);
        if (!_isRunning)
        {
            Sheet.LeaveRun();
        }

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
