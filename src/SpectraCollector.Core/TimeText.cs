using System.Globalization;

namespace SpectraCollector.Core;

/// <summary>One format for time ranges, shared by the report and by sample names made from times.</summary>
internal static class TimeText
{
    private const string Full = "yyyy-MM-dd HH:mm:ss";

    /// <summary><c>2026-11-06 13:05:00–13:12:00</c>; the date repeats only when the range crosses midnight.</summary>
    public static string Range(DateTime from, DateTime to)
    {
        var sameDay = from.Date == to.Date;
        return from.ToString(Full, CultureInfo.InvariantCulture) + "–"
            + to.ToString(sameDay ? "HH:mm:ss" : Full, CultureInfo.InvariantCulture);
    }
}
