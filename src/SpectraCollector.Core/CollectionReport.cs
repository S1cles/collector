using System.Globalization;
using System.Text;

namespace SpectraCollector.Core;

/// <summary>The kind of problem a <see cref="ReportWarning"/> describes.</summary>
public enum WarningKind
{
    NoFiles,
    UnevenSpectrometers,

    /// <summary>A spectrometer's first file in a sample comes well after the sample's Begin.</summary>
    LateStart,

    /// <summary>A spectrometer's last file in a sample comes well before the sample's End.</summary>
    EarlyEnd,
    Gap,
    EmptyHour,
}

/// <summary>A coverage problem; <paramref name="Sample"/> is null when it concerns no single sample.</summary>
public sealed record ReportWarning(WarningKind Kind, string? Sample, string Message);

/// <summary>File counts of one sample, with an entry for every spectrometer seen in the scan.</summary>
public sealed record SampleSummary(Sample Sample, IReadOnlyDictionary<int, int> CountBySpectrometer);

/// <summary>Per-sample coverage of a scan, with warnings about missing or patchy data.</summary>
public sealed class CollectionReport
{
    private const int MinFilesForMedian = 3;
    private const int IntervalFactor = 3;
    private const string Newline = "\r\n";
    private const string ClockFormat = "HH:mm:ss";
    private static readonly TimeSpan UnknownInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MinEdgeAllowance = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan LongestSecondsText = TimeSpan.FromSeconds(120);

    private CollectionReport(IReadOnlyList<SampleSummary> samples, IReadOnlyList<ReportWarning> warnings, int totalFiles)
    {
        Samples = samples;
        Warnings = warnings;
        TotalFiles = totalFiles;
    }

    /// <summary>One summary per sample, in input order.</summary>
    public IReadOnlyList<SampleSummary> Samples { get; }

    /// <summary>
    /// Warnings: per sample in input order, then empty hours ascending. Inside a sample the order is no files,
    /// uneven spectrometers, late starts, early ends, gaps; spectrometers ascending within each kind.
    /// </summary>
    public IReadOnlyList<ReportWarning> Warnings { get; }

    /// <summary>The number of unique files in the scan.</summary>
    public int TotalFiles { get; }

    /// <summary>Builds the report of <paramref name="scan"/> against <paramref name="samples"/>.</summary>
    public static CollectionReport Build(IReadOnlyList<Sample> samples, SourceScan scan)
    {
        var spectrometers = scan.Files.Select(f => f.Name.Spectrometer).Distinct().Order().ToList();
        var scanIntervals = spectrometers.ToDictionary(n => n, n => MedianInterval(TimesOf(scan.Files, n)));
        var summaries = new List<SampleSummary>();
        var warnings = new List<ReportWarning>();

        foreach (var sample in samples)
        {
            var window = sample.Window;
            var inSample = scan.Files.Where(f => window.Contains(f.Name.Timestamp)).ToList();
            var counts = spectrometers.ToDictionary(n => n, n => inSample.Count(f => f.Name.Spectrometer == n));
            summaries.Add(new SampleSummary(sample, counts));

            if (inSample.Count == 0)
            {
                warnings.Add(new ReportWarning(
                    WarningKind.NoFiles, sample.Name, $"{sample.Name}: no spectra found in {WindowText(window)}."));
                continue;
            }

            if (counts.Values.Distinct().Count() > 1)
            {
                var detail = string.Join(", ", counts.Select(c => $"Spectra_{c.Key}: {c.Value}"));
                warnings.Add(new ReportWarning(
                    WarningKind.UnevenSpectrometers,
                    sample.Name,
                    $"{sample.Name}: spectrometers have different file counts ({detail})."));
            }

            var series = spectrometers
                .Select(n => new Series(n, TimesOf(inSample, n), scanIntervals[n]))
                .Where(s => s.Times.Count > 0)
                .ToList();
            warnings.AddRange(series.Select(s => LateStart(sample, s)).OfType<ReportWarning>());
            warnings.AddRange(series.Select(s => EarlyEnd(sample, s)).OfType<ReportWarning>());
            warnings.AddRange(series.SelectMany(s => Gaps(sample.Name, s)));
        }

        foreach (var hour in scan.EmptyHours)
        {
            var text = hour.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                + "–" + hour.AddHours(1).ToString("HH:mm", CultureInfo.InvariantCulture);
            warnings.Add(new ReportWarning(
                WarningKind.EmptyHour,
                null,
                $"No spectra at all for {text} (no hour archive and no files in Tech_Spectra)."));
        }

        return new CollectionReport(summaries, warnings, scan.Files.Count);
    }

