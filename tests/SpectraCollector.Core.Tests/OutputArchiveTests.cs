using System.IO.Compression;
using SpectraCollector.Core;

namespace SpectraCollector.Core.Tests;

public sealed class OutputArchiveTests : IDisposable
{
    private static readonly DateTime Hour = new(2026, 11, 17, 13, 0, 0);
    private readonly FakeAnalyzer _analyzer = new();
    private readonly DirectoryInfo _out = Directory.CreateTempSubdirectory("out-");

    public void Dispose()
    {
        _analyzer.Dispose();
        _out.Delete(true);
    }

    private FoundSpectrum Loose(int spectrometer, DateTime t)
    {
        var fileName = FakeAnalyzer.SpectrumFileName(spectrometer, t);
        var path = _analyzer.AddLoose(fileName, Hour);
        Assert.True(SpectrumName.TryParse(fileName, out var name));
        return new FoundSpectrum(name, path, null);
    }

    private string Write(string dir, IReadOnlyList<FoundSpectrum> files, IProgress<int>? progress = null, CancellationToken ct = default) =>
        OutputArchive.Write(dir, "spectra_x", files, "samples.csv", "a,b\n", "report", progress, ct);

    [Fact]
    public void Zip_has_spectra_csv_and_report_with_identical_bytes()
    {
        var first = Loose(1, Hour.AddMinutes(1));
        var second = Loose(2, Hour.AddMinutes(2));

        var zipPath = Write(_out.FullName, [first, second]);

        using var zip = ZipFile.OpenRead(zipPath);
        Assert.Equal(
            new[]
            {
                "spectra_x/Tech_Spectra/" + first.Name.FileName,
                "spectra_x/Tech_Spectra/" + second.Name.FileName,
                "spectra_x/samples.csv",
                "spectra_x/collect-report.txt",
            }.Order(),
            zip.Entries.Select(e => e.FullName).Order());
        foreach (var file in new[] { first, second })
        {
            var entry = zip.GetEntry("spectra_x/Tech_Spectra/" + file.Name.FileName)!;
            using var reader = new StreamReader(entry.Open());
            Assert.Equal(File.ReadAllText(file.SourcePath), reader.ReadToEnd());
            Assert.Equal(file.Name.Timestamp, entry.LastWriteTime.DateTime);
        }

        using var csv = zip.GetEntry("spectra_x/samples.csv")!.Open();
        Assert.Equal(new byte[] { (byte)'a', (byte)',', (byte)'b', (byte)'\n' }, new BinaryReader(csv).ReadBytes(4));
        Assert.Empty(Directory.GetFiles(_out.FullName, "*.partial"));
    }

    [Fact]
    public void Missing_output_dir_is_created()
    {
        var dir = Path.Combine(_out.FullName, "a", "b");

        var zipPath = Write(dir, [Loose(1, Hour.AddMinutes(1))]);

        Assert.True(File.Exists(zipPath));
    }

    [Fact]
    public void Taken_name_gets_a_numeric_suffix()
    {
        var files = new[] { Loose(1, Hour.AddMinutes(1)) };

        var first = Write(_out.FullName, files);
        var second = Write(_out.FullName, files);
        var third = Write(_out.FullName, files);

        Assert.Equal("spectra_x.zip", Path.GetFileName(first));
        Assert.Equal("spectra_x_2.zip", Path.GetFileName(second));
        Assert.Equal("spectra_x_3.zip", Path.GetFileName(third));
    }

    [Fact]
    public void Progress_counts_each_written_spectrum()
    {
        var reported = new List<int>();

        Write(_out.FullName, [Loose(1, Hour.AddMinutes(1)), Loose(2, Hour.AddMinutes(2))], new ListProgress(reported.Add));

        Assert.Equal([1, 2], reported);
    }

    [Fact]
    public void Unreadable_spectrum_fails_and_leaves_no_files()
    {
        var good = Loose(1, Hour.AddMinutes(1));
        var bad = good with { SourcePath = Path.Combine(_analyzer.Layout.DataRoot, "gone.csv") };

        var e = Assert.Throws<SpectrumCopyException>(() => Write(_out.FullName, [good, bad]));

        Assert.IsType<FileNotFoundException>(e.InnerException, exactMatch: false);
        Assert.Equal(
            $"Cannot read {bad.Name.FileName} from {bad.SourcePath}: {e.InnerException!.Message.TrimEnd('.')}.", e.Message);
        Assert.DoesNotContain("..", e.Message);
        Assert.Empty(Directory.GetFileSystemEntries(_out.FullName));
    }

    [Fact]
    public void Target_write_failure_is_not_a_spectrum_copy_failure()
    {
        var file = Loose(1, Hour.AddMinutes(1));

        Assert.Throws<IOException>(() => OutputArchive.Copy(file, new ThrowingWriteStream(), CancellationToken.None));
    }

    [Fact]
    public void Source_read_failure_is_a_spectrum_copy_failure()
    {
        var bad = Loose(1, Hour.AddMinutes(1)) with { SourcePath = Path.Combine(_out.FullName, "gone.csv") };

        Assert.Throws<SpectrumCopyException>(() => OutputArchive.Copy(bad, new MemoryStream(), CancellationToken.None));
    }

    [Fact]
    public void Cancel_mid_write_leaves_no_files()
    {
        using var cts = new CancellationTokenSource();
        var files = new[] { Loose(1, Hour.AddMinutes(1)), Loose(2, Hour.AddMinutes(2)), Loose(3, Hour.AddMinutes(3)) };

        Assert.ThrowsAny<OperationCanceledException>(() =>
            Write(_out.FullName, files, new ListProgress(n => { if (n == 1) cts.Cancel(); }), cts.Token));

        Assert.Empty(Directory.GetFileSystemEntries(_out.FullName));
    }
}

internal sealed class ListProgress(Action<int> report) : IProgress<int>
{
    public void Report(int value) => report(value);
}

internal sealed class ThrowingWriteStream : MemoryStream
{
    public override void Write(byte[] buffer, int offset, int count) => throw new IOException("disk full");
}
