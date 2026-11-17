using System.Globalization;

namespace SpectraCollector.Core;

/// <summary>Where the analyzer keeps spectra: a data root and a belt such as <c>Main\Belt_1</c>.</summary>
public sealed record DataLayout(string DataRoot, string Belt)
{
    /// <summary>The belt folder for the month of <paramref name="hour"/>, e.g. <c>D:\LyncisAC_DATA\2026_11\Main\Belt_1</c>.</summary>
    public string BeltDir(DateTime hour) => Path.Combine(
        [DataRoot, hour.ToString("yyyy_MM", CultureInfo.InvariantCulture), .. Belt.Split('\\', '/')]);

    /// <summary>The folder with loose spectrum files for the month of <paramref name="hour"/>.</summary>
    public string TechSpectraDir(DateTime hour) => Path.Combine(BeltDir(hour), "Tech_Spectra");

    /// <summary>The path of the archive the analyzer packs for <paramref name="hour"/>.</summary>
    public string HourArchive(DateTime hour) => Path.Combine(
        BeltDir(hour),
        "Packed_Spectra",
        "spect_data_" + hour.ToString("yyyy-MM-dd_HH", CultureInfo.InvariantCulture) + ".zip");

    /// <summary>True when <paramref name="path"/> is the data root or lies under it.</summary>
    public bool Contains(string path)
    {
        var root = WithSeparator(Path.GetFullPath(DataRoot));
        return WithSeparator(Path.GetFullPath(path)).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static string WithSeparator(string path) =>
        Path.EndsInDirectorySeparator(path) ? path : path + Path.DirectorySeparatorChar;
}
