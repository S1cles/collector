using System.Globalization;
using System.Text;

namespace SpectraCollector.Core;

/// <summary>The kind of problem a <see cref="ReportWarning"/> describes.</summary>
public enum WarningKind
{
    NoFiles,
    UnevenSpectrometers,
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
    private const int MinFilesForGapCheck = 3;
    private const int GapFactor = 3;
    private const string Newline = "\r\n";

    private CollectionReport(IReadOnlyList<SampleSummary> samples, IReadOnlyList<ReportWarning> warnings, int totalFiles)
    {
        Samples = samples;
        Warnings = warnings;
        TotalFiles = totalFiles;
    }

    /// <summary>One summary per sample, in input order.</summary>
    public IReadOnlyList<SampleSummary> Samples { get; }

    /// <summary>Warnings: per sample in input order, then empty hours ascending.</summary>
    public IReadOnlyList<ReportWarning> Warnings { get; }

    /// <summary>The number of unique files in the scan.</summary>
    public int TotalFiles { get; }

    /// <summary>Builds the report of <paramref name="scan"/> against <paramref name="samples"/>.</summary>
    public static CollectionReport Build(IReadOnlyList<Sample> samples, SourceScan scan)
    {
        var spectrometers = scan.Files.Select(f => f.Name.Spectrometer).Distinct().Order().ToList();
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

            foreach (var spectrometer in spectrometers)
            {
                var times = inSample.Where(f => f.Name.Spectrometer == spectrometer).Select(f => f.Name.Timestamp).Order();
                warnings.AddRange(Gaps(sample.Name, spectrometer, times.ToList()));
            }
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

    private static IEnumerable<ReportWarning> Gaps(string sample, int spectrometer, IReadOnlyList<DateTime> times)
    {
        if (times.Count < MinFilesForGapCheck)
        {
            yield break;
        }

        var intervals = Enumerable.Range(1, times.Count - 1).Select(i => times[i] - times[i - 1]).ToList();
        var median = Median(intervals);
        for (var i = 0; i < intervals.Count; i++)
        {
            if (intervals[i] > median * GapFactor)
            {
                var seconds = Math.Round(intervals[i].TotalSeconds).ToString(CultureInfo.InvariantCulture);
                var after = times[i].ToString("HH:mm:ss", CultureInfo.InvariantCulture);
                yield return new ReportWarning(
                    WarningKind.Gap, sample, $"{sample}: Spectra_{spectrometer} has a {seconds} s gap after {after}.");
            }
        }
    }

    private static TimeSpan Median(IReadOnlyList<TimeSpan> values)
    {
        var sorted = values.Order().ToList();
        var middle = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    private static string WindowText(TimeWindow window)
    {
        var from = window.From.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var sameDay = window.From.Date == window.To.Date;
        var to = window.To.ToString(sameDay ? "HH:mm:ss" : "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        return from + "–" + to;
    }
}
