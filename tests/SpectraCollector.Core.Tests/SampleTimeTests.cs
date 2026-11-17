using SpectraCollector.Core;

namespace SpectraCollector.Core.Tests;

public class SampleTimeTests
{
    [Theory]
    [InlineData("2026-11-06 13:05:07", 2026, 11, 6, 13, 5, 7)]
    [InlineData("2026-11-06 13:05", 2026, 11, 6, 13, 5, 0)]
    [InlineData("06.11.2026 13:05:07", 2026, 11, 6, 13, 5, 7)]
    [InlineData("06.11.2026 13:05", 2026, 11, 6, 13, 5, 0)]
    [InlineData("06/11/2026 13:05:07", 2026, 11, 6, 13, 5, 7)]
    [InlineData("06/11/2026 13:05", 2026, 11, 6, 13, 5, 0)]
    [InlineData("  2026-11-06 13:05  ", 2026, 11, 6, 13, 5, 0)]
    public void TryParse_AcceptsSupportedFormats(string text, int y, int mo, int d, int h, int mi, int s)
    {
        Assert.True(SampleTime.TryParse(text, out var value));
        Assert.Equal(new DateTime(y, mo, d, h, mi, s), value);
        Assert.Equal(DateTimeKind.Unspecified, value.Kind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("2026-11-06T13:05:00")]
    [InlineData("6.11.2026 13:05")]
    [InlineData("2026-11-06")]
    [InlineData("13:05")]
    [InlineData("06/13/2026 13:05")]
    [InlineData("tomorrow")]
    public void TryParse_RejectsEverythingElse(string text)
    {
        Assert.False(SampleTime.TryParse(text, out _));
    }
}
