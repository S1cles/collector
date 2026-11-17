using System.IO.Compression;

namespace SpectraCollector.Core;

/// <summary>The spectra found for a set of samples and the hours where the analyzer had nothing.</summary>
public sealed record SourceScan(IReadOnlyList<FoundSpectrum> Files, IReadOnlyList<DateTime> EmptyHours);

/// <summary>Finds spectrum files in hour archives and in Tech_Spectra.</summary>
public static class SpectraSource
{
    private const string SpectrumPattern = "Spectra_*";

    /// <summary>The same search as the overload with progress, without progress reports.</summary>
    /// <exception cref="ArchiveUnreadableException">An hour archive exists but cannot be read.</exception>
    public static SourceScan Find(DataLayout layout, IReadOnlyList<Sample> samples, CancellationToken ct) =>
        Find(layout, samples, null, ct);

    /// <summary>
    /// Scans every hour the samples touch and keeps files inside any sample window, once per file name.
    /// Each month's Tech_Spectra is listed once, before any of that month's hour archives is opened, and an archive
    /// copy replaces a loose one; so an hour the archiver packs during the scan is still found.
    /// </summary>
    /// <param name="progress">Receives <see cref="CollectPhase.Searching"/> with hours done out of hours to scan.</param>
    /// <exception cref="ArchiveUnreadableException">An hour archive exists but cannot be read.</exception>
    public static SourceScan Find(
        DataLayout layout, IReadOnlyList<Sample> samples, IProgress<CollectProgress>? progress, CancellationToken ct) =>
        Find(layout, samples, progress, dir => Directory.EnumerateFiles(dir, SpectrumPattern), ct);

    internal static SourceScan Find(
        DataLayout layout,
        IReadOnlyList<Sample> samples,
        IProgress<CollectProgress>? progress,
        Func<string, IEnumerable<string>> listLoose,
        CancellationToken ct)
    {
        var windows = samples.Select(s => s.Window).ToList();
        var hours = windows.SelectMany(w => w.Hours()).Distinct().Order().ToList();
        var reportable = HoursCoveredPastTheirStart(windows);
        var found = new Dictionary<string, FoundSpectrum>(StringComparer.OrdinalIgnoreCase);
        var emptyHours = new List<DateTime>();
        var done = 0;
        progress?.Report(new CollectProgress(CollectPhase.Searching, done, hours.Count));

        foreach (var month in hours.GroupBy(layout.TechSpectraDir))
        {
            var looseHours = ScanLoose(month.Key, month.ToHashSet(), windows, found, listLoose, ct);
            foreach (var hour in month)
            {
                ct.ThrowIfCancellationRequested();
                var hasArchive = ScanArchive(layout.HourArchive(hour), windows, found, ct);
                if (!hasArchive && !looseHours.Contains(hour) && reportable.Contains(hour))
                {
                    emptyHours.Add(hour);
                }

                progress?.Report(new CollectProgress(CollectPhase.Searching, ++done, hours.Count));
            }
        }

        var files = found.Values
            .OrderBy(f => f.Name.Timestamp)
            .ThenBy(f => f.Name.Spectrometer)
            .ThenBy(f => f.Name.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new SourceScan(files, emptyHours);
    }

    /// <summary>A window that ends exactly on the hour touches that hour only at its first instant; that is no reason to report the hour as empty.</summary>
    private static HashSet<DateTime> HoursCoveredPastTheirStart(List<TimeWindow> windows) =>
        windows.SelectMany(w => w.Hours().Where(hour => hour < w.To)).ToHashSet();

    private static bool ScanArchive(
        string path, List<TimeWindow> windows, Dictionary<string, FoundSpectrum> found, CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            using var stream = FoundSpectrum.OpenShared(path);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            foreach (var entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();
                var fileName = Path.GetFileName(entry.FullName);
                if (fileName.Length > 0 && SpectrumName.TryParse(fileName, out var name))
                {
                    Keep(found, windows, new FoundSpectrum(name, path, entry.FullName), replace: true);
                }
            }
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            throw new ArchiveUnreadableException(path, e);
        }

        return true;
    }

    /// <summary>Lists <paramref name="dir"/> once and returns the wanted hours that have at least one spectrum there.</summary>
    private static HashSet<DateTime> ScanLoose(
        string dir,
        HashSet<DateTime> wantedHours,
        List<TimeWindow> windows,
        Dictionary<string, FoundSpectrum> found,
        Func<string, IEnumerable<string>> listLoose,
        CancellationToken ct)
    {
        var hoursWithFiles = new HashSet<DateTime>();
        if (!Directory.Exists(dir))
        {
            return hoursWithFiles;
        }

        foreach (var path in listLoose(dir))
        {
            ct.ThrowIfCancellationRequested();
            if (!SpectrumName.TryParse(Path.GetFileName(path), out var name))
            {
                continue;
            }

            var hour = HourOf(name.Timestamp);
            if (wantedHours.Contains(hour))
            {
                hoursWithFiles.Add(hour);
                Keep(found, windows, new FoundSpectrum(name, path, null), replace: false);
            }
        }

        return hoursWithFiles;
    }

    private static DateTime HourOf(DateTime t) => new(t.Ticks - t.Ticks % TimeSpan.TicksPerHour, t.Kind);

    private static void Keep(Dictionary<string, FoundSpectrum> found, List<TimeWindow> windows, FoundSpectrum spectrum, bool replace)
    {
        if (!windows.Any(w => w.Contains(spectrum.Name.Timestamp)))
        {
            return;
        }

        if (replace)
        {
            found[spectrum.Name.FileName] = spectrum;
        }
        else
        {
            found.TryAdd(spectrum.Name.FileName, spectrum);
        }
    }
}
