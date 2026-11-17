using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SpectraCollector.Core;

/// <summary>A parsed analyzer spectrum file name <c>Spectra_&lt;n&gt;_..._yyyy.MM.dd_HH.mm.ss[.ext]</c>.</summary>
public sealed partial record SpectrumName(string FileName, int Spectrometer, DateTime Timestamp)
{
    private const string TimestampFormat = "yyyy.MM.dd_HH.mm.ss";

    [GeneratedRegex(@"^Spectra_(?<n>\d+)_.*?(?<ts>\d{4}\.\d{2}\.\d{2}_\d{2}\.\d{2}\.\d{2})(?:\.[^.]*)?$")]
    private static partial Regex Pattern();

    /// <summary>Parses <paramref name="fileName"/>; returns false for anything that is not a spectrum name.</summary>
    public static bool TryParse(string fileName, [NotNullWhen(true)] out SpectrumName? name)
    {
        name = null;
        var match = Pattern().Match(fileName);
        if (!match.Success
            || !int.TryParse(match.Groups["n"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var spectrometer)
            || !DateTime.TryParseExact(match.Groups["ts"].Value, TimestampFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var timestamp))
        {
            return false;
        }

        name = new SpectrumName(fileName, spectrometer, timestamp);
        return true;
    }
}
