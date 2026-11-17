using System.Text;
using SpectraCollector.Core;

namespace SpectraCollector.Core.Tests;

public class SpectraSourceTests : IDisposable
{
    private readonly FakeAnalyzer _analyzer = new();

    private static DateTime At(int hour, int minute, int second) => new(2026, 11, 6, hour, minute, second);

    private static string Name(int spectrometer, DateTime t) => FakeAnalyzer.SpectrumFileName(spectrometer, t);

    private static Sample Sample(DateTime begin, DateTime end) => new("S01", begin, end);

    private SourceScan Find(params Sample[] samples) =>
        SpectraSource.Find(_analyzer.Layout, samples, CancellationToken.None);

    private static string[] Names(SourceScan scan) => scan.Files.Select(f => f.Name.FileName).ToArray();

    private static string ReadAll(FoundSpectrum f)
    {
        using var stream = f.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    public void Dispose() => _analyzer.Dispose();

    [Fact]
    public void Find_LooseOnly_ReturnsFilesInWindow()
    {
        var a = Name(0, At(13, 31, 0));
        var b = Name(0, At(13, 32, 0));
        _analyzer.AddLoose(a, At(13, 0, 0));
        _analyzer.AddLoose(b, At(13, 0, 0));

        var scan = Find(Sample(At(13, 30, 30), At(13, 40, 1)));

        Assert.Equal([a, b], Names(scan));
        Assert.Empty(scan.EmptyHours);
        Assert.All(scan.Files, f => Assert.Null(f.EntryName));
    }

    [Fact]
    public void Find_ArchiveOnly_ReturnsEntriesThatOpen()
    {
        var a = Name(0, At(13, 31, 0));
        _analyzer.AddArchive(At(13, 0, 0), [a, Name(0, At(14, 31, 0))]);

        var scan = Find(Sample(At(13, 30, 0), At(13, 40, 0)));

        var file = Assert.Single(scan.Files);
        Assert.Equal(a, file.Name.FileName);
        Assert.Equal(a, file.EntryName);
        Assert.Equal(a, ReadAll(file));
    }

    [Fact]
    public void Find_ArchivedHourAndLooseHour_AreBothCollected()
    {
        var a = Name(0, At(13, 50, 0));
        var b = Name(0, At(14, 10, 0));
        _analyzer.AddArchive(At(13, 0, 0), [a]);
        _analyzer.AddLoose(b, At(14, 0, 0));

        var scan = Find(Sample(At(13, 45, 0), At(14, 15, 0)));

        Assert.Equal([a, b], Names(scan));
        Assert.NotNull(scan.Files[0].EntryName);
        Assert.Null(scan.Files[1].EntryName);
    }

    [Fact]
    public void Find_WindowAcrossMonthBoundary_ReadsBothMonthFolders()
    {
        var a = Name(0, new DateTime(2026, 10, 31, 23, 59, 0));
        var b = Name(0, new DateTime(2026, 11, 1, 0, 1, 0));
        _analyzer.AddLoose(a, new DateTime(2026, 10, 31, 23, 0, 0));
        _analyzer.AddLoose(b, new DateTime(2026, 11, 1, 0, 0, 0));

        var scan = Find(Sample(new DateTime(2026, 10, 31, 23, 58, 0), new DateTime(2026, 11, 1, 0, 2, 0)));

        Assert.Equal([a, b], Names(scan));
    }

    [Fact]
    public void Find_WindowEdges_AreInclusive()
    {
        var inLow = Name(0, At(13, 30, 0));
        var inHigh = Name(0, At(13, 41, 0));
        _analyzer.AddLoose(inLow, At(13, 0, 0));
        _analyzer.AddLoose(inHigh, At(13, 0, 0));
        _analyzer.AddLoose(Name(0, At(13, 29, 50)), At(13, 0, 0));
        _analyzer.AddLoose(Name(0, At(13, 41, 10)), At(13, 0, 0));

        var scan = Find(Sample(At(13, 30, 30), At(13, 40, 1)));

        Assert.Equal([inLow, inHigh], Names(scan));
    }

    [Fact]
    public void Find_TwoSpectrometers_SortedByTimestampThenSpectrometer()
    {
        var late0 = Name(0, At(13, 32, 0));
        var early1 = Name(1, At(13, 31, 0));
        var early0 = Name(0, At(13, 31, 0));
        foreach (var n in new[] { late0, early1, early0 })
        {
            _analyzer.AddLoose(n, At(13, 0, 0));
        }

        var scan = Find(Sample(At(13, 30, 0), At(13, 40, 0)));

        Assert.Equal([early0, early1, late0], Names(scan));
    }

    [Fact]
    public void Find_UnrelatedNames_AreIgnored()
    {
        var a = Name(0, At(13, 31, 0));
        _analyzer.AddLoose(a, At(13, 0, 0));
        _analyzer.AddLoose("notes.txt", At(13, 0, 0));
        _analyzer.AddLoose("Spectra_x_Belt_1_2026.11.06_13.31.00.csv", At(13, 0, 0));
        _analyzer.AddArchive(At(13, 0, 0), ["readme.txt", Name(1, At(13, 32, 0))]);

        var scan = Find(Sample(At(13, 30, 0), At(13, 40, 0)));

        Assert.Equal([a, Name(1, At(13, 32, 0))], Names(scan));
    }

    [Fact]
    public void Find_OverlappingSamples_CollectOneCopy()
    {
        var a = Name(0, At(13, 35, 0));
        _analyzer.AddLoose(a, At(13, 0, 0));

        var scan = Find(Sample(At(13, 30, 0), At(13, 40, 0)), Sample(At(13, 34, 0), At(13, 45, 0)));

        Assert.Equal([a], Names(scan));
    }

    [Fact]
    public void Find_FileInArchiveAndLoose_KeepsArchiveCopy()
    {
        var a = Name(0, At(13, 35, 0));
        _analyzer.AddArchive(At(13, 0, 0), [a]);
        _analyzer.AddLoose(a, At(13, 0, 0));

        var scan = Find(Sample(At(13, 30, 0), At(13, 40, 0)));

        var file = Assert.Single(scan.Files);
        Assert.NotNull(file.EntryName);
    }

    [Fact]
    public void Find_EntryInSubfolder_MatchedByFileName()
    {
        var a = Name(0, At(13, 35, 0));
        _analyzer.AddArchive(At(13, 0, 0), [a], subfolder: "Tech_Spectra");

        var scan = Find(Sample(At(13, 30, 0), At(13, 40, 0)));

        var file = Assert.Single(scan.Files);
        Assert.Equal(a, file.Name.FileName);
        Assert.Equal("Tech_Spectra/" + a, file.EntryName);
        Assert.Equal(a, ReadAll(file));
    }

    [Fact]
    public void Find_LooseFileHeldOpenByWriter_IsStillCollected()
    {
        var a = Name(0, At(13, 35, 0));
        var path = _analyzer.AddLoose(a, At(13, 0, 0));
        using var writer = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        var scan = Find(Sample(At(13, 30, 0), At(13, 40, 0)));

        var file = Assert.Single(scan.Files);
        Assert.Equal(a, ReadAll(file));
    }

    [Fact]
    public void Find_MissingFolders_ReportEmptyHoursAscending()
    {
        _analyzer.AddLoose(Name(0, At(14, 5, 0)), At(14, 0, 0));

        var scan = Find(Sample(At(13, 30, 0), At(15, 10, 0)));

        Assert.Equal([At(13, 0, 0), At(15, 0, 0)], scan.EmptyHours);
        Assert.Single(scan.Files);
    }

    [Fact]
    public void Find_HourWithOnlyOutOfWindowFiles_IsNotEmpty()
    {
        _analyzer.AddLoose(Name(0, At(13, 5, 0)), At(13, 0, 0));

        var scan = Find(Sample(At(13, 30, 0), At(13, 40, 0)));

        Assert.Empty(scan.Files);
        Assert.Empty(scan.EmptyHours);
    }

    [Fact]
    public void Find_UnreadableArchive_ThrowsArchiveUnreadable()
    {
        var path = _analyzer.AddArchive(At(13, 0, 0), []);
        File.WriteAllBytes(path, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);

        var e = Assert.Throws<ArchiveUnreadableException>(() => Find(Sample(At(13, 30, 0), At(13, 40, 0))));

        Assert.Contains(path, e.Message);
        Assert.NotNull(e.InnerException);
    }

    [Fact]
    public void Find_CancelledToken_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            SpectraSource.Find(_analyzer.Layout, [Sample(At(13, 30, 0), At(13, 40, 0))], cts.Token));
    }
}
