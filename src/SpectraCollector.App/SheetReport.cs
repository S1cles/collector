using SpectraCollector.Core;

namespace SpectraCollector.App;

/// <summary>One numbered footnote under the table: a report warning and its 1-based number in report order.</summary>
public sealed record Footnote(string Number, string Text);

/// <summary>
/// What a <see cref="CollectionReport"/> puts on the sheet: one column per spectrometer, the finished look of every
/// row, totals and footnotes. A plain computation over Core types, built once when the report arrives.
/// </summary>
internal sealed class SheetReport
{
    public SheetReport(CollectionReport report)
    {
        Source = report;
        var spectrometers = report.TotalBySpectrometer.Keys.Order().ToList();
        Columns = spectrometers.Select(SheetText.Spectrometer).ToList();
        Blanks = Enumerable.Repeat(CountCell.Blank, spectrometers.Count).ToList();
        Totals = spectrometers.Select(n => SheetText.Count(report.TotalBySpectrometer[n])).ToList();
        TotalFiles = SheetText.Files(report.FileCount);
        Footnotes = report.Warnings
            .Select((w, i) => new Footnote(SheetText.Count(i + 1), w.Message))
            .ToList();
        Rows = report.Samples.Select(s => Target(s, spectrometers, report.Warnings)).ToList();
    }

    /// <summary>The report this layout was built from.</summary>
    public CollectionReport Source { get; }

    /// <summary>Column headers, one per spectrometer in ascending order.</summary>
    public IReadOnlyList<string> Columns { get; }

    /// <summary>One blank cell per column, shared by every row not reached yet.</summary>
    public IReadOnlyList<CountCell> Blanks { get; }

    /// <summary>The finished look of each row, index-aligned with the report's samples.</summary>
    public IReadOnlyList<RowTarget> Rows { get; }

    /// <summary>Unique files per spectrometer, aligned with <see cref="Columns"/>.</summary>
    public IReadOnlyList<string> Totals { get; }

    /// <summary>The unique spectra in the zip, e.g. <c>327 files</c>.</summary>
    public string TotalFiles { get; }

    public IReadOnlyList<Footnote> Footnotes { get; }

    private static RowTarget Target(
        SampleSummary summary, IReadOnlyList<int> spectrometers, IReadOnlyList<ReportWarning> warnings)
    {
        var counts = spectrometers
            .Select(n => summary.CountBySpectrometer.TryGetValue(n, out var count) ? count : 0)
            .ToList();
        var numbers = warnings
            .Select((w, i) => (w.Sample, Number: i + 1))
            .Where(w => w.Sample == summary.Sample.Name)
            .Select(w => w.Number)
            .ToList();
        return new RowTarget(
            counts.Sum(),
            counts.Select(c => new CountCell(SheetText.Count(c))).ToList(),
            numbers.Count == 0 ? NoteMark.Ok : NoteMark.Warning,
            SheetText.NoteNumbers(numbers));
    }
}
