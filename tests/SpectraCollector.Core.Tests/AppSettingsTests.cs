using SpectraCollector.Core;

namespace SpectraCollector.Core.Tests;

public sealed class AppSettingsTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("settings-");

    public void Dispose() => _dir.Delete(true);

    private string SettingsPath => Path.Combine(_dir.FullName, "sub", "settings.json");

    [Fact]
    public void Round_trips_and_creates_the_directory()
    {
        var settings = new AppSettings("a.csv", "out", "E:/data", "Main/Belt_2", true);

        settings.Save(SettingsPath);

        Assert.Equal(settings, AppSettings.Load(SettingsPath));
        Assert.False(File.Exists(SettingsPath + ".tmp"));
        Assert.Contains("\"dataRoot\"", File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void Missing_file_gives_defaults() =>
        Assert.Equal(AppSettings.Default, AppSettings.Load(SettingsPath));

    [Fact]
    public void Corrupt_file_gives_defaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, "{ not json");

        Assert.Equal(AppSettings.Default, AppSettings.Load(SettingsPath));
    }

    [Fact]
    public void Empty_fields_fall_back_individually()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, """{"csvPath":"x.csv","dataRoot":"","singleInterval":true}""");

        var loaded = AppSettings.Load(SettingsPath);

        Assert.Equal(new AppSettings("x.csv", null, AppSettings.Default.DataRoot, AppSettings.Default.Belt, true), loaded);
    }
}