    /// <summary>Renders the report as plain text with CRLF line endings.</summary>
    public string ToText()
    {
        var width = Samples.Count == 0 ? 0 : Samples.Max(s => s.Sample.Name.Length);
        var text = new StringBuilder();
        text.Append("SpectraCollector report").Append(Newline);
        text.Append(CultureInfo.InvariantCulture, $"Samples: {Samples.Count}, files: {TotalFiles}").Append(Newline);
        text.Append(Newline);
        foreach (var summary in Samples)
        {
            var counts = string.Join("  ", summary.CountBySpectrometer.Select(c => $"Spectra_{c.Key}: {c.Value}"));
            text.Append(summary.Sample.Name.PadRight(width))
                .Append("  ")
                .Append(WindowText(summary.Sample.Window))
                .Append("  ")
                .Append(counts)
                .Append(Newline);
        }

        text.Append(Newline);
        if (Warnings.Count == 0)
        {
            text.Append("Warnings: none").Append(Newline);
        }
        else
        {
            text.Append("Warnings:").Append(Newline);
            foreach (var warning in Warnings)
            {
                text.Append("- ").Append(warning.Message).Append(Newline);
            }
        }

        return text.ToString();
    }

    /// <summary>One spectrometer's file times in a sample, with its median interval over the whole scan.</summary>
    private sealed record Series(int Spectrometer, IReadOnlyList<DateTime> Times, TimeSpan? ScanInterval)
    {
        public string Label => string.Create(CultureInfo.InvariantCulture, $"Spectra_{Spectrometer}");

        /// <summary>How far the first or last file may sit from Begin or End: three typical intervals, at least a minute.</summary>
        public TimeSpan EdgeAllowance
        {
            get
            {
                var allowance = (MedianInterval(Times) ?? ScanInterval ?? UnknownInterval) * IntervalFactor;
                return allowance > MinEdgeAllowance ? allowance : MinEdgeAllowance;
            }
        }
    }

    private static List<DateTime> TimesOf(IEnumerable<FoundSpectrum> files, int spectrometer) =>
        files.Where(f => f.Name.Spectrometer == spectrometer).Select(f => f.Name.Timestamp).Order().ToList();

    private static ReportWarning? LateStart(Sample sample, Series series)
    {
        var first = series.Times[0];
        var late = first - sample.Begin;
        return late <= series.EdgeAllowance
            ? null
            : new ReportWarning(
                WarningKind.LateStart,
                sample.Name,
                $"{sample.Name}: {series.Label} starts at {Clock(first)}, {DurationText(late)} after Begin.");
    }

    private static ReportWarning? EarlyEnd(Sample sample, Series series)
    {
        var last = series.Times[^1];
        var early = sample.End - last;
        return early <= series.EdgeAllowance
            ? null
            : new ReportWarning(
                WarningKind.EarlyEnd,
                sample.Name,
                $"{sample.Name}: {series.Label} ends at {Clock(last)}, {DurationText(early)} before End.");
    }

    private static IEnumerable<ReportWarning> Gaps(string sample, Series series)
    {
        if (series.Times.Count < MinFilesForMedian)
        {
            yield break;
        }

        var intervals = Intervals(series.Times);
        var median = Median(intervals);
        if (median <= TimeSpan.Zero)
        {
            yield break;
        }

        for (var i = 0; i < intervals.Count; i++)
        {
            if (intervals[i] > median * IntervalFactor)
            {
                var seconds = Math.Round(intervals[i].TotalSeconds).ToString(CultureInfo.InvariantCulture);
                yield return new ReportWarning(
                    WarningKind.Gap,
                    sample,
                    $"{sample}: {series.Label} has a {seconds} s gap after {Clock(series.Times[i])}.");
            }
        }
    }

    private static TimeSpan? MedianInterval(IReadOnlyList<DateTime> times) =>
        times.Count < MinFilesForMedian ? null : Median(Intervals(times));

    private static List<TimeSpan> Intervals(IReadOnlyList<DateTime> times) =>
        Enumerable.Range(1, times.Count - 1).Select(i => times[i] - times[i - 1]).ToList();

    private static TimeSpan Median(IReadOnlyList<TimeSpan> values)
    {
        var sorted = values.Order().ToList();
        var middle = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    private static string Clock(DateTime t) => t.ToString(ClockFormat, CultureInfo.InvariantCulture);

    /// <summary>Under two minutes in seconds, otherwise in whole minutes, e.g. <c>70 s</c> or <c>4 min</c>.</summary>
    private static string DurationText(TimeSpan duration) =>
        duration < LongestSecondsText
            ? Math.Round(duration.TotalSeconds, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture) + " s"
            : Math.Round(duration.TotalMinutes, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture) + " min";

    private static string WindowText(TimeWindow window)
    {
        var from = window.From.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var sameDay = window.From.Date == window.To.Date;
        var to = window.To.ToString(sameDay ? "HH:mm:ss" : "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        return from + "–" + to;
    }
}
