using System.ComponentModel;
using SpectraCollector.Core;

namespace SpectraCollector.App;

/// <summary>What the Note column of a sample row shows.</summary>
public enum NoteMark
{
    None,
    Ok,
    Warning,
}

/// <summary>One count cell of a row; a null <see cref="Text"/> is the dotted blank of a count not known yet.</summary>
public sealed record CountCell(string? Text)
{
    /// <summary>A count not known yet.</summary>
    public static CountCell Blank { get; } = new((string?)null);

    /// <summary>True for <see cref="Blank"/>.</summary>
    public bool IsBlank => Text is null;
}

/// <summary>The finished look of one row, taken from the report: counts per spectrometer and the Note.</summary>
/// <param name="Expected">Files the row is finished at: the sum of the sample's counts over all spectrometers.</param>
internal sealed record RowTarget(int Expected, IReadOnlyList<CountCell> Cells, NoteMark Note, string NoteNumbers);

/// <summary>
/// One sample line of the sheet: its rounded window, then blank cells, a filling bar while its files are written,
/// or its counts and Note once they are. Only changes raise <see cref="PropertyChanged"/>, so a progress tick that
/// leaves a row as it was costs nothing in the UI.
/// </summary>
public sealed class SheetRow : INotifyPropertyChanged
{
    private static readonly string[] Shown =
        [nameof(Cells), nameof(IsCollecting), nameof(Fill), nameof(Note), nameof(NoteNumbers)];

    private RowTarget? _shownTarget;

    internal SheetRow(Sample sample, IReadOnlyList<CountCell> blanks)
    {
        Name = sample.Name;
        From = SheetText.From(sample.Window);
        To = SheetText.To(sample.Window);
        Cells = blanks;
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; }

    public string From { get; }

    public string To { get; }

    public IReadOnlyList<CountCell> Cells { get; private set; }

    /// <summary>True while some but not all of the row's files are written; the row then shows a bar instead of cells.</summary>
    public bool IsCollecting { get; private set; }

    /// <summary>The written share of the row's files, 0 to 1, while <see cref="IsCollecting"/>.</summary>
    public double Fill { get; private set; }

    public NoteMark Note { get; private set; }

    /// <summary>The footnote numbers after the warning mark, e.g. <c>1, 3</c>; empty unless <see cref="Note"/> is a warning.</summary>
    public string NoteNumbers { get; private set; } = string.Empty;

    internal void ShowBlank(IReadOnlyList<CountCell> blanks)
    {
        if (!IsCollecting && _shownTarget is null && ReferenceEquals(Cells, blanks))
        {
            return;
        }

        _shownTarget = null;
        Show(blanks, collecting: false, fill: 0, NoteMark.None, string.Empty);
    }

    internal void ShowFilling(double fill)
    {
        if (IsCollecting && Fill.Equals(fill))
        {
            return;
        }

        _shownTarget = null;
        Show(Cells, collecting: true, fill, NoteMark.None, string.Empty);
    }

    internal void ShowCounts(RowTarget target)
    {
        if (ReferenceEquals(_shownTarget, target))
        {
            return;
        }

        _shownTarget = target;
        Show(target.Cells, collecting: false, fill: 0, target.Note, target.NoteNumbers);
    }

    private void Show(IReadOnlyList<CountCell> cells, bool collecting, double fill, NoteMark note, string numbers)
    {
        Cells = cells;
        IsCollecting = collecting;
        Fill = fill;
        Note = note;
        NoteNumbers = numbers;
        foreach (var name in Shown)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
