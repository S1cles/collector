using System.Globalization;

namespace SpectraCollector.Core;

/// <summary>Everything one collection needs.</summary>
public sealed record CollectRequest(
    IReadOnlyList<Sample> Samples, string CsvName, string CsvText, DataLayout Layout, string OutputDir);

/// <summary>Spectra written so far out of the total to write.</summary>
public sealed record CollectProgress(int Done, int Total);

/// <summary>The finished zip and the report that went into it.</summary>
public sealed record CollectResult(string ZipPath, CollectionReport Report);

/// <summary>Runs one whole collection: search, report, zip.</summary>
public static class Collector
{
    /// <summary>Runs on the thread pool; throws <see cref="NothingFoundException"/> when no spectra match.</summary>
    public static Task<CollectResult> RunAsync(
        CollectRequest request, IProgress<CollectProgress>? progress, CancellationToken ct) =>
        Task.Run(() => Run(request, progress, ct), ct);

    private static CollectResult Run(CollectRequest request, IProgress<CollectProgress>? progress, CancellationToken ct)
    {
        var scan = SpectraSource.Find(request.Layout, request.Samples, ct);
        if (scan.Files.Count == 0)
        {
            throw new NothingFoundException("No spectra found for these times. Check the times and the LyncisAC data folder.");
        }

        var report = CollectionReport.Build(request.Samples, scan);
        var earliest = request.Samples.Min(s => s.Begin);
        var baseName = "spectra_" + earliest.ToString("yyyy-MM-dd_HHmm", CultureInfo.InvariantCulture);
        var total = scan.Files.Count;
        progress?.Report(new CollectProgress(0, total));
        var zipPath = OutputArchive.Write(
            request.OutputDir,
            baseName,
            scan.Files,
            request.CsvName,
            request.CsvText,
            report.ToText(),
            new DelegateProgress(done => progress?.Report(new CollectProgress(done, total))),
            ct);
        return new CollectResult(zipPath, report);
    }

    private sealed class DelegateProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }
}
