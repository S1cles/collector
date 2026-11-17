using System.Globalization;

namespace SpectraCollector.Core;

/// <summary>Where the analyzer keeps spectra: a data root and a belt such as <c>Main\Belt_1</c>.</summary>
public sealed record DataLayout(string DataRoot, string Belt)
{
    /// <summary>The folder with loose spectrum files for the month of <paramref name="hour"/>.</summary>
    public string TechSpectraDir(DateTime hour) => Path.Combine(BeltDir(hour), "Tech_Spectra");

    /// <summary>The path of the archive the analyzer packs for <paramref name="hour"/>.</summary>
    public string HourArchive(DateTime hour) => Path.Combine(
        BeltDir(hour),
        "Packed_Spectra",
        "spect_data_" + hour.ToString("yyyy-MM-dd_HH", CultureInfo.InvariantCulture) + ".zip");

    private string BeltDir(DateTime hour) => Path.Combine(
        [DataRoot, hour.ToString("yyyy_MM", CultureInfo.InvariantCulture), .. Belt.Split('\\', '/')]);
}
