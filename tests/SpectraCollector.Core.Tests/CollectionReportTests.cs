using SpectraCollector.Core;

namespace SpectraCollector.Core.Tests;

public sealed class CollectionReportTests
{
    private static readonly DateTime Day = new(2026, 11, 6);

    private static DateTime At(int h, int m, int s = 0) => Day.AddHours(h).AddMinutes(m).AddSeconds(s);

    private static FoundSpectrum Spectrum(int spectrometer, DateTime t, string tag = "x") =>
        new(new SpectrumName($"Spectra_{spectrometer}_{tag}_{t:yyyy.MM.dd_HH.mm.ss}.txt", spectrometer, t), "src", null);

    private static List<FoundSpectrum> Series(int spectrometer, DateTime start, int count, int stepSeconds) =>
        Enumerable.Range(0, count).Select(i => Spectrum(spectrometer, start.AddSeconds(i * stepSeconds))).ToList();

    private static SourceScan Scan(IEnumerable<FoundSpectrum> files, params DateTime[] emptyHours) =>
        new(files.OrderBy(f => f.Name.Timestamp).ThenBy(f => f.Name.Spectrometer).ToList(), emptyHours);

    private static Sample S(string name, DateTime begin, DateTime end) => new(name, begin, end);

    [Fact]
    public void Counts_files_per_sample_and_spectrometer()
    {
        var files = Series(0, At(13, 5), 3, 10).Concat(Series(1, At(13, 5), 3, 10)).Concat(Series(0, At(13, 20), 2, 10));
        var report = CollectionReport.Build(
            [S("S01", At(13, 5), At(13, 6)), S("S02", At(13, 20), At(13, 21))], Scan(files));

        Assert.Equal(8, report.TotalFiles);
        Assert.Equal(new Dictionary<int, int> { [0] = 3, [1] = 3 }, report.Samples[0].CountBySpectrometer);
        Assert.Equal(new Dictionary<int, int> { [0] = 2, [1] = 0 }, report.Samples[1].CountBySpectrometer);
    }

    [Fact]
    public void Warns_when_a_sample_has_no_files()
    {
        var report = CollectionReport.Build(
            [S("S01", At(13, 5), At(13, 12)), S("S02", At(13, 20), At(13, 21))],
            Scan(Series(0, At(13, 20), 3, 10)));

        var warning = Assert.Single(report.Warnings, w => w.Kind == WarningKind.NoFiles);
        Assert.Equal("S01", warning.Sample);
        Assert.Equal("S01: no spectra found in 2026-11-06 13:05:00–13:12:00.", warning.Message);
        Assert.DoesNotContain(report.Warnings, w => w.Sample == "S01" && w.Kind != WarningKind.NoFiles);
    }

    [Fact]
    public void Warns_when_spectrometers_have_different_counts()
    {
        var files = Series(0, At(13, 20), 43, 10).Concat(Series(1, At(13, 20), 41, 10));
        var report = CollectionReport.Build([S("S02", At(13, 20), At(13, 27))], Scan(files));

        var warning = Assert.Single(report.Warnings);
        Assert.Equal(WarningKind.UnevenSpectrometers, warning.Kind);
        Assert.Equal("S02: spectrometers have different file counts (Spectra_0: 43, Spectra_1: 41).", warning.Message);
    }

    [Fact]
    public void Warns_about_a_gap_longer_than_three_times_the_median_interval()
    {
        var files = Series(0, At(13, 22), 2, 10).Concat(Series(0, At(13, 22, 50), 3, 10));
        var report = CollectionReport.Build([S("S02", At(13, 22), At(13, 23, 10))], Scan(files));

        var warning = Assert.Single(report.Warnings);
        Assert.Equal(WarningKind.Gap, warning.Kind);
        Assert.Equal("S02: Spectra_0 has a 40 s gap after 13:22:10.", warning.Message);
    }

    [Fact]
    public void Does_not_warn_about_a_twenty_second_hole_in_ten_second_cadence()
    {
        var files = Series(0, At(13, 22), 2, 10).Concat(Series(0, At(13, 22, 30), 3, 10));
        var report = CollectionReport.Build([S("S02", At(13, 22), At(13, 22, 50))], Scan(files));

        Assert.Empty(report.Warnings);
    }

    [Fact]
    public void Warns_when_a_spectrometer_starts_late()
    {
        var report = CollectionReport.Build(
            [S("S01", At(13, 5), At(13, 12))], Scan(Series(0, At(13, 6, 10), 36, 10)));

        var warning = Assert.Single(report.Warnings);
        Assert.Equal(WarningKind.LateStart, warning.Kind);
        Assert.Equal("S01", warning.Sample);
        Assert.Equal("S01: Spectra_0 starts at 13:06:10, 70 s after Begin.", warning.Message);
    }

    [Fact]
    public void Warns_when_a_spectrometer_ends_early()
    {
        var report = CollectionReport.Build(
            [S("S01", At(13, 5), At(13, 12))], Scan(Series(0, At(13, 5), 19, 10)));

        var warning = Assert.Single(report.Warnings);
        Assert.Equal(WarningKind.EarlyEnd, warning.Kind);
        Assert.Equal("S01: Spectra_0 ends at 13:08:00, 4 min before End.", warning.Message);
    }

    [Fact]
    public void Allows_a_minute_at_either_end_even_with_a_fast_cadence()
    {
        var report = CollectionReport.Build(
            [S("S01", At(13, 5), At(13, 12))], Scan(Series(0, At(13, 6), 31, 10)));

        Assert.Empty(report.Warnings);
    }

