using SpectraCollector.Core;

namespace SpectraCollector.Core.Tests;

public class DataLayoutTests
{
    private const string Root = "data";
    private static readonly DateTime Hour = new(2026, 11, 6, 13, 0, 0);

    private static readonly string ExpectedArchive =
        Path.Combine(Root, "2026_11", "Main", "Belt_1", "Packed_Spectra", "spect_data_2026-11-06_13.zip");

    private static readonly string ExpectedTech = Path.Combine(Root, "2026_11", "Main", "Belt_1", "Tech_Spectra");

    [Theory]
    [InlineData(@"Main\Belt_1")]
    [InlineData("Main/Belt_1")]
    [InlineData(@"Main\Belt_1\")]
    public void Paths_UseLiteralAnalyzerLayout(string belt)
    {
        var layout = new DataLayout(Root, belt);

        Assert.Equal(ExpectedArchive, layout.HourArchive(Hour));
        Assert.Equal(ExpectedTech, layout.TechSpectraDir(Hour));
    }
}
