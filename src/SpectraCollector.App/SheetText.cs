using System.Globalization;
using System.IO;
using SpectraCollector.Core;

namespace SpectraCollector.App;

/// <summary>The wording and number formats of the report sheet; plain functions of Core values, free of WPF types.</summary>
internal static class SheetText
{
    public const string SingleIntervalTitle = "Single interval";
    public const string ReportFileName = "collect-report.txt";

    private const string ClockFormat = "HH:mm:ss";
    private const string DayFormat = "yyyy-MM-dd HH:mm:ss";
    private const string NoCsvTitle = "No CSV file chosen";
    private const int ShortestRange = 3;

    /// <summary>The "Files from" cell: the clock time, with the date when the window crosses midnight.</summary>
    public static string From(TimeWindow window) => Clock(window.From, CrossesMidnight(window));

    /// <summary>The "to" cell, formatted like <see cref="From"/>.</summary>
    public static string To(TimeWindow window) => Clock(window.To, CrossesMidnight(window));

    /// <summary>The sheet title for a CSV path still being typed: its file name, or a prompt when it is blank.</summary>
    public static string CsvTitle(string path)
    {
        var trimmed = path.Trim();
        if (trimmed.Length == 0)
        {
            return NoCsvTitle;
        }

        var name = Path.GetFileName(trimmed);
        return name.Length > 0 ? name : trimmed;
    }

    /// <summary>
    /// The footer line while running: <c>Searching hour 3 of 8</c> names the hour being searched, and
    /// <c>196 of 327 files · 60 %</c> counts written files with a percentage rounded down.
    /// </summary>
    public static string Status(CollectProgress progress)
    {
        if (progress.Phase == CollectPhase.Searching)
        {
            var hour = Math.Min(progress.Done + 1, progress.Total);
            return progress.Total == 0
                ? "Searching…"
                : string.Create(CultureInfo.InvariantCulture, $"Searching hour {hour} of {progress.Total}");
        }

        var percent = progress.Total == 0 ? 0 : (int)((long)progress.Done * 100 / progress.Total);
        return string.Create(
            CultureInfo.InvariantCulture, $"{progress.Done} of {progress.Total} files · {percent} %");
    }

    /// <summary><c>1 file</c> or <c>327 files</c>.</summary>
    public static string Files(int count) =>
        string.Create(CultureInfo.InvariantCulture, $"{count} {(count == 1 ? "file" : "files")}");

    /// <summary>The column header of one spectrometer, e.g. <c>Spectra_0</c>.</summary>
    public static string Spectrometer(int number) =>
        string.Create(CultureInfo.InvariantCulture, $"Spectra_{number}");

    /// <summary>A count cell.</summary>
    public static string Count(int count) => count.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Footnote numbers of one sample, ascending: <c>2</c>, <c>2, 3</c>, and runs of three or more as <c>4–9</c>,
    /// so a sample with many gaps does not widen the Note column.
    /// </summary>
    public static string NoteNumbers(IReadOnlyList<int> numbers)
    {
        var parts = new List<string>();
        var start = 0;
        while (start < numbers.Count)
        {
            var end = start;
            while (end + 1 < numbers.Count && numbers[end + 1] == numbers[end] + 1)
            {
                end++;
            }

            if (end - start + 1 >= ShortestRange)
            {
                parts.Add(Count(numbers[start]) + "–" + Count(numbers[end]));
            }
            else
            {
                parts.AddRange(numbers.Skip(start).Take(end - start + 1).Select(Count));
            }

            start = end + 1;
        }

        return string.Join(", ", parts);
    }

    private static bool CrossesMidnight(TimeWindow window) => window.From.Date != window.To.Date;

    private static string Clock(DateTime time, bool withDate) =>
        time.ToString(withDate ? DayFormat : ClockFormat, CultureInfo.InvariantCulture);
}
