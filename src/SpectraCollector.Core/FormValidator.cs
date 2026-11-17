using System.Globalization;
using System.Text;

namespace SpectraCollector.Core;

/// <summary>The raw field values of the window.</summary>
public sealed record FormInput(
    bool SingleInterval, string? CsvPath, string BeginText, string EndText, string OutputDir, string DataRoot, string Belt);

/// <summary>A ready request, or the plain-English problems that prevent one; the hint is shown whenever the samples are valid.</summary>
public sealed record FormCheck(CollectRequest? Request, IReadOnlyList<string> Problems, string? WindowHint);

/// <summary>Turns the raw form fields into a <see cref="CollectRequest"/> or a list of problems.</summary>
public static class FormValidator
{
    private const string SingleSampleName = "Sample_1";
    private const string SingleCsvName = "samples.csv";
    private const string DayFormat = "yyyy-MM-dd HH:mm:ss";
    private const string TimeFormat = "HH:mm:ss";

    private sealed record SampleSource(IReadOnlyList<Sample>? Samples, string CsvName, string CsvText);

    /// <summary>Validates every field; never throws, and reports every applicable problem.</summary>
    public static FormCheck Check(FormInput input)
    {
        var problems = new List<string>();
        var source = input.SingleInterval ? ReadSingle(input, problems) : ReadCsv(input.CsvPath, problems);
        problems.AddRange(OutputProblems(input));
        problems.AddRange(DataProblems(input));

        var hint = source.Samples is null ? null : Hint(source.Samples, input.SingleInterval);
        if (problems.Count > 0 || source.Samples is null)
        {
            return new FormCheck(null, problems, hint);
        }

        var request = new CollectRequest(
            source.Samples, source.CsvName, source.CsvText, new DataLayout(input.DataRoot, input.Belt), input.OutputDir);
        return new FormCheck(request, problems, hint);
    }

    private static SampleSource ReadSingle(FormInput input, List<string> problems)
    {
        var begin = ReadTime(input.BeginText, "Begin", "2026-11-06 13:30:00", problems, out var beginValue);
        var end = ReadTime(input.EndText, "End", "2026-11-06 13:40:00", problems, out var endValue);
        if (!begin || !end)
        {
            return new SampleSource(null, string.Empty, string.Empty);
        }

        if (endValue < beginValue)
        {
            problems.Add("End is earlier than Begin.");
            return new SampleSource(null, string.Empty, string.Empty);
        }

        IReadOnlyList<Sample> samples = [new Sample(SingleSampleName, beginValue, endValue)];
        return new SampleSource(samples, SingleCsvName, SampleTable.ToCsv(samples));
    }

    private static bool ReadTime(string text, string label, string example, List<string> problems, out DateTime value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            problems.Add($"Enter {label}.");
            return false;
        }

        if (!SampleTime.TryParse(text, out value))
        {
            problems.Add($"{label} is not a time. Use {example}.");
            return false;
        }

        return true;
    }

    private static SampleSource ReadCsv(string? path, List<string> problems)
    {
        var none = new SampleSource(null, string.Empty, string.Empty);
        if (string.IsNullOrWhiteSpace(path))
        {
            problems.Add("Choose a CSV file with sample times.");
            return none;
        }

        if (!File.Exists(path))
        {
            problems.Add("CSV file not found.");
            return none;
        }

        string text;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            text = reader.ReadToEnd();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            problems.Add($"Cannot read the CSV file: {e.Message.TrimEnd().TrimEnd('.')}.");
            return none;
        }

        try
        {
            var samples = SampleTable.Parse(new StringReader(text));
            return new SampleSource(samples, Path.GetFileName(path), text);
        }
        catch (SampleTimesException e)
        {
            problems.Add(e.Message);
            return none;
        }
    }

    private static List<string> OutputProblems(FormInput input)
    {
        if (string.IsNullOrWhiteSpace(input.OutputDir))
        {
            return ["Choose where to save the zip."];
        }

        if (!IsValidPath(input.OutputDir))
        {
            return ["The output folder is not a valid path."];
        }

        var inside = !string.IsNullOrWhiteSpace(input.DataRoot)
            && IsValidPath(input.DataRoot)
            && new DataLayout(input.DataRoot, input.Belt).Contains(input.OutputDir);
        return inside ? ["Choose an output folder outside the LyncisAC data folder."] : [];
    }

    private static List<string> DataProblems(FormInput input)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(input.DataRoot))
        {
            problems.Add("Choose the LyncisAC data folder.");
        }
        else if (!IsValidPath(input.DataRoot))
        {
            problems.Add("The LyncisAC data folder is not a valid path.");
        }
        else if (!Directory.Exists(input.DataRoot))
        {
            problems.Add("LyncisAC data folder not found.");
        }

        if (string.IsNullOrWhiteSpace(input.Belt))
        {
            problems.Add(@"Enter the belt folder, for example Main\Belt_1.");
        }

        return problems;
    }

    private static bool IsValidPath(string path)
    {
        try
        {
            Path.GetFullPath(path);
            return true;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static string Hint(IReadOnlyList<Sample> samples, bool single)
    {
        var from = samples.Min(s => s.Window.From);
        var to = samples.Max(s => s.Window.To);
        var format = from.Date == to.Date ? TimeFormat : DayFormat;
        var range = $"files from {from.ToString(format, CultureInfo.InvariantCulture)} to {to.ToString(format, CultureInfo.InvariantCulture)}";
        if (single)
        {
            return char.ToUpperInvariant(range[0]) + range[1..];
        }

        var noun = samples.Count == 1 ? "sample" : "samples";
        return $"{samples.Count.ToString(CultureInfo.InvariantCulture)} {noun}, {range}";
    }
}
