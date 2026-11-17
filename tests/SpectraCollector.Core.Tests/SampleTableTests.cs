using SpectraCollector.Core;

namespace SpectraCollector.Core.Tests;

public class SampleTableTests
{
    private static IReadOnlyList<Sample> Parse(string text) => SampleTable.Parse(new StringReader(text));

    private static string MessageOf(string text) =>
        Assert.Throws<SampleTimesException>(() => Parse(text)).Message;

    [Fact]
    public void Parse_ReadsSemicolonTable()
    {
        var samples = Parse("Sample;Exposition;Begin;End;Material\nS01;;2026-11-06 13:05:00;2026-11-06 13:12:00;\n");

        var sample = Assert.Single(samples);
        Assert.Equal("S01", sample.Name);
        Assert.Equal(new DateTime(2026, 11, 6, 13, 5, 0), sample.Begin);
        Assert.Equal(new DateTime(2026, 11, 6, 13, 12, 0), sample.End);
    }

    [Fact]
    public void Parse_MatchesHeaderNamesInAnyCaseAndOrder()
    {
        var samples = Parse("end;BEGIN;sample\n2026-11-06 13:12;2026-11-06 13:05;S01\n");

        var sample = Assert.Single(samples);
        Assert.Equal("S01", sample.Name);
        Assert.Equal(new DateTime(2026, 11, 6, 13, 5, 0), sample.Begin);
        Assert.Equal(new DateTime(2026, 11, 6, 13, 12, 0), sample.End);
    }

    [Theory]
    [InlineData(";")]
    [InlineData(",")]
    [InlineData("\t")]
    public void Parse_DetectsDelimiterFromHeader(string d)
    {
        var text = $"Sample{d}Begin{d}End\nS01{d}2026-11-06 13:05{d}2026-11-06 13:12\n";

        var sample = Assert.Single(Parse(text));
        Assert.Equal("S01", sample.Name);
        Assert.Equal(new DateTime(2026, 11, 6, 13, 12, 0), sample.End);
    }

    [Fact]
    public void Parse_HandlesQuotedFieldContainingDelimiterAndEscapedQuote()
    {
        var samples = Parse("Sample,Begin,End\n\"A,\"\"x\"\"\",2026-11-06 13:05,2026-11-06 13:12\n");

        Assert.Equal("A,\"x\"", Assert.Single(samples).Name);
    }

    [Fact]
    public void Parse_AcceptsAllTimeFormats()
    {
        var samples = Parse(
            "Sample;Begin;End\nA;06.11.2026 13:05:30;06/11/2026 13:12\nB;2026-11-06 14:00;2026-11-06 14:10:05\n");

        Assert.Equal(2, samples.Count);
        Assert.Equal(new DateTime(2026, 11, 6, 13, 5, 30), samples[0].Begin);
        Assert.Equal(new DateTime(2026, 11, 6, 13, 12, 0), samples[0].End);
        Assert.Equal(new DateTime(2026, 11, 6, 14, 10, 5), samples[1].End);
    }

    [Fact]
    public void Parse_SkipsBomAndBlankLinesAndAcceptsCrlf()
    {
        var text = "﻿Sample;Begin;End\r\nA;2026-11-06 13:05;2026-11-06 13:12\r\n\r\n;;\r\nB;2026-11-06 14:00;2026-11-06 14:10\r\n\r\n\r\n";

        var samples = Parse(text);

        Assert.Equal(["A", "B"], samples.Select(s => s.Name));
    }

