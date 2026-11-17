using System.IO.Compression;
using SpectraCollector.Core;

namespace SpectraCollector.Core.Tests;

public sealed class CollectorTests : IDisposable
{
    private static readonly DateTime Hour = new(2026, 11, 17, 13, 0, 0);
    private readonly FakeAnalyzer _analyzer = new();
    private readonly DirectoryInfo _out = Directory.CreateTempSubdirectory("out-");

    public void Dispose()
    {
        _analyzer.Dispose();
        _out.Delete(true);
    }

    private CollectRequest Request(params Sample[] samples) =>
        new(samples, "samples.csv", "csv", _analyzer.Layout, _out.FullName);

    [Fact]
    public async Task Collects_spectra_and_reports_progress_up_to_total()
    {
        _analyzer.AddLoose(FakeAnalyzer.SpectrumFileName(1, Hour.AddMinutes(31)), Hour);
        _analyzer.AddArchive(Hour, [FakeAnalyzer.SpectrumFileName(2, Hour.AddMinutes(32))]);
        var progress = new List<CollectProgress>();

        var result = await Collector.RunAsync(
            Request(new Sample("S1", Hour.AddMinutes(30), Hour.AddMinutes(40))),
            new SyncProgress(progress.Add),
            CancellationToken.None);

        Assert.Equal("spectra_2026-11-17_1330.zip", Path.GetFileName(result.ZipPath));
        Assert.Equal(2, result.Report.TotalFiles);
        Assert.Equal(
            [
                new CollectProgress(CollectPhase.Searching, 0, 1),
                new CollectProgress(CollectPhase.Searching, 1, 1),
                new CollectProgress(CollectPhase.Writing, 0, 2),
                new CollectProgress(CollectPhase.Writing, 1, 2),
                new CollectProgress(CollectPhase.Writing, 2, 2),
            ],
            progress);
        using var zip = ZipFile.OpenRead(result.ZipPath);
        Assert.Equal(4, zip.Entries.Count);
    }

    [Fact]
    public async Task Nothing_found_throws_and_creates_no_zip()
    {
        await Assert.ThrowsAsync<NothingFoundException>(() => Collector.RunAsync(
            Request(new Sample("S1", Hour.AddMinutes(30), Hour.AddMinutes(40))), null, CancellationToken.None));

        Assert.Empty(Directory.GetFileSystemEntries(_out.FullName));
    }

    [Fact]
    public async Task Output_inside_the_data_root_is_rejected_before_anything_is_written()
    {
        var root = _analyzer.Layout.DataRoot;
        foreach (var dir in new[] { root, Path.Combine(root, "out") })
        {
            var request = Request(new Sample("S1", Hour.AddMinutes(30), Hour.AddMinutes(40))) with { OutputDir = dir };

            var e = await Assert.ThrowsAsync<OutputInsideDataException>(
                () => Collector.RunAsync(request, null, CancellationToken.None));

            Assert.Equal("Choose an output folder outside the LyncisAC data folder.", e.Message);
            Assert.Empty(Directory.GetFileSystemEntries(root));
        }
    }
}

internal sealed class SyncProgress(Action<CollectProgress> report) : IProgress<CollectProgress>
{
    public void Report(CollectProgress value) => report(value);
}
