using System.Text.Json;

namespace SpectraCollector.Core;

/// <summary>The fields the window remembers between runs.</summary>
public sealed record AppSettings(string? CsvPath, string? OutputDir, string DataRoot, string Belt, bool SingleInterval)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>Settings used when nothing has been saved yet.</summary>
    public static AppSettings Default { get; } = new(null, null, @"D:\LyncisAC_DATA", @"Main\Belt_1", false);

    /// <summary>%LOCALAPPDATA%\SpectraCollector\settings.json.</summary>
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpectraCollector", "settings.json");

    /// <summary>Reads the file; a missing or corrupt file gives the defaults, empty fields fall back individually.</summary>
    public static AppSettings Load(string path)
    {
        if (!File.Exists(path))
        {
            return Default;
        }

        AppSettings? loaded;
        try
        {
            loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json);
        }
        catch (JsonException)
        {
            return Default;
        }

        if (loaded is null)
        {
            return Default;
        }

        return loaded with
        {
            DataRoot = string.IsNullOrWhiteSpace(loaded.DataRoot) ? Default.DataRoot : loaded.DataRoot,
            Belt = string.IsNullOrWhiteSpace(loaded.Belt) ? Default.Belt : loaded.Belt,
        };
    }

    /// <summary>Writes the file through a .tmp so a crash never leaves it half-written.</summary>
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
        File.Move(tmp, path, overwrite: true);
    }
}
