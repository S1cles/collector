using SpectraCollector.Core;

namespace SpectraCollector.Core.Tests;

public class TimeWindowTests
{
    private static DateTime At(int hour, int minute, int second) => new(2026, 11, 6, hour, minute, second);

    [Fact]
    public void Rounded_FloorsBeginAndCeilsEndToMinute()
    {
        var window = TimeWindow.Rounded(At(13, 30, 30), At(13, 40, 1));

        Assert.Equal(At(13, 30, 0), window.From);
        Assert.Equal(At(13, 41, 0), window.To);
    }

    [Fact]
    public void Rounded_KeepsWholeMinutesUnchanged()
    {
        var window = TimeWindow.Rounded(At(13, 30, 0), At(13, 40, 0));

        Assert.Equal(At(13, 30, 0), window.From);
        Assert.Equal(At(13, 40, 0), window.To);
    }

    [Fact]
    public void Contains_IsInclusiveAtBothEnds()
    {
        var window = TimeWindow.Rounded(At(13, 30, 30), At(13, 40, 1));

        Assert.True(window.Contains(At(13, 30, 0)));
        Assert.True(window.Contains(At(13, 41, 0)));
        Assert.False(window.Contains(At(13, 41, 1)));
        Assert.False(window.Contains(At(13, 29, 59)));
    }

    [Fact]
    public void Hours_YieldsStartOfEveryIntersectingHour()
    {
        var window = new TimeWindow(At(12, 59, 30), At(14, 0, 10));

        Assert.Equal([At(12, 0, 0), At(13, 0, 0), At(14, 0, 0)], window.Hours());
    }
}
