using SpectraCollector.Core;

namespace SpectraCollector.Core.Tests;

public class SpectrumNameTests
{
    [Theory]
    [InlineData("Spectra_0_Belt_1_2026.11.06_13.11.10.csv", 0)]
    [InlineData("Spectra_0_Belt_1_2026.11.06_13.11.10", 0)]
    [InlineData("Spectra_12_Belt_3_2026.11.06_13.11.10.csv", 12)]
    public void TryParse_ReadsSpectrometerAndTimestamp(string fileName, int spectrometer)
    {
        Assert.True(SpectrumName.TryParse(fileName, out var name));

        Assert.Equal(fileName, name.FileName);
        Assert.Equal(spectrometer, name.Spectrometer);
        Assert.Equal(new DateTime(2026, 11, 6, 13, 11, 10), name.Timestamp);
    }

    [Theory]
    [InlineData("spect_data_2026-11-06_13.zip")]
    [InlineData("Spectra_x_Belt_1_2026.11.06_13.11.10.csv")]
    [InlineData("Spectra_0_Belt_1_2026.13.06_13.11.10.csv")]
    [InlineData("Spectra_0_Belt_1.csv")]
    public void TryParse_RejectsNonSpectrumNames(string fileName)
    {
        Assert.False(SpectrumName.TryParse(fileName, out var name));
        Assert.Null(name);
    }
}
