using LinkManager.Core;
using LinkManager.Core.Health;
using LinkManager.Core.Links;
using LinkManager.Core.Registry;

namespace LinkManager.Core.Tests;

/// <summary>测试桩：模拟"已开启开发者模式"，绕过门槛专测护栏与路径校验。</summary>
internal sealed class EnabledDetector : IDeveloperModeDetector
{
    public bool IsDeveloperModeEnabled => true;
}

/// <summary>测试桩：模拟"系统未开开发者模式"，验证门槛拒绝。</summary>
internal sealed class DisabledDetector : IDeveloperModeDetector
{
    public bool IsDeveloperModeEnabled => false;
}

public class LinkCreatorSymlinkTests : IDisposable
{
    private readonly string _root;

    public LinkCreatorSymlinkTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"link-manager-symlink-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static bool CanCreateSymlinks() =>
        OperatingSystem.IsWindows() &&
        new WindowsDeveloperModeDetector().IsDeveloperModeEnabled;

    [Fact]
    public void CreateSymlink_rejects_when_developer_mode_is_disabled()
    {
        // 门槛前置：未开开发者模式时，任何输入（含合法绝对路径）都被拒
        var source = Path.Combine(_root, "src");
        var target = Path.Combine(_root, "dst");
        Directory.CreateDirectory(source);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            LinkCreator.CreateSymlink(source, target, note: "",
                detector: new DisabledDetector()));

        Assert.Contains("开发者模式", ex.Message);
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public void CreateSymlink_rejects_relative_source_path()
    {
        // symlink 只接受绝对路径（issue #18）；用 EnabledDetector 桩满足门槛，专测路径校验
        var ex = Assert.Throws<LinkCreateException>(() =>
            LinkCreator.CreateSymlink("relative-src", "relative-dst", note: "",
                detector: new EnabledDetector()));

        Assert.Contains("相对路径", ex.Message);
        Assert.Contains("绝对路径", ex.Message);
    }

    [Fact]
    public void CreateSymlink_rejects_relative_target_path()
    {
        var source = Path.Combine(_root, "src-rel2");
        Directory.CreateDirectory(source);

        var ex = Assert.Throws<LinkCreateException>(() =>
            LinkCreator.CreateSymlink(source, "relative-dst", note: "",
                detector: new EnabledDetector()));

        Assert.Contains("相对路径", ex.Message);
    }

    // 无权限环境（未开开发者模式/非 Windows）自动跳过；本机已开启则真实执行
    [SkippableFact]
    public void CreateSymlink_creates_symlink_and_registers_it()
    {
        Skip.IfNot(CanCreateSymlinks(), "环境无 symlink 权限（未开开发者模式或非 Windows）");

        var source = Path.Combine(_root, "src-real");
        var target = Path.Combine(_root, "dst-real");
        Directory.CreateDirectory(source);

        var entry = LinkCreator.CreateSymlink(source, target, note: "测试", registryRoot: _root);

        Assert.True(Directory.Exists(target));
        Assert.True(new DirectoryInfo(target).Attributes.HasFlag(FileAttributes.ReparsePoint));
        Assert.Equal(target, entry.Source);
        Assert.Equal(source, entry.Target);
        Assert.Equal("symlink", entry.Type);

        var registry = LinkRegistry.Load(_root);
        Assert.Single(registry.Entries);
        Assert.Equal("symlink", registry.Entries[0].Type);
    }

    [SkippableFact]
    public void CreateSymlink_rolls_back_when_registry_is_corrupt()
    {
        Skip.IfNot(CanCreateSymlinks(), "环境无 symlink 权限（未开开发者模式或非 Windows）");

        var source = Path.Combine(_root, "src-rb2");
        var target = Path.Combine(_root, "dst-rb2");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(Path.Combine(_root, LinkRegistry.DefaultFolderName));
        File.WriteAllText(Path.Combine(_root, LinkRegistry.DefaultFolderName, LinkRegistry.DefaultFileName), "{ not json");

        Assert.Throws<RegistryCorruptException>(() =>
            LinkCreator.CreateSymlink(source, target, note: "", registryRoot: _root));
        Assert.False(Directory.Exists(target));
    }

    [SkippableFact]
    public void Health_check_reports_ok_for_symlink()
    {
        Skip.IfNot(CanCreateSymlinks(), "环境无 symlink 权限（未开开发者模式或非 Windows）");

        var source = Path.Combine(_root, "src-h");
        var target = Path.Combine(_root, "dst-h");
        Directory.CreateDirectory(source);
        LinkCreator.CreateSymlink(source, target, note: "", registryRoot: _root);

        Assert.Equal(LinkStatus.Ok, LinkHealth.Check(target, "symlink"));
    }

    [SkippableFact]
    public void Health_check_reports_broken_when_symlink_target_deleted()
    {
        Skip.IfNot(CanCreateSymlinks(), "环境无 symlink 权限（未开开发者模式或非 Windows）");

        var source = Path.Combine(_root, "src-hb");
        var target = Path.Combine(_root, "dst-hb");
        Directory.CreateDirectory(source);
        LinkCreator.CreateSymlink(source, target, note: "", registryRoot: _root);
        Directory.Delete(source);

        Assert.Equal(LinkStatus.Broken, LinkHealth.Check(target, "symlink"));
    }
}
