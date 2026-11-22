using LinkManager.Core.Settings;

namespace LinkManager.Core.Tests;

public class LinkSettingsTests : IDisposable
{
    private readonly string _root;
    private readonly string _settingsFile;

    public LinkSettingsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"link-manager-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _settingsFile = Path.Combine(_root, "link-manager", "settings.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Load_without_file_returns_default_settings_without_creating_it()
    {
        var settings = LinkSettings.Load(_settingsFile);

        Assert.Null(settings.RegistryFilePath);
        Assert.False(File.Exists(_settingsFile));
    }

    [Fact]
    public void Custom_registry_path_survives_a_save_and_reload()
    {
        var settings = LinkSettings.Load(_settingsFile);
        settings.WithRegistryFilePath(@"D:\MyData\registry.json").Save();

        Assert.True(File.Exists(_settingsFile));
        var reloaded = LinkSettings.Load(_settingsFile);
        Assert.Equal(@"D:\MyData\registry.json", reloaded.RegistryFilePath);
    }

    [Fact]
    public void Clearing_the_custom_path_back_to_null_survives_a_reload()
    {
        LinkSettings.Load(_settingsFile)
            .WithRegistryFilePath(@"D:\MyData\registry.json").Save();

        LinkSettings.Load(_settingsFile)
            .WithRegistryFilePath(null).Save();

        var reloaded = LinkSettings.Load(_settingsFile);
        Assert.Null(reloaded.RegistryFilePath);
    }

    [Fact]
    public void Corrupt_settings_file_throws_a_Chinese_error_instead_of_crashing()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsFile)!);
        File.WriteAllText(_settingsFile, "{ 这不是 JSON !!");

        var ex = Assert.Throws<SettingsCorruptException>(() => LinkSettings.Load(_settingsFile));

        Assert.Contains("损坏", ex.Message);
    }

    [Fact]
    public void Default_settings_path_is_in_the_appdata_link_manager_folder()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "link-manager", "settings.json");

        Assert.Equal(expected, LinkSettings.DefaultFilePath);
    }
}
