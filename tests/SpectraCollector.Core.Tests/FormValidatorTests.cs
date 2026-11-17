using System.Text;
using SpectraCollector.Core;

namespace SpectraCollector.Core.Tests;

public sealed class FormValidatorTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory();

    public void Dispose() => _dir.Delete(true);

    private string Folder(string name) => Directory.CreateDirectory(Path.Combine(_dir.FullName, name)).FullName;

    private FormInput Single(string begin = "2026-11-06 13:30:30", string end = "2026-11-06 13:40:01") =>
        new(true, null, begin, end, Folder("out"), Folder("data"), @"Main\Belt_1");

    private FormInput Csv(string? path) =>
        new(false, path, "", "", Folder("out"), Folder("data"), @"Main\Belt_1");

    private string WriteCsv(string name, string text)
    {
        var path = Path.Combine(_dir.FullName, name);
        File.WriteAllText(path, text);
        return path;
    }

    private string WriteCsvBytes(string name, byte[] bytes)
    {
        var path = Path.Combine(_dir.FullName, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>One sample named Šlakas, saved by Excel in Windows-1257 (Š is 0xD0).</summary>
    internal static readonly byte[] Cp1257Csv =
        [.. "Sample;Begin;End\r\n"u8, 0xD0, .. "lakas;2026-11-06 13:05:00;2026-11-06 13:12:00\r\n"u8];

    private const string ThreeSamples =
        "Sample;Exposition;Begin;End;Material\n" +
        "A;;2026-11-06 13:05:00;2026-11-06 13:12:00;\n" +
        "B;;2026-11-06 14:00:00;2026-11-06 14:10:00;\n" +
        "C;;2026-11-06 15:01:00;2026-11-06 15:20:30;\n";

    [Fact]
    public void Single_Valid_BuildsOneSampleRequest()
    {
        var input = Single();

        var check = FormValidator.Check(input);

        Assert.Empty(check.Problems);
        var request = check.Request!;
        var sample = Assert.Single(request.Samples);
        Assert.Equal("Sample_1", sample.Name);
        Assert.Equal("samples.csv", request.CsvName);
        Assert.Equal(new UTF8Encoding(false).GetBytes(SampleTable.ToCsv(request.Samples)), request.CsvBytes);
        Assert.Equal(input.OutputDir, request.OutputDir);
        Assert.Equal(new DataLayout(input.DataRoot, @"Main\Belt_1"), request.Layout);
        Assert.Equal("Files from 13:30:00 to 13:41:00", check.WindowHint);
    }

    [Fact]
    public void Single_AcrossMidnight_HintShowsDates()
    {
        var check = FormValidator.Check(Single("2026-11-06 23:58:00", "2026-11-07 00:03:00"));

        Assert.Equal("Files from 2026-11-06 23:58:00 to 2026-11-07 00:03:00", check.WindowHint);
    }

    [Fact]
    public void Single_EndBeforeBegin_IsProblem()
    {
        var check = FormValidator.Check(Single("2026-11-06 13:40:00", "2026-11-06 13:30:00"));

        Assert.Equal(["End is earlier than Begin."], check.Problems);
        Assert.Null(check.Request);
        Assert.Null(check.WindowHint);
    }

    [Fact]
    public void Single_BadBegin_IsProblem()
    {
        var check = FormValidator.Check(Single(begin: "yesterday"));

        Assert.Equal(["Begin is not a time. Use 2026-11-06 13:30:00."], check.Problems);
        Assert.Null(check.Request);
        Assert.Null(check.WindowHint);
    }

    [Fact]
    public void Single_BadEnd_IsProblem()
    {
        var check = FormValidator.Check(Single(end: "soon"));

        Assert.Equal(["End is not a time. Use 2026-11-06 13:40:00."], check.Problems);
    }

    [Fact]
    public void Single_EmptyOrBlankFields_AskForBoth()
    {
        var check = FormValidator.Check(Single(begin: "", end: "   "));

        Assert.Equal(["Enter Begin.", "Enter End."], check.Problems);
    }

    [Fact]
    public void Csv_Valid_UsesFileNameAndBytes()
    {
        var path = WriteCsv("times.csv", ThreeSamples);

        var check = FormValidator.Check(Csv(path));

        Assert.Empty(check.Problems);
        var request = check.Request!;
        Assert.Equal(3, request.Samples.Count);
        Assert.Equal("times.csv", request.CsvName);
        Assert.Equal(File.ReadAllBytes(path), request.CsvBytes);
        Assert.Equal("3 samples, files from 13:05:00 to 15:21:00", check.WindowHint);
    }

    [Fact]
    public void Csv_AnsiFile_IsReadInTheCultureCodePageAndKeptAsBytes()
    {
        var path = WriteCsvBytes("ansi.csv", Cp1257Csv);

        FormCheck check;
        using (new CultureScope("lt-LT"))
        {
            check = FormValidator.Check(Csv(path));
        }

        Assert.Empty(check.Problems);
        Assert.Equal("Šlakas", Assert.Single(check.Request!.Samples).Name);
        Assert.Equal(Cp1257Csv, check.Request.CsvBytes);
    }

    [Fact]
    public void Csv_Utf8WithoutBom_IsReadAsUtf8()
    {
        var path = WriteCsvBytes("utf8.csv", "Sample;Begin;End\nŠlakas;2026-11-06 13:05;2026-11-06 13:12\n"u8.ToArray());

        FormCheck check;
        using (new CultureScope("lt-LT"))
        {
            check = FormValidator.Check(Csv(path));
        }

        Assert.Equal("Šlakas", Assert.Single(check.Request!.Samples).Name);
    }

    [Fact]
    public void Csv_Utf16WithBom_IsReadInThatEncoding()
    {
        var text = "Sample;Begin;End\nŠlakas;2026-11-06 13:05;2026-11-06 13:12\n";
        var path = WriteCsvBytes("utf16.csv", [.. Encoding.Unicode.Preamble, .. Encoding.Unicode.GetBytes(text)]);

        var check = FormValidator.Check(Csv(path));

        Assert.Equal("Šlakas", Assert.Single(check.Request!.Samples).Name);
        Assert.Equal(File.ReadAllBytes(path), check.Request.CsvBytes);
    }

    [Fact]
    public void Csv_OneSample_HintIsSingular()
    {
        var path = WriteCsv("one.csv", "Sample;Begin;End\nA;2026-11-06 13:05:00;2026-11-06 13:12:00\n");

        var check = FormValidator.Check(Csv(path));

        Assert.Equal("1 sample, files from 13:05:00 to 13:12:00", check.WindowHint);
    }

    [Fact]
    public void Csv_AcrossDays_HintShowsDates()
    {
        var path = WriteCsv("days.csv",
            "Sample;Begin;End\nA;2026-11-06 23:58:00;2026-11-06 23:59:00\nB;2026-11-07 00:01:00;2026-11-07 00:02:00\n");

        var check = FormValidator.Check(Csv(path));

        Assert.Equal("2 samples, files from 2026-11-06 23:58:00 to 2026-11-07 00:02:00", check.WindowHint);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Csv_NoPath_IsProblem(string? path)
    {
        var check = FormValidator.Check(Csv(path));

        Assert.Equal(["Choose a CSV file with sample times."], check.Problems);
        Assert.Null(check.Request);
    }

    [Fact]
    public void Csv_MissingFile_IsProblem()
    {
        var check = FormValidator.Check(Csv(Path.Combine(_dir.FullName, "nope.csv")));

        Assert.Equal(["CSV file not found."], check.Problems);
    }

    [Fact]
    public void Csv_RelativePath_IsProblem()
    {
        var check = FormValidator.Check(Csv("times.csv"));

        Assert.Equal(["Use a full path for the CSV file."], check.Problems);
        Assert.Null(check.Request);
    }

    [Fact]
    public void Csv_ParseError_ReportsLineMessage()
    {
        var path = WriteCsv("bad.csv", "Sample;Begin;End\nA;2026-11-06 13:20:00;2026-11-06 13:10:00\n");

        var check = FormValidator.Check(Csv(path));

        Assert.Equal(["Line 2: End is earlier than Begin."], check.Problems);
        Assert.Null(check.Request);
        Assert.Null(check.WindowHint);
    }

    [Fact]
    public void Csv_ReadsFileHeldOpenByAnotherProgram()
    {
        var path = WriteCsv("held.csv", ThreeSamples);
        using var holder = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        var check = FormValidator.Check(Csv(path));

        Assert.Empty(check.Problems);
    }

    [Fact]
    public void Csv_FileLockedExclusively_IsProblemWithSingleTrailingPeriod()
    {
        var path = WriteCsv("locked.csv", ThreeSamples);
        using var holder = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var check = FormValidator.Check(Csv(path));

        var problem = Assert.Single(check.Problems);
        Assert.StartsWith("Cannot read the CSV file: ", problem);
        Assert.EndsWith(".", problem);
        Assert.False(problem.EndsWith("..", StringComparison.Ordinal));
        Assert.Null(check.Request);
    }

    [Fact]
    public void Output_Empty_IsProblem_AndHintStillShown()
    {
        var check = FormValidator.Check(Single() with { OutputDir = "" });

        Assert.Equal(["Choose where to save the zip."], check.Problems);
        Assert.Null(check.Request);
        Assert.Equal("Files from 13:30:00 to 13:41:00", check.WindowHint);
    }

    [Fact]
    public void Output_InvalidPath_IsProblemWithoutThrowing()
    {
        var check = FormValidator.Check(Single() with { OutputDir = "out\0dir" });

        Assert.Equal(["The output folder is not a valid path."], check.Problems);
        Assert.Null(check.Request);
    }

    [Fact]
    public void Output_RelativePath_IsProblem()
    {
        var check = FormValidator.Check(Single() with { OutputDir = "out" });

        Assert.Equal([@"Use a full folder path for the output, for example D:\Out."], check.Problems);
        Assert.Null(check.Request);
    }

    [Fact]
    public void Data_RelativeRoot_IsProblem()
    {
        var check = FormValidator.Check(Single() with { DataRoot = "data" });

        Assert.Equal([@"Use a full folder path for the LyncisAC data, for example D:\LyncisAC_DATA."], check.Problems);
        Assert.Null(check.Request);
    }

    [Fact]
    public void Data_InvalidRoot_IsProblemWithoutThrowing()
    {
        var check = FormValidator.Check(Single() with { DataRoot = "data\0root" });

        Assert.Equal(["The LyncisAC data folder is not a valid path."], check.Problems);
        Assert.Null(check.Request);
    }

    [Fact]
    public void Output_InsideDataFolder_IsProblem()
    {
        var input = Single();
        var inside = Path.Combine(input.DataRoot, "exports");

        var check = FormValidator.Check(input with { OutputDir = inside });

        Assert.Equal(["Choose an output folder outside the LyncisAC data folder."], check.Problems);
    }

    [Fact]
    public void Output_IsDataRoot_IsProblem()
    {
        var input = Single();

        var check = FormValidator.Check(input with { OutputDir = input.DataRoot });

        Assert.Equal(["Choose an output folder outside the LyncisAC data folder."], check.Problems);
    }

    [Fact]
    public void Data_EmptyRoot_IsProblem()
    {
        var check = FormValidator.Check(Single() with { DataRoot = "" });

        Assert.Equal(["Choose the LyncisAC data folder."], check.Problems);
    }

    [Fact]
    public void Data_MissingRoot_IsProblem()
    {
        var check = FormValidator.Check(Single() with { DataRoot = Path.Combine(_dir.FullName, "gone") });

        Assert.Equal(["LyncisAC data folder not found."], check.Problems);
    }

    [Fact]
    public void Data_EmptyBelt_IsProblem()
    {
        var check = FormValidator.Check(Single() with { Belt = " " });

        Assert.Equal([@"Enter the belt folder, for example Main\Belt_1."], check.Problems);
    }

    [Fact]
    public void Problems_AreOrderedSamplesOutputData()
    {
        var input = new FormInput(true, null, "", "", "", "", "");

        var check = FormValidator.Check(input);

        Assert.Equal(
            [
                "Enter Begin.",
                "Enter End.",
                "Choose where to save the zip.",
                "Choose the LyncisAC data folder.",
                @"Enter the belt folder, for example Main\Belt_1.",
            ],
            check.Problems);
    }
}
