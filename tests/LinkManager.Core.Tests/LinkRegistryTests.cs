using LinkManager.Core.Settings;
using LinkManager.Core;
using LinkManager.Core.Registry;

namespace LinkManager.Core.Tests;

public class LinkRegistryTests : IDisposable
{
    private readonly string _root;

    public LinkRegistryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"link-manager-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Load_creates_an_empty_registry_when_the_file_is_missing()
    {
        var registry = LinkRegistry.Load(_root);

        Assert.Empty(registry.Entries);
    }
    [Fact]
    public void Added_entry_survives_a_reload()
    {
        var registry = LinkRegistry.Load(_root);
        registry.Add(new LinkEntry
        {
            Source = @"C:\Users\me\.app",
            Target = @"F:\Data\.app",
            Type = "junction",
            Note = "测试条目",
        });

        registry.Save();

        var reloaded = LinkRegistry.Load(_root);
        var entry = Assert.Single(reloaded.Entries);
        Assert.Equal(@"C:\Users\me\.app", entry.Source);
        Assert.Equal(@"F:\Data\.app", entry.Target);
        Assert.Equal("junction", entry.Type);
        Assert.Equal("测试条目", entry.Note);
    }

    [Fact]
    public void Remove_deletes_by_source_case_insensitively()
    {
        var registry = LinkRegistry.Load(_root);
        registry.Add(new LinkEntry { Source = @"C:\A", Target = @"F:\B", Type = "junction" });
        registry.Save();

        var reloaded = LinkRegistry.Load(_root);

        Assert.True(reloaded.Remove(@"c:\a"));
        Assert.Empty(reloaded.Entries);
    }

    [Fact]
    public void Corrupt_registry_file_throws_a_Chinese_error_instead_of_crashing()
    {
        var dir = Path.Combine(_root, "link-manager");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "registry.json"), "{ 这不是 JSON !!");

        var ex = Assert.Throws<RegistryCorruptException>(() => LinkRegistry.Load(_root));

        Assert.Contains("损坏", ex.Message);
    }

    [Fact]
    public void Default_root_points_at_the_user_appdata_directory()
    {
        var expectedRoot = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var expectedFile = Path.Combine(expectedRoot, "link-manager", "registry.json");

        Assert.Equal(expectedFile, LinkRegistry.DefaultFilePath);
    }
    [Fact]
    public void Custom_root_overrides_the_default_location()
    {
        var registry = LinkRegistry.Load(_root);

        Assert.StartsWith(_root, registry.FilePath);
    }
    [Fact]
    public void Save_roundtrips_through_the_default_file_path_helper()
    {
        var registry = LinkRegistry.Load(_root);

        Assert.Equal(Path.Combine(_root, "link-manager", "registry.json"), registry.FilePath);
        Assert.Equal(Path.Combine(registry.RootDirectory, "link-manager", "registry.json"), registry.FilePath);
    }

    [Fact]
    public void Load_creates_the_registry_file_on_first_run()
    {
        LinkRegistry.Load(_root);

        Assert.True(File.Exists(Path.Combine(_root, "link-manager", "registry.json")));
    }
}

public class LinkRegistryLocationTests : IDisposable
{
    private readonly string _root;
    private readonly string _settingsFile;
    private readonly string _defaultRegistryFile;

    public LinkRegistryLocationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"link-manager-loc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _settingsFile = Path.Combine(_root, "settings.json");
        _defaultRegistryFile = Path.Combine(_root, "link-manager", "registry.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Custom_path_in_settings_redirects_Load_to_the_custom_file()
    {
        var custom = Path.Combine(_root, "custom-reg", "reg.json");
        LinkSettings.Load(_settingsFile).WithRegistryFilePath(custom).Save();

        var registry = LinkRegistry.Load(settingsFilePath: _settingsFile);

        Assert.Equal(custom, registry.FilePath);
        Assert.False(File.Exists(_defaultRegistryFile));
    }

    [Fact]
    public void Data_written_at_the_custom_location_survives_a_reload()
    {
        var custom = Path.Combine(_root, "custom-reg", "reg.json");
        LinkSettings.Load(_settingsFile).WithRegistryFilePath(custom).Save();

        var registry = LinkRegistry.Load(settingsFilePath: _settingsFile);
        registry.Add(new LinkEntry { Source = @"C:\A", Target = @"F:\B", Type = "junction" });
        registry.Save();

        var reloaded = LinkRegistry.Load(settingsFilePath: _settingsFile);
        var entry = Assert.Single(reloaded.Entries);
        Assert.Equal(@"C:\A", entry.Source);
    }

    [Fact]
    public void No_custom_path_in_settings_resolves_the_default_location_without_touching_it()
    {
        // 只验证解析结果指向产品默认位置；不调用 Load（Load 会在真实 AppData 建文件）。
        // Load 的创建行为已由 Custom_path_in_settings_redirects_Load_to_the_custom_file 覆盖。
        var resolved = LinkRegistry.ResolveRegistryFilePath(LinkSettings.Load(_settingsFile));

        Assert.Equal(LinkRegistry.DefaultFilePath, resolved);
        Assert.StartsWith(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), resolved);
    }