    [Fact]
    public void Allows_three_intervals_at_either_end_with_a_slow_cadence()
    {
        var report = CollectionReport.Build(
            [S("S01", At(13, 0), At(13, 20))], Scan(Series(0, At(13, 1, 30), 35, 30)));

        Assert.Empty(report.Warnings);
    }

    [Fact]
    public void Measures_late_start_and_early_end_from_the_unrounded_times()
    {
        var report = CollectionReport.Build(
            [S("S01", At(13, 5, 50), At(13, 12, 10))], Scan(Series(0, At(13, 6, 30), 31, 10)));

        Assert.Empty(report.Warnings);
    }

    [Fact]
    public void Uses_the_scan_wide_interval_when_a_sample_has_too_few_files()
    {
        var files = Series(0, At(13, 0), 31, 10).Concat(Series(0, At(13, 21, 10), 2, 10));
        var report = CollectionReport.Build(
            [S("S01", At(13, 0), At(13, 5)), S("S02", At(13, 20), At(13, 21, 20))], Scan(files));

        var warning = Assert.Single(report.Warnings);
        Assert.Equal("S02: Spectra_0 starts at 13:21:10, 70 s after Begin.", warning.Message);
    }

    [Fact]
    public void Uses_a_sixty_second_interval_when_none_is_known()
    {
        var files = Series(0, At(13, 2, 50), 2, 20).Concat(Series(1, At(13, 3, 11), 2, 9));
        var report = CollectionReport.Build([S("S01", At(13, 0), At(13, 3, 10))], Scan(files));

        var warning = Assert.Single(report.Warnings);
        Assert.Equal("S01: Spectra_1 starts at 13:03:11, 3 min after Begin.", warning.Message);
    }

    [Fact]
    public void A_zero_median_interval_does_not_make_every_interval_a_gap()
    {
        var files = new[]
        {
            Spectrum(0, At(13, 5), "a"), Spectrum(0, At(13, 5), "b"), Spectrum(0, At(13, 5), "c"), Spectrum(0, At(13, 5, 10)),
        };
        var report = CollectionReport.Build([S("S01", At(13, 5), At(13, 5, 10))], Scan(files));

        Assert.Empty(report.Warnings);
    }

    [Fact]
    public void Orders_warnings_inside_a_sample_by_kind()
    {
        var files = Series(0, At(13, 0), 13, 10)
            .Concat(Series(0, At(13, 3), 31, 10))
            .Concat(Series(1, At(13, 1, 10), 54, 10));
        var report = CollectionReport.Build([S("S01", At(13, 0), At(13, 10))], Scan(files));

        Assert.Equal(
            [
                "S01: spectrometers have different file counts (Spectra_0: 44, Spectra_1: 54).",
                "S01: Spectra_1 starts at 13:01:10, 70 s after Begin.",
                "S01: Spectra_0 ends at 13:08:00, 2 min before End.",
                "S01: Spectra_0 has a 60 s gap after 13:02:00.",
            ],
            report.Warnings.Select(w => w.Message));
        Assert.Equal(
            [WarningKind.UnevenSpectrometers, WarningKind.LateStart, WarningKind.EarlyEnd, WarningKind.Gap],
            report.Warnings.Select(w => w.Kind));
    }

    [Fact]
    public void Warns_once_per_empty_hour_without_a_sample()
    {
        var report = CollectionReport.Build(
            [S("S01", At(13, 5), At(13, 12))],
            Scan(Series(0, At(13, 5), 3, 10), At(13, 0), At(14, 0)));

        var empty = report.Warnings.Where(w => w.Kind == WarningKind.EmptyHour).ToList();
        Assert.Equal(2, empty.Count);
        Assert.All(empty, w => Assert.Null(w.Sample));
        Assert.Equal(
            "No spectra at all for 2026-11-06 13:00–14:00 (no hour archive and no files in Tech_Spectra).",
            empty[0].Message);
    }

    [Fact]
    public void Text_lists_windows_counts_and_every_warning_with_crlf()
    {
        var files = Series(0, At(13, 20), 49, 10).Concat(Series(1, At(13, 20), 47, 10));
        var report = CollectionReport.Build(
            [S("S01", At(13, 5), At(13, 12)), S("S02", At(13, 20), At(13, 28))], Scan(files));

        var expected = string.Join(
            "\r\n",
            "SpectraCollector report",
            "Samples: 2, files: 96",
            "",
            "S01  2026-11-06 13:05:00–13:12:00  Spectra_0: 0  Spectra_1: 0",
            "S02  2026-11-06 13:20:00–13:28:00  Spectra_0: 49  Spectra_1: 47",
            "",
            "Warnings:",
            "- S01: no spectra found in 2026-11-06 13:05:00–13:12:00.",
            "- S02: spectrometers have different file counts (Spectra_0: 49, Spectra_1: 47).",
            "");
        Assert.Equal(expected, report.ToText());
    }

    [Fact]
    public void Text_says_none_without_warnings_and_pads_names()
    {
        var report = CollectionReport.Build(
            [S("A", At(13, 5), At(13, 6)), S("Long", At(13, 5), At(13, 6))],
            Scan(Series(0, At(13, 5), 3, 10)));

        Assert.Contains("A     2026-11-06 13:05:00–13:06:00  Spectra_0: 3", report.ToText());
        Assert.EndsWith("Warnings: none\r\n", report.ToText());
    }

    [Fact]
    public void Text_shows_both_dates_when_the_window_crosses_midnight()
    {
        var report = CollectionReport.Build(
            [S("S01", At(23, 50), At(24, 10))], Scan(Series(0, At(23, 50), 3, 10)));

        Assert.Contains("2026-11-06 23:50:00–2026-11-07 00:10:00", report.ToText());
    }
}
