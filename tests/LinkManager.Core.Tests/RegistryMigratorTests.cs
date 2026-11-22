using LinkManager.Core.Registry;
using LinkManager.Core.Settings;

namespace LinkManager.Core.Tests;

/// <summary>
/// 迁移测试纪律：一切路径显式给定（settingsFilePath / root / 文件路径），
/// 绝不解析产品默认位置——那是真实用户数据（也是其他会话的共享资源）。
/// </summary>
public class RegistryMigratorTests : IDisposable
{
    private readonly string _root;
    private readonly string _settingsFile;
    private readonly string _oldRoot;     // 旧登记 root：_root\old\link-manager\registry.json
    private readonly string _oldFile;

    public RegistryMigratorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"link-manager-mig-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _settingsFile = Path.Combine(_root, "settings.json");
        _oldRoot = Path.Combine(_root, "old");
        _oldFile = Path.Combine(_oldRoot, LinkRegistry.DefaultFolderName, LinkRegistry.DefaultFileName);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    /// <summary>在显式 root 下种一个含一条数据的登记表。</summary>
    private void SeedRegistryAt(string root)
    {
        Directory.CreateDirectory(root);
        var registry = LinkRegistry.Load(rootDirectory: root);
        registry.Add(new LinkEntry { Source = @"C:\A", Target = @"F:\B", Type = "junction", Note = "旧数据" });
        registry.Save();
    }

    [Fact]
    public void Relocate_moves_existing_data_to_the_new_location_and_points_settings_there()
    {
        SeedRegistryAt(_oldRoot);
        var newPath = Path.Combine(_root, "new-home", "reg.json");

        RegistryMigrator.RelocateRegistry(newPath, explicitRoot: _oldRoot, settingsFilePath: _settingsFile);

        // 新位置有旧数据
        var migrated = LinkRegistry.Load(settingsFilePath: _settingsFile);
        Assert.Equal(newPath, migrated.FilePath);
        var entry = Assert.Single(migrated.Entries);
        Assert.Equal("旧数据", entry.Note);
        // 旧文件已删除
        Assert.False(File.Exists(_oldFile));
        // 设置已持久化自定义路径
        Assert.Equal(newPath, LinkSettings.Load(_settingsFile).RegistryFilePath);
    }

    [Fact]
    public void Relocate_without_an_existing_registry_lands_an_empty_table_at_the_new_path()
    {
        // 全新安装场景：旧位置无登记文件，也应允许改位置
        var newPath = Path.Combine(_root, "new-home-2", "reg.json");

        RegistryMigrator.RelocateRegistry(newPath, explicitRoot: _oldRoot, settingsFilePath: _settingsFile);

        Assert.Equal(newPath, LinkSettings.Load(_settingsFile).RegistryFilePath);
        Assert.True(File.Exists(newPath));
        Assert.Empty(LinkRegistry.Load(settingsFilePath: _settingsFile).Entries);
    }

    [Fact]
    public void Relocate_refuses_to_overwrite_an_existing_file_at_the_new_path()
    {
        SeedRegistryAt(_oldRoot);
        var newPath = Path.Combine(_root, "existing", "reg.json");
        Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
        File.WriteAllText(newPath, "precious data");

        var ex = Assert.Throws<RegistryMigrationException>(() =>
            RegistryMigrator.RelocateRegistry(newPath, explicitRoot: _oldRoot, settingsFilePath: _settingsFile));

        Assert.Contains("已存在", ex.Message);
        Assert.Equal("precious data", File.ReadAllText(newPath));
        // 旧数据原封不动，设置未变
        Assert.True(File.Exists(_oldFile));
        Assert.Null(LinkSettings.Load(_settingsFile).RegistryFilePath);
    }

    [Fact]
    public void Relocate_to_the_current_location_is_a_no_op()
    {
        SeedRegistryAt(_oldRoot);
        var current = Path.Combine(_oldRoot, LinkRegistry.DefaultFolderName, LinkRegistry.DefaultFileName);

        RegistryMigrator.RelocateRegistry(current, explicitRoot: _oldRoot, settingsFilePath: _settingsFile);

        Assert.True(File.Exists(_oldFile));
        Assert.Single(LinkRegistry.Load(rootDirectory: _oldRoot).Entries);
    }

    [Fact]
    public void MigrateTo_moves_the_file_and_deletes_the_old_one()
    {
        SeedRegistryAt(_oldRoot);
        var newFile = Path.Combine(_root, "new-reg", "registry.json");

        RegistryMigrator.MigrateTo(_oldFile, newFile);

        Assert.True(File.Exists(newFile));
        Assert.False(File.Exists(_oldFile));
        // 新文件可加载且数据完整
        var reloaded = LinkRegistry.Load(rootDirectory: Path.Combine(_root, "new-reg"));
        // 直接按路径验证内容（newFile 不是标准布局，用 JSON 校验）
        var json = File.ReadAllText(newFile);
        Assert.Contains("旧数据", json);
        _ = reloaded;
    }

    [Fact]
    public void MigrateTo_refuses_to_overwrite_an_existing_target_file()
    {
        SeedRegistryAt(_oldRoot);
        var newFile = Path.Combine(_root, "blocked", "registry.json");
        Directory.CreateDirectory(Path.GetDirectoryName(newFile)!);
        File.WriteAllText(newFile, "occupied");

        var ex = Assert.Throws<RegistryMigrationException>(() => RegistryMigrator.MigrateTo(_oldFile, newFile));

        Assert.Contains("已存在", ex.Message);
        // 旧数据原封不动
        Assert.True(File.Exists(_oldFile));
        Assert.Single(LinkRegistry.Load(rootDirectory: _oldRoot).Entries);
        Assert.Equal("occupied", File.ReadAllText(newFile));
    }
}
