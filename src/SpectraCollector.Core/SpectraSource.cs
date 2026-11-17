using System.Globalization;
using System.IO.Compression;

namespace SpectraCollector.Core;

/// <summary>The spectra found for a set of samples and the hours where the analyzer had nothing.</summary>
public sealed record SourceScan(IReadOnlyList<FoundSpectrum> Files, IReadOnlyList<DateTime> EmptyHours);

/// <summary>Finds spectrum files in hour archives and in Tech_Spectra.</summary>
public static class SpectraSource
{
    /// <summary>Scans every hour the samples touch; keeps files inside any sample window, once per file name; the archive copy wins over a loose one.</summary>
    /// <exception cref="ArchiveUnreadableException">An hour archive exists but cannot be read.</exception>
    public static SourceScan Find(DataLayout layout, IReadOnlyList<Sample> samples, CancellationToken ct)
    {
        var windows = samples.Select(s => s.Window).ToList();
        var hours = windows.SelectMany(w => w.Hours()).Distinct().Order().ToList();
        var found = new Dictionary<string, FoundSpectrum>(StringComparer.OrdinalIgnoreCase);
        var emptyHours = new List<DateTime>();

        foreach (var hour in hours)
        {
            ct.ThrowIfCancellationRequested();
            var hourHasFiles = ScanLoose(layout.TechSpectraDir(hour), hour, windows, found);
            hourHasFiles |= ScanArchive(layout.HourArchive(hour), windows, found, ct);
            if (!hourHasFiles)
            {
                emptyHours.Add(hour);
            }
        }

        var files = found.Values
            .OrderBy(f => f.Name.Timestamp)
            .ThenBy(f => f.Name.Spectrometer)
            .ThenBy(f => f.Name.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new SourceScan(files, emptyHours);
    }

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

    private static bool ScanLoose(
        string dir, DateTime hour, List<TimeWindow> windows, Dictionary<string, FoundSpectrum> found)
    {
        if (!Directory.Exists(dir))
        {
            return false;
        }

        var pattern = "Spectra_*_" + hour.ToString("yyyy.MM.dd_HH", CultureInfo.InvariantCulture) + "*";
        var any = false;
        foreach (var path in Directory.EnumerateFiles(dir, pattern))
        {
            if (SpectrumName.TryParse(Path.GetFileName(path), out var name))
            {
                any = true;
                Keep(found, windows, new FoundSpectrum(name, path, null), replace: false);
            }
        }

        return any;
    }

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
