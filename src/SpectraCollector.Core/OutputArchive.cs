using System.IO.Compression;
using System.Text;

namespace SpectraCollector.Core;

/// <summary>Writes the output zip atomically: a .partial file that is renamed only when complete.</summary>
public static class OutputArchive
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>Writes the zip into <paramref name="outputDir"/> and returns its path.</summary>
    /// <param name="baseName">Zip name without extension; also the folder inside the zip.</param>
    /// <param name="csvBytes">Written verbatim, so the engineer gets back exactly the file they gave.</param>
    /// <param name="filesWritten">Receives the running count after each spectrum is written.</param>
    public static string Write(
        string outputDir,
        string baseName,
        IReadOnlyList<FoundSpectrum> files,
        string csvName,
        byte[] csvBytes,
        string reportText,
        IProgress<int>? filesWritten,
        CancellationToken ct)
    {
        Directory.CreateDirectory(outputDir);
        var zipPath = FreeZipPath(outputDir, baseName);
        var partialPath = zipPath + ".partial";
        try
        {
            using (var stream = new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                WriteSpectra(zip, baseName, files, filesWritten, ct);
                WriteEntry(zip, baseName + "/" + csvName, csvBytes);
                WriteEntry(zip, baseName + "/collect-report.txt", Utf8NoBom.GetBytes(reportText));
            }

            File.Move(partialPath, zipPath);
            return zipPath;
        }
        catch
        {
            File.Delete(partialPath);
            throw;
        }
    }

    private static string FreeZipPath(string outputDir, string baseName)
    {
        var path = Path.Combine(outputDir, baseName + ".zip");
        for (var n = 2; File.Exists(path); n++)
        {
            path = Path.Combine(outputDir, $"{baseName}_{n}.zip");
        }

        return path;
    }

    private static void WriteSpectra(
        ZipArchive zip, string baseName, IReadOnlyList<FoundSpectrum> files, IProgress<int>? filesWritten, CancellationToken ct)
    {
        var written = 0;
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var entry = zip.CreateEntry(baseName + "/Tech_Spectra/" + file.Name.FileName, CompressionLevel.Optimal);
            entry.LastWriteTime = file.Name.Timestamp;
            using var target = entry.Open();
            Copy(file, target, ct);
            filesWritten?.Report(++written);
        }
    }

    internal static void Copy(FoundSpectrum file, Stream target, CancellationToken ct)
    {
        var buffer = new byte[81920];
        using var source = OpenSource(file);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var read = ReadSource(file, source, buffer);
            if (read == 0)
            {
                return;
            }

            target.Write(buffer, 0, read);
        }
    }

    private static Stream OpenSource(FoundSpectrum file)
    {
        try
        {
            return file.Open();
        }
        catch (Exception e) when (IsReadFailure(e))
        {
            throw new SpectrumCopyException(file.Name.FileName, file.SourcePath, e);
        }
    }

    private static int ReadSource(FoundSpectrum file, Stream source, byte[] buffer)
    {
        try
        {
            return source.Read(buffer, 0, buffer.Length);
        }
        catch (Exception e) when (IsReadFailure(e))
        {
            throw new SpectrumCopyException(file.Name.FileName, file.SourcePath, e);
        }
    }

    private static bool IsReadFailure(Exception e) =>
        e is IOException or UnauthorizedAccessException or InvalidDataException;

    private static void WriteEntry(ZipArchive zip, string entryName, byte[] content)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var target = entry.Open();
        target.Write(content);
    }
}
