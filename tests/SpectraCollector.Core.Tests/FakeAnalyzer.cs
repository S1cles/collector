using System.Globalization;
using System.IO.Compression;
using System.Text;
using SpectraCollector.Core;

namespace SpectraCollector.Core.Tests;

/// <summary>A throw-away analyzer data tree; the content of every file is its own file name.</summary>
public sealed class FakeAnalyzer : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("fake-analyzer-");

    public DataLayout Layout => new(_root.FullName, @"Main\Belt_1");

    public static string SpectrumFileName(int spectrometer, DateTime t) =>
        $"Spectra_{spectrometer}_Belt_1_{t.ToString("yyyy.MM.dd_HH.mm.ss", CultureInfo.InvariantCulture)}.csv";

    /// <summary>Writes a loose file to Tech_Spectra and returns its path.</summary>
    public string AddLoose(string fileName, DateTime hour)
    {
        var dir = Layout.TechSpectraDir(hour);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);
        File.WriteAllText(path, fileName);
        return path;
    }

    /// <summary>Writes the hour archive holding the named files, optionally under a folder inside the zip.</summary>
    public string AddArchive(DateTime hour, IEnumerable<string> fileNames, string? subfolder = null)
    {
        var path = Layout.HourArchive(hour);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var fileName in fileNames)
        {
            var entry = zip.CreateEntry(subfolder is null ? fileName : subfolder + "/" + fileName);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(fileName);
        }

        return path;
    }

    public void Dispose() => _root.Delete(true);
}