    [Fact]
    public void Explicit_root_directory_still_wins_over_settings()
    {
        // registryRoot 非空 = 显式指定（测试与嵌入方场景），优先于设置
        var registry = LinkRegistry.Load(rootDirectory: _root);

        Assert.Equal(_defaultRegistryFile, registry.FilePath);
    }

    [Fact]
    public void ResolveRegistryFilePath_reflects_the_settings_value()
    {
        // 默认回落（无显式 root、无自定义路径）→ 产品默认位置
        Assert.Equal(LinkRegistry.DefaultFilePath,
            LinkRegistry.ResolveRegistryFilePath(LinkSettings.Load(_settingsFile)));

        var custom = Path.Combine(_root, "custom-reg", "reg.json");
        var settings = LinkSettings.Load(_settingsFile).WithRegistryFilePath(custom);
        Assert.Equal(custom, LinkRegistry.ResolveRegistryFilePath(settings));

        // 显式 root 优先于设置中的自定义路径
        Assert.Equal(_defaultRegistryFile, LinkRegistry.ResolveRegistryFilePath(settings, _root));
    }

    [Fact]
    public void Corrupt_settings_file_surfaces_the_settings_error_from_Load()
    {
        File.WriteAllText(_settingsFile, "{ not json");

        Assert.Throws<SettingsCorruptException>(() => LinkRegistry.Load(settingsFilePath: _settingsFile));
    }
}

public class LinkRegistryNoteTests : IDisposable
{
    private readonly string _root;

    public LinkRegistryNoteTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"link-manager-note-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static LinkRegistry Seeded(string root)
    {
        var registry = LinkRegistry.Load(rootDirectory: root);
        registry.Add(new LinkEntry { Source = @"C:\Links\App", Target = @"F:\Data\App", Type = "junction", Note = "旧备注" });
        registry.Save();
        return registry;
    }

    [Fact]
    public void UpdateNote_changes_the_note_of_the_matching_entry()
    {
        var registry = Seeded(_root);

        registry.UpdateNote(@"C:\Links\App", "新备注");

        Assert.Equal("新备注", Assert.Single(registry.Entries).Note);
    }

    [Fact]
    public void Updated_note_survives_a_reload()
    {
        var registry = Seeded(_root);
        registry.UpdateNote(@"C:\Links\App", "持久化备注");
        registry.Save();

        var reloaded = LinkRegistry.Load(rootDirectory: _root);
        Assert.Equal("持久化备注", Assert.Single(reloaded.Entries).Note);
    }

    [Fact]
    public void UpdateNote_matches_source_case_insensitively()
    {
        var registry = Seeded(_root);

        Assert.True(registry.UpdateNote(@"c:\links\app", "大小写不敏感"));

        Assert.Equal("大小写不敏感", Assert.Single(registry.Entries).Note);
    }

    [Fact]
    public void UpdateNote_returns_false_for_an_unknown_source()
    {
        var registry = Seeded(_root);

        Assert.False(registry.UpdateNote(@"C:\No\Such", "备注"));

        Assert.Equal("旧备注", Assert.Single(registry.Entries).Note);
    }

    [Fact]
    public void UpdateNote_to_empty_string_clears_the_note()
    {
        var registry = Seeded(_root);

        registry.UpdateNote(@"C:\Links\App", "");

        Assert.Equal("", Assert.Single(registry.Entries).Note);
    }
}

public class LinkRegistryPathNormalizationTests : IDisposable
{
    private readonly string _root;

    public LinkRegistryPathNormalizationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"link-manager-pn-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private LinkRegistry Seeded()
    {
        var registry = LinkRegistry.Load(rootDirectory: _root);
        registry.Add(new LinkEntry { Source = @"C:\Links\App", Target = @"F:\Data\App", Type = "junction" });
        registry.Save();
        return registry;
    }

    [Fact]
    public void UpdateNote_matches_forward_slash_input()
    {
        var registry = Seeded();

        Assert.True(registry.UpdateNote("C:/Links/App", "斜杠归一"));

        Assert.Equal("斜杠归一", Assert.Single(registry.Entries).Note);
    }

    [Fact]
    public void Remove_matches_forward_slash_input()
    {
        var registry = Seeded();

        Assert.True(registry.Remove("C:/Links/App"));

        Assert.Empty(registry.Entries);
    }
}

public class LinkRegistrySeparatorTests : IDisposable
{
    private readonly string _root;

    public LinkRegistrySeparatorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"link-manager-sep-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private LinkRegistry Seeded()
    {
        var registry = LinkRegistry.Load(rootDirectory: _root);
        registry.Add(new LinkEntry { Source = @"C:\Links\App", Target = @"F:\Data\App", Type = "junction" });
        registry.Save();
        return registry;
    }

    [Fact]
    public void Forward_slash_source_still_matches_for_update_note_and_remove()
    {
        var registry = Seeded();

        Assert.True(registry.UpdateNote("C:/Links/App", "n"));
        Assert.Equal("n", Assert.Single(registry.Entries).Note);

        Assert.True(registry.Remove("C:/Links/App"));
        Assert.Empty(registry.Entries);
    }
}
