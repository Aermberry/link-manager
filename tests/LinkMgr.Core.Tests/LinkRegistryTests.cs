using LinkMgr.Core;
using LinkMgr.Core.Registry;

namespace LinkMgr.Core.Tests;

public class LinkRegistryTests : IDisposable
{
    private readonly string _root;

    public LinkRegistryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"linkmgr-tests-{Guid.NewGuid():N}");
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
        var dir = Path.Combine(_root, "linkmgr");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "registry.json"), "{ 这不是 JSON !!");

        var ex = Assert.Throws<RegistryCorruptException>(() => LinkRegistry.Load(_root));

        Assert.Contains("损坏", ex.Message);
    }

    [Fact]
    public void Default_root_points_at_the_user_appdata_directory()
    {
        var expectedRoot = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var expectedFile = Path.Combine(expectedRoot, "linkmgr", "registry.json");

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

        Assert.Equal(Path.Combine(_root, "linkmgr", "registry.json"), registry.FilePath);
        Assert.Equal(Path.Combine(registry.RootDirectory, "linkmgr", "registry.json"), registry.FilePath);
    }

    [Fact]
    public void Load_creates_the_registry_file_on_first_run()
    {
        LinkRegistry.Load(_root);

        Assert.True(File.Exists(Path.Combine(_root, "linkmgr", "registry.json")));
    }
}