    [Fact]
    public void Parse_ReadsBomThroughStreamReader()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }
            .Concat("Sample;Begin;End\nA;2026-11-06 13:05;2026-11-06 13:12\n"u8.ToArray()).ToArray();
        using var reader = new StreamReader(new MemoryStream(bytes), detectEncodingFromByteOrderMarks: true);

        Assert.Equal("A", Assert.Single(SampleTable.Parse(reader)).Name);
    }

    [Fact]
    public void Parse_AllowsEndEqualToBegin()
    {
        var sample = Assert.Single(Parse("Sample;Begin;End\nA;2026-11-06 13:05;2026-11-06 13:05\n"));

        Assert.Equal(sample.Begin, sample.End);
    }

    [Fact]
    public void Window_RoundsBeginAndEnd()
    {
        var sample = new Sample("A", new DateTime(2026, 11, 6, 13, 30, 30), new DateTime(2026, 11, 6, 13, 40, 1));

        Assert.Equal(new TimeWindow(new DateTime(2026, 11, 6, 13, 30, 0), new DateTime(2026, 11, 6, 13, 41, 0)), sample.Window);
    }

    [Fact]
    public void Parse_ReportsMissingBeginColumnOnLineOne()
    {
        Assert.Equal("Line 1: Column 'Begin' is missing.", MessageOf("Sample;End\nA;2026-11-06 13:05\n"));
    }

    [Fact]
    public void Parse_ReportsBadTimeWithLineNumber()
    {
        var text = "Sample;Begin;End\nA;2026-11-06 13:05;2026-11-06 13:12\nB;nonsense;2026-11-06 13:12\n";

        Assert.Equal("Line 3: Begin is not a valid time. Use 2026-11-06 13:30:00.", MessageOf(text));
    }

    [Fact]
    public void Parse_ReportsBadEndTime()
    {
        Assert.Equal(
            "Line 2: End is not a valid time. Use 2026-11-06 13:40:00.", MessageOf("Sample;Begin;End\nA;2026-11-06 13:05;\n"));
    }

    [Fact]
    public void Parse_ReportsEndBeforeBegin()
    {
        var text = "Sample;Begin;End\nA;2026-11-06 13:05;2026-11-06 13:12\n\nB;2026-11-06 14:00;2026-11-06 13:59\n";

        Assert.Equal("Line 4: End is earlier than Begin.", MessageOf(text));
    }

    [Fact]
    public void Parse_ReportsEmptyName()
    {
        Assert.Equal("Line 2: Sample name is empty.", MessageOf("Sample;Begin;End\n ;2026-11-06 13:05;2026-11-06 13:12\n"));
    }

    [Fact]
    public void Parse_ReportsNoDataRows()
    {
        Assert.Equal("Line 1: The file has no sample rows.", MessageOf("Sample;Begin;End\n\n"));
    }

    [Fact]
    public void Parse_ReportsEmptyFile()
    {
        Assert.StartsWith("Line 1: ", MessageOf(""));
    }

    [Fact]
    public void Parse_ReportsUnterminatedQuote()
    {
        Assert.Equal("Line 2: A quoted field is not closed.", MessageOf("Sample;Begin;End\n\"A;2026-11-06 13:05;2026-11-06 13:12\n"));
    }

    [Fact]
    public void ToCsv_WritesHeaderAndCrlfLines()
    {
        var samples = new[] { new Sample("S01", new DateTime(2026, 11, 6, 13, 5, 0), new DateTime(2026, 11, 6, 13, 12, 30)) };

        Assert.Equal(
            "Sample;Exposition;Begin;End;Material\r\nS01;;2026-11-06 13:05:00;2026-11-06 13:12:30;\r\n",
            SampleTable.ToCsv(samples));
    }

    [Fact]
    public void ToCsv_RoundTripsThroughParseIncludingAwkwardNames()
    {
        var samples = new[]
        {
            new Sample("S01", new DateTime(2026, 11, 6, 13, 5, 0), new DateTime(2026, 11, 6, 13, 12, 30)),
            new Sample("a;b \"q\"", new DateTime(2026, 11, 6, 14, 0, 0), new DateTime(2026, 11, 6, 14, 0, 0)),
        };

        var csv = SampleTable.ToCsv(samples);

        Assert.Contains("\"a;b \"\"q\"\"\"", csv);
        Assert.Equal(samples, Parse(csv));
    }
}
