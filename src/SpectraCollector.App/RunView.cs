using System.Globalization;
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

/// <summary>One line of plain text in the result area.</summary>
public sealed record ResultLine(string Text, ResultTone Tone);

/// <summary>One spectrometer's file count within a sample row, e.g. <c>Spectra_0</c> and <c>43</c>.</summary>
public sealed record SpectrometerCount(string Label, string Count);

/// <summary>One sample of a finished collection, formatted for the result area.</summary>
public sealed record SampleRow(string Name, string Window, IReadOnlyList<SpectrometerCount> Counts, bool HasWarning)
{
    private const string TimeFormat = "HH:mm:ss";
    private const string DayFormat = "yyyy-MM-dd HH:mm:ss";

    internal static SampleRow From(SampleSummary summary, IReadOnlySet<string> warnedSamples) => new(
        summary.Sample.Name,
        WindowText(summary.Sample.Window),
        summary.CountBySpectrometer
            .OrderBy(c => c.Key)
            .Select(c => new SpectrometerCount(
                string.Create(CultureInfo.InvariantCulture, $"Spectra_{c.Key}"),
                c.Value.ToString(CultureInfo.InvariantCulture)))
            .ToList(),
        warnedSamples.Contains(summary.Sample.Name));

    private static string WindowText(TimeWindow window)
    {
        var format = window.From.Date == window.To.Date ? TimeFormat : DayFormat;
        return window.From.ToString(format, CultureInfo.InvariantCulture)
            + "–"
            + window.To.ToString(format, CultureInfo.InvariantCulture);
    }
}

/// <summary>What the result area shows: the report and the saved zip after a success, otherwise one outcome line.</summary>
public sealed record RunView(
    IReadOnlyList<SampleRow> Samples, IReadOnlyList<string> Warnings, string? ZipPath, ResultLine? Outcome)
{
    private const string WarningMark = "⚠ ";

    /// <summary>Nothing to show yet.</summary>
    public static RunView Empty { get; } = new([], [], null, null);

    /// <summary>True when a zip was written.</summary>
    public bool HasZip => ZipPath is not null;

    /// <summary>The success line under the report.</summary>
    public string SavedText => ZipPath is null ? string.Empty : "Saved " + ZipPath;

    /// <summary>The report of a successful collection.</summary>
    public static RunView Succeeded(CollectResult result)
    {
        var warned = result.Report.Warnings.Select(w => w.Sample).OfType<string>().ToHashSet();
        return new RunView(
            result.Report.Samples.Select(s => SampleRow.From(s, warned)).ToList(),
            result.Report.Warnings.Select(w => WarningMark + w.Message).ToList(),
            result.ZipPath,
            null);
    }

    /// <summary>A run that saved nothing, described by <paramref name="outcome"/>.</summary>
    public static RunView Ended(ResultLine outcome) => new([], [], null, outcome);
}
