using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using SpectraCollector.Core;

namespace SpectraCollector.App;

/// <summary>The colour of a result line: Graphite, Warning, Error or Success.</summary>
public enum ResultTone
{
    Quiet,
    Warning,
    Error,
    Success,
}

/// <summary>One line of plain text on the sheet, such as the outcome of a run that saved nothing.</summary>
public sealed record ResultLine(string Text, ResultTone Tone);

/// <summary>The colour of the stamp in the sheet header: Ink while running, then the outcome's colour.</summary>
public enum StampTone
{
    Ink,
    Success,
    Quiet,
    Warning,
    Error,
}

/// <summary>The stamp in the sheet header, e.g. "Collecting…" or "Saved".</summary>
public sealed record SheetStamp(string Text, StampTone Tone)
{
    /// <summary>"Saved" carries a check mark.</summary>
    public bool HasCheck => Tone == StampTone.Success;
}

/// <summary>What a saved zip holds, for the "Zip contents" block.</summary>
/// <param name="Folder">The folder inside the zip: the zip file name without extension.</param>
public sealed record ZipContents(string Folder, string SpectraFiles, string CsvName, string ReportName);

/// <summary>The parts of the sheet that exist only after a success.</summary>
public sealed record SheetSummary(
    string TotalFiles, IReadOnlyList<string> Totals, IReadOnlyList<Footnote> Footnotes, ZipContents Zip);

/// <summary>
/// The report sheet under the form. While idle it previews the current form: one blank row per parsed sample, or the
/// problems. A run freezes it to the run's samples, splits the Files column per spectrometer once the report is
/// known and fills the rows as files are written. A success adds the totals, footnotes and zip contents; any other
/// end returns to the blank rows with the outcome line. It stays on the run's result until an input changes.
/// </summary>
public sealed class ReportSheet : INotifyPropertyChanged
{
    private const string SavedPrefix = "Saved ";
    private static readonly IReadOnlyList<string> PreRunColumns = ["Files"];
    private static readonly IReadOnlyList<CountCell> PreRunBlanks = [CountCell.Blank];

    private IReadOnlyList<Sample>? _rowSamples;
    private SheetReport? _layout;
    private string _title = string.Empty;
    private string? _subtitle;
    private IReadOnlyList<string> _problems = [];
    private bool _showTable;
    private IReadOnlyList<string> _columns = PreRunColumns;
    private IReadOnlyList<SheetRow> _rows = [];
    private SheetSummary? _summary;
    private ResultLine? _outcome;
    private SheetStamp? _stamp;
    private double _bandFill;
    private string? _statusText;
    private string? _savedZipPath;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>True while the sheet previews the form; false from the start of a run until an input changes.</summary>
    internal bool IsDraft { get; private set; } = true;

    /// <summary>The CSV file name, or "Single interval".</summary>
    public string Title
    {
        get => _title;
        private set => Set(ref _title, value);
    }

    /// <summary>The window hint, e.g. <c>3 samples, files from 2026-11-06 13:05:00 to 13:51:00</c>.</summary>
    public string? Subtitle
    {
        get => _subtitle;
        private set => Set(ref _subtitle, value, [nameof(HasSubtitle)]);
    }

    public bool HasSubtitle => !string.IsNullOrEmpty(_subtitle);

    /// <summary>What keeps the form from a run; shown instead of the table.</summary>
    public IReadOnlyList<string> Problems
    {
        get => _problems;
        private set => Set(ref _problems, value, [nameof(HasProblems)]);
    }

    public bool HasProblems => _problems.Count > 0;

    public bool ShowTable
    {
        get => _showTable;
        private set => Set(ref _showTable, value);
    }

    /// <summary>Headers of the count columns: "Files" before the report is known, then one per spectrometer.</summary>
    public IReadOnlyList<string> Columns
    {
        get => _columns;
        private set => Set(ref _columns, value);
    }

    public IReadOnlyList<SheetRow> Rows
    {
        get => _rows;
        private set => Set(ref _rows, value);
    }

    public SheetSummary? Summary
    {
        get => _summary;
        private set => Set(ref _summary, value, [nameof(HasSummary)]);
    }

    public bool HasSummary => _summary is not null;

    /// <summary>How a run that saved nothing ended; it takes the footnotes' place.</summary>
    public ResultLine? Outcome
    {
        get => _outcome;
        private set => Set(ref _outcome, value);
    }

    public SheetStamp? Stamp
    {
        get => _stamp;
        private set => Set(ref _stamp, value, [nameof(HasStamp)]);
    }

    public bool HasStamp => _stamp is not null;

    /// <summary>Follow-up lines under the result, such as a settings file that could not be written.</summary>
    public ObservableCollection<ResultLine> Notes { get; } = [];

    /// <summary>The filled share of the spectrum band, 0 to 1: Done/Total of the current phase while running, 1 after a success.</summary>
    public double BandFill
    {
        get => _bandFill;
        private set => Set(ref _bandFill, value);
    }

