using System.IO.Compression;
using System.Text;
using SpectraCollector.Core;

namespace SpectraCollector.Core.Tests;

public sealed class CollectorTests : IDisposable
{
    private static readonly DateTime Hour = new(2026, 11, 17, 13, 0, 0);
    private readonly FakeAnalyzer _analyzer = new();
    private readonly DirectoryInfo _out = Directory.CreateTempSubdirectory("out-");
    private readonly DirectoryInfo _input = Directory.CreateTempSubdirectory("input-");

    public void Dispose()
    {
        _analyzer.Dispose();
        _out.Delete(true);
        _input.Delete(true);
    }

    private CollectRequest Request(params Sample[] samples) =>
        new(samples, "samples.csv", "csv"u8.ToArray(), _analyzer.Layout, _out.FullName);

    private CollectRequest RequestFromCsvFile(string fileName, byte[] csv, string culture)
    {
        var path = Path.Combine(_input.FullName, fileName);
        File.WriteAllBytes(path, csv);
        using var scope = new CultureScope(culture);
        var check = FormValidator.Check(
            new FormInput(false, path, "", "", _out.FullName, _analyzer.Layout.DataRoot, _analyzer.Layout.Belt));
        Assert.Empty(check.Problems);
        return check.Request!;
    }

    private static byte[] EntryBytes(string zipPath, string entryName)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        using var entry = zip.GetEntry(entryName)!.Open();
        using var copy = new MemoryStream();
        entry.CopyTo(copy);
        return copy.ToArray();
    }

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
        Assert.Equal(2, result.Report.FileCount);
        Assert.Equal(
            [
                (CollectPhase.Searching, 0, 1),
                (CollectPhase.Searching, 1, 1),
                (CollectPhase.Writing, 0, 2),
                (CollectPhase.Writing, 1, 2),
                (CollectPhase.Writing, 2, 2),
            ],
            progress.Select(p => (p.Phase, p.Done, p.Total)));
        using var zip = ZipFile.OpenRead(result.ZipPath);
        Assert.Equal(4, zip.Entries.Count);
    }

    private async Task<(CollectResult Result, List<CollectProgress> Events)> RunWithProgress(params Sample[] samples)
    {
        var events = new List<CollectProgress>();
        var result = await Collector.RunAsync(Request(samples), new SyncProgress(events.Add), CancellationToken.None);
        return (result, events);
    }

    [Fact]
    public async Task Writing_progress_starts_with_the_report_and_zero_counts()
    {
        _analyzer.AddLoose(FakeAnalyzer.SpectrumFileName(1, Hour.AddMinutes(31)), Hour);
        _analyzer.AddLoose(FakeAnalyzer.SpectrumFileName(1, Hour.AddMinutes(51)), Hour);

        var (result, events) = await RunWithProgress(
            new Sample("S1", Hour.AddMinutes(30), Hour.AddMinutes(40)),
            new Sample("S2", Hour.AddMinutes(50), Hour.AddMinutes(55)));

        var writing = events.Where(e => e.Phase == CollectPhase.Writing).ToList();
        Assert.Same(result.Report, writing[0].Report);
        Assert.Equal([0, 0], writing[0].WrittenPerSample);
        Assert.All(writing.Skip(1), e => Assert.Null(e.Report));
        Assert.All(events.Where(e => e.Phase == CollectPhase.Searching), e =>
        {
            Assert.Null(e.Report);
            Assert.Null(e.WrittenPerSample);
        });
    }

    [Fact]
    public async Task Writing_progress_counts_written_files_per_sample()
    {
        for (var i = 0; i < 3; i++)
        {
            _analyzer.AddLoose(FakeAnalyzer.SpectrumFileName(0, Hour.AddMinutes(31).AddSeconds(i * 10)), Hour);
            _analyzer.AddLoose(FakeAnalyzer.SpectrumFileName(1, Hour.AddMinutes(31).AddSeconds(i * 10)), Hour);
        }

        _analyzer.AddLoose(FakeAnalyzer.SpectrumFileName(0, Hour.AddMinutes(51)), Hour);

        var (result, events) = await RunWithProgress(
            new Sample("S1", Hour.AddMinutes(30), Hour.AddMinutes(40)),
            new Sample("S2", Hour.AddMinutes(50), Hour.AddMinutes(55)));

        var counts = events.Where(e => e.Phase == CollectPhase.Writing).Select(e => e.WrittenPerSample!).ToList();
        Assert.Equal(
            result.Report.Samples.Select(s => s.CountBySpectrometer.Values.Sum()), counts[^1]);
        for (var i = 1; i < counts.Count; i++)
        {
            Assert.All(Enumerable.Range(0, 2), k => Assert.True(counts[i][k] >= counts[i - 1][k]));
        }
    }

    [Fact]
    public async Task Overlapping_samples_count_a_shared_file_for_both()
    {
        _analyzer.AddLoose(FakeAnalyzer.SpectrumFileName(0, Hour.AddMinutes(31)), Hour);
        _analyzer.AddLoose(FakeAnalyzer.SpectrumFileName(0, Hour.AddMinutes(32)), Hour);

        var (result, events) = await RunWithProgress(
            new Sample("S1", Hour.AddMinutes(30), Hour.AddMinutes(32)),
            new Sample("S2", Hour.AddMinutes(31), Hour.AddMinutes(33)));

        var last = events.Last(e => e.Phase == CollectPhase.Writing);
        Assert.Equal(2, last.Total);
        Assert.Equal([2, 2], last.WrittenPerSample);
        using var zip = ZipFile.OpenRead(result.ZipPath);
        Assert.Equal(2, zip.Entries.Count(e => e.FullName.Contains("/Tech_Spectra/")));
    }

    [Fact]
    public async Task Ansi_csv_file_reaches_the_zip_byte_for_byte()
    {
        var day = new DateTime(2026, 11, 6, 13, 0, 0);
        _analyzer.AddLoose(FakeAnalyzer.SpectrumFileName(0, day.AddMinutes(6)), day);
        var request = RequestFromCsvFile("times.csv", FormValidatorTests.Cp1257Csv, "lt-LT");

        var result = await Collector.RunAsync(request, null, CancellationToken.None);

        Assert.Equal("Šlakas", result.Report.Samples[0].Sample.Name);
        Assert.Equal(FormValidatorTests.Cp1257Csv, EntryBytes(result.ZipPath, "spectra_2026-11-06_1305/times.csv"));
    }

    [Fact]
    public async Task Utf8_csv_file_keeps_its_bom_in_the_zip()
    {
        byte[] csv = [.. Encoding.UTF8.Preamble, .. "Sample;Begin;End\r\nS01;2026-11-17 13:30;2026-11-17 13:40\r\n"u8];
        _analyzer.AddLoose(FakeAnalyzer.SpectrumFileName(0, Hour.AddMinutes(31)), Hour);
        var request = RequestFromCsvFile("times.csv", csv, "en-US");

        var result = await Collector.RunAsync(request, null, CancellationToken.None);

        Assert.Equal(csv, EntryBytes(result.ZipPath, "spectra_2026-11-17_1330/times.csv"));
    }

    [Fact]
    public async Task Nothing_found_throws_naming_the_folder_and_creates_no_zip()
    {
        var e = await Assert.ThrowsAsync<NothingFoundException>(() => Collector.RunAsync(
            Request(new Sample("S1", Hour.AddMinutes(30), Hour.AddMinutes(40))), null, CancellationToken.None));

        var folder = Path.Combine(_analyzer.Layout.DataRoot, "2026_11", "Main", "Belt_1");
        Assert.Equal(
            $"No spectra found for these times in {folder}. Check the times, the data folder and the belt.", e.Message);
        Assert.Empty(Directory.GetFileSystemEntries(_out.FullName));
    }

    [Fact]
    public async Task Cancelled_before_start_throws_and_writes_nothing()
    {
        _analyzer.AddLoose(FakeAnalyzer.SpectrumFileName(1, Hour.AddMinutes(31)), Hour);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Collector.RunAsync(
            Request(new Sample("S1", Hour.AddMinutes(30), Hour.AddMinutes(40))), null, cts.Token));

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
