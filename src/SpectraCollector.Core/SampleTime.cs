using System.Globalization;

namespace SpectraCollector.Core;

/// <summary>Parses the time formats accepted for sample begin and end.</summary>
public static class SampleTime
{
    private static readonly string[] Formats =
    [
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm",
        "dd.MM.yyyy HH:mm:ss",
        "dd.MM.yyyy HH:mm",
        "dd/MM/yyyy HH:mm:ss",
        "dd/MM/yyyy HH:mm",
    ];

    /// <summary>Parses trimmed <paramref name="text"/> in one of the six accepted formats.</summary>
    public static bool TryParse(string text, out DateTime value) =>
        DateTime.TryParseExact(text.Trim(), Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
}