    /// <summary>The footer line while running, e.g. <c>196 of 327 files · 60 %</c>.</summary>
    public string? StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value, [nameof(HasStatus)]);
    }

    public bool HasStatus => _statusText is not null;

    /// <summary>The zip written by the last successful run, for "Open folder".</summary>
    public string? SavedZipPath
    {
        get => _savedZipPath;
        private set => Set(ref _savedZipPath, value, [nameof(HasSaved), nameof(SavedText)]);
    }

    public bool HasSaved => _savedZipPath is not null;

    public string SavedText => _savedZipPath is null ? string.Empty : SavedPrefix + _savedZipPath;

    /// <summary>Shows the form as it is now; ignored unless <see cref="IsDraft"/>.</summary>
    internal void ShowDraft(string title, FormCheck check)
    {
        if (!IsDraft)
        {
            return;
        }

        Title = title;
        Subtitle = check.WindowHint;
        Problems = check.Problems;
        ShowTable = check.Request is not null;
        var samples = check.Request?.Samples ?? [];
        if (!ShowsSamples(samples))
        {
            SetPreRunRows(samples);
        }
    }

    /// <summary>True when the rows are exactly <paramref name="samples"/>, e.g. still the samples of the shown run.</summary>
    internal bool ShowsSamples(IReadOnlyList<Sample> samples) =>
        _rowSamples is not null && _rowSamples.SequenceEqual(samples);

    /// <summary>Freezes the sheet to <paramref name="samples"/> for a run and clears the last result.</summary>
    internal void BeginRun(string title, string? subtitle, IReadOnlyList<Sample> samples, string runLabel)
    {
        IsDraft = false;
        Title = title;
        Subtitle = subtitle;
        Problems = [];
        ShowTable = true;
        SetPreRunRows(samples);
        ClearResult();
        Stamp = new SheetStamp(runLabel, StampTone.Ink);
    }

    /// <summary>Shows what the run is doing: "Searching…", "Collecting…" or "Cancelling…".</summary>
    internal void SetRunLabel(string runLabel) => Stamp = new SheetStamp(runLabel, StampTone.Ink);

    /// <summary>Shows the newest progress; <paramref name="report"/> is the one the first Writing event carried.</summary>
    internal void ShowProgress(CollectProgress latest, CollectionReport? report)
    {
        BandFill = latest.Total == 0 ? 0 : (double)latest.Done / latest.Total;
        StatusText = SheetText.Status(latest);
        if (latest.Phase != CollectPhase.Writing || report is null)
        {
            return;
        }

        var layout = LayoutFor(report);
        if (latest.WrittenPerSample is { } written)
        {
            FillRows(layout, written);
        }
    }

    /// <summary>Shows the full report of a saved zip.</summary>
    internal void ShowSaved(CollectResult result, string csvName)
    {
        var layout = LayoutFor(result.Report);
        for (var i = 0; i < Math.Min(_rows.Count, layout.Rows.Count); i++)
        {
            _rows[i].ShowCounts(layout.Rows[i]);
        }

        var zip = new ZipContents(
            Path.GetFileNameWithoutExtension(result.ZipPath), layout.TotalFiles, csvName, SheetText.ReportFileName);
        Summary = new SheetSummary(layout.TotalFiles, layout.Totals, layout.Footnotes, zip);
        Stamp = new SheetStamp("Saved", StampTone.Success);
        BandFill = 1;
        StatusText = null;
        SavedZipPath = result.ZipPath;
    }

    /// <summary>Returns to the blank rows of the run and shows why nothing was saved.</summary>
    internal void ShowEnded(ResultLine outcome, SheetStamp stamp)
    {
        IsDraft = false;
        ResetRows();
        ClearResult();
        Outcome = outcome;
        Stamp = stamp;
    }

    /// <summary>Drops the shown run so the next <see cref="ShowDraft"/> previews the form again.</summary>
    internal void LeaveRun()
    {
        if (IsDraft)
        {
            return;
        }

        IsDraft = true;
        ClearResult();
        _rowSamples = null;
    }

    internal void AddNote(ResultLine note) => Notes.Add(note);

    private void SetPreRunRows(IReadOnlyList<Sample> samples)
    {
        _layout = null;
        _rowSamples = samples;
        Columns = PreRunColumns;
        Rows = samples.Select(s => new SheetRow(s, PreRunBlanks)).ToList();
    }

    private void ResetRows()
    {
        _layout = null;
        Columns = PreRunColumns;
        foreach (var row in _rows)
        {
            row.ShowBlank(PreRunBlanks);
        }
    }

    private void ClearResult()
    {
        Summary = null;
        Outcome = null;
        Stamp = null;
        BandFill = 0;
        StatusText = null;
        SavedZipPath = null;
        Notes.Clear();
    }

    private SheetReport LayoutFor(CollectionReport report)
    {
        if (_layout is not null && ReferenceEquals(_layout.Source, report))
        {
            return _layout;
        }

        var layout = new SheetReport(report);
        _layout = layout;
        Columns = layout.Columns;
        foreach (var row in _rows)
        {
            row.ShowBlank(layout.Blanks);
        }

        return layout;
    }

    private void FillRows(SheetReport layout, IReadOnlyList<int> written)
    {
        var count = Math.Min(_rows.Count, Math.Min(layout.Rows.Count, written.Count));
        for (var i = 0; i < count; i++)
        {
            var target = layout.Rows[i];
            if (written[i] >= target.Expected)
            {
                _rows[i].ShowCounts(target);
            }
            else if (written[i] > 0)
            {
                _rows[i].ShowFilling((double)written[i] / target.Expected);
            }
            else
            {
                _rows[i].ShowBlank(layout.Blanks);
            }
        }
    }

    private void Set<T>(
        ref T storage, T value, ReadOnlySpan<string> alsoChanged = default, [CallerMemberName] string name = "")
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
        {
            return;
        }

        storage = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        foreach (var other in alsoChanged)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(other));
        }
    }
}
