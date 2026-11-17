namespace SpectraCollector.Core;

/// <summary>An inclusive time interval, aligned to whole minutes when built with <see cref="Rounded"/>.</summary>
public readonly record struct TimeWindow(DateTime From, DateTime To)
{
    /// <summary>Floors <paramref name="begin"/> and ceils <paramref name="end"/> to the minute.</summary>
    public static TimeWindow Rounded(DateTime begin, DateTime end)
    {
        var from = new DateTime(begin.Ticks - begin.Ticks % TimeSpan.TicksPerMinute, begin.Kind);
        var remainder = end.Ticks % TimeSpan.TicksPerMinute;
        var to = remainder == 0
            ? end
            : new DateTime(end.Ticks - remainder + TimeSpan.TicksPerMinute, end.Kind);
        return new TimeWindow(from, to);
    }

    /// <summary>True when <paramref name="t"/> lies within the window, both ends included.</summary>
    public bool Contains(DateTime t) => t >= From && t <= To;

    /// <summary>The start of every clock hour that intersects the window.</summary>
    public IEnumerable<DateTime> Hours()
    {
        var hour = new DateTime(From.Ticks - From.Ticks % TimeSpan.TicksPerHour, From.Kind);
        for (; hour <= To; hour = hour.AddHours(1))
        {
            yield return hour;
        }
    }
}
