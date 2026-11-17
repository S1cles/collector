using System.Globalization;

namespace SpectraCollector.Core;

/// <summary>Everything one collection needs.</summary>
/// <param name="CsvName">The name the sample times get in the zip: the input CSV's own name, or samples.csv.</param>
/// <param name="CsvBytes">The sample times exactly as they go into the zip: the input file's bytes, unconverted.</param>
public sealed record CollectRequest(
    IReadOnlyList<Sample> Samples, string CsvName, byte[] CsvBytes, DataLayout Layout, string OutputDir);

/// <summary>The stage of a collection that a <see cref="CollectProgress"/> describes.</summary>
public enum CollectPhase
{
    /// <summary>Looking for spectra; Done and Total count hours.</summary>
    Searching,

    /// <summary>Writing the zip; Done and Total count spectra.</summary>
    Writing,
}

/// <summary>How far a <paramref name="Phase"/> has come; a Total of 0 means the extent is not known.</summary>
/// <param name="Report">Set only on the first Writing event, so the window can fill in the sheet before the zip is done.</param>
/// <param name="WrittenPerSample">
/// Set on Writing events: a fresh snapshot of spectra written so far per sample, index-aligned with
/// <see cref="CollectRequest.Samples"/>. A spectrum inside two sample windows counts for both.
/// </param>
public sealed record CollectProgress(
    CollectPhase Phase,
    int Done,
    int Total,
    CollectionReport? Report = null,
    IReadOnlyList<int>? WrittenPerSample = null);

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
        if (request.Layout.Contains(request.OutputDir))
        {
            throw new OutputInsideDataException();
        }

        var scan = SpectraSource.Find(request.Layout, request.Samples, progress, ct);
        if (scan.Files.Count == 0)
        {
            var firstFolder = request.Layout.BeltDir(request.Samples.Min(s => s.Window.From));
            throw new NothingFoundException(
                $"No spectra found for these times in {firstFolder}. Check the times, the data folder and the belt.");
        }

        var report = CollectionReport.Build(request.Samples, scan);
        var earliest = request.Samples.Min(s => s.Begin);
        var baseName = "spectra_" + earliest.ToString("yyyy-MM-dd_HHmm", CultureInfo.InvariantCulture);
        var total = scan.Files.Count;
        var samplesOfFile = SamplesOfEachFile(request.Samples, scan.Files);
        var written = new int[request.Samples.Count];
        progress?.Report(new CollectProgress(CollectPhase.Writing, 0, total, report, [.. written]));
        var zipPath = OutputArchive.Write(
            request.OutputDir,
            baseName,
            scan.Files,
            request.CsvName,
            request.CsvBytes,
            report.ToText(),
            new DelegateProgress(done =>
            {
                foreach (var sample in samplesOfFile[done - 1])
                {
                    written[sample]++;
                }

                progress?.Report(new CollectProgress(CollectPhase.Writing, done, total, null, [.. written]));
            }),
            ct);
        return new CollectResult(zipPath, report);
    }

    private static int[][] SamplesOfEachFile(IReadOnlyList<Sample> samples, IReadOnlyList<FoundSpectrum> files)
    {
        var windows = samples.Select(s => s.Window).ToList();
        return files
            .Select(f => Enumerable.Range(0, windows.Count).Where(i => windows[i].Contains(f.Name.Timestamp)).ToArray())
            .ToArray();
    }

    private sealed class DelegateProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }
}
