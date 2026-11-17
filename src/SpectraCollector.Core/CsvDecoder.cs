using System.Globalization;
using System.Text;

namespace SpectraCollector.Core;

/// <summary>
/// Turns the bytes of a sample-times CSV into text the way it was most likely saved: by its byte order mark,
/// else as UTF-8, else (Excel's "CSV" on Windows) in the ANSI code page of the current culture.
/// </summary>
internal static class CsvDecoder
{
    private const int FallbackCodePage = 1252;
    private static readonly UTF8Encoding StrictUtf8 = new(false, throwOnInvalidBytes: true);

    private static readonly Encoding[] BomEncodings =
    [
        new UTF32Encoding(bigEndian: false, byteOrderMark: true),
        Encoding.UTF8,
        Encoding.Unicode,
        Encoding.BigEndianUnicode,
        new UTF32Encoding(bigEndian: true, byteOrderMark: true),
    ];

    static CsvDecoder() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>Decodes <paramref name="bytes"/>; never throws for content, at worst a character is misread.</summary>
    public static string Decode(byte[] bytes)
    {
        foreach (var encoding in BomEncodings)
        {
            var bom = encoding.Preamble;
            if (bytes.AsSpan().StartsWith(bom))
            {
                return encoding.GetString(bytes, bom.Length, bytes.Length - bom.Length);
            }
        }

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return AnsiEncoding().GetString(bytes);
        }
    }

    private static Encoding AnsiEncoding()
    {
        var codePage = CultureInfo.CurrentCulture.TextInfo.ANSICodePage;
        if (codePage == 0)
        {
            return Encoding.GetEncoding(FallbackCodePage);
        }

        try
        {
            return Encoding.GetEncoding(codePage);
        }
        catch (Exception e) when (e is NotSupportedException or ArgumentException)
        {
            return Encoding.GetEncoding(FallbackCodePage);
        }
    }
}
