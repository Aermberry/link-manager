using LinkMgr.Core.Links;
using LinkMgr.Core.Registry;

namespace LinkMgr.Core.Tests;

public class LinkCreatorTests : IDisposable
{
    private readonly string _root;

    public LinkCreatorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"linkmgr-create-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Create_makes_a_junction_and_returns_the_entry()
    {
        var source = Path.Combine(_root, "src");
        var target = Path.Combine(_root, "dst");
        Directory.CreateDirectory(source);

        var entry = LinkCreator.CreateJunction(source, target, note: "测试", registryRoot: _root);

        Assert.True(Directory.Exists(target));
        var info = new DirectoryInfo(target);
        Assert.Equal(FileAttributes.ReparsePoint, info.Attributes & FileAttributes.ReparsePoint);
        // 登记约定：Source 是链接路径，Target 是真实目录
        Assert.Equal(target, entry.Source);
        Assert.Equal(source, entry.Target);
        Assert.Equal("junction", entry.Type);
    }

    [Fact]
    public void Create_registers_the_new_link()
    {
        var source = Path.Combine(_root, "src2");
        var target = Path.Combine(_root, "dst2");
        Directory.CreateDirectory(source);

        LinkCreator.CreateJunction(source, target, note: "", registryRoot: _root);

        var registry = LinkRegistry.Load(_root);
        Assert.Single(registry.Entries);
    }

    [Fact]
    public void Create_rejects_missing_source_without_touching_filesystem()
    {
        var source = Path.Combine(_root, "no-such-src");
        var target = Path.Combine(_root, "dst3");

        var ex = Assert.Throws<LinkCreateException>(() => LinkCreator.CreateJunction(source, target, note: ""));

        Assert.Contains("源路径不存在", ex.Message);
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public void Create_rejects_nonempty_target_without_touching_filesystem()
    {
        var source = Path.Combine(_root, "src4");
        var target = Path.Combine(_root, "dst4");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "data.txt"), "precious");

        var ex = Assert.Throws<LinkCreateException>(() => LinkCreator.CreateJunction(source, target, note: ""));

        Assert.Contains("非空", ex.Message);
        Assert.True(File.Exists(Path.Combine(target, "data.txt")));
        Assert.False(new DirectoryInfo(target).Attributes.HasFlag(FileAttributes.ReparsePoint));
    }

    [Fact]
    public void Create_allows_empty_existing_target()
    {
        var source = Path.Combine(_root, "src5");
        var target = Path.Combine(_root, "dst5");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(target);

        LinkCreator.CreateJunction(source, target, note: "", registryRoot: _root);

        Assert.True(new DirectoryInfo(target).Attributes.HasFlag(FileAttributes.ReparsePoint));
    }

    [Fact]
    public void Create_rejects_target_nested_in_source()
    {
        // 防环：目标在源内部——junction 指向源，源内又含目标，必然成环
        var source = Path.Combine(_root, "outer");
        Directory.CreateDirectory(source);
        var target = Path.Combine(source, "inner");

        var ex = Assert.Throws<LinkCreateException>(() => LinkCreator.CreateJunction(source, target, note: ""));

        Assert.Contains("循环链接", ex.Message);
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public void Create_rejects_source_nested_in_target()
    {
        // 防环：源在目标内部——目标非空检查只是副产物，真根因是循环
        var target = Path.Combine(_root, "outer");
        Directory.CreateDirectory(target);
        var source = Path.Combine(target, "inner");
        Directory.CreateDirectory(source);

        var ex = Assert.Throws<LinkCreateException>(() => LinkCreator.CreateJunction(source, target, note: ""));

        Assert.Contains("循环链接", ex.Message);
        Assert.False(new DirectoryInfo(target).Attributes.HasFlag(FileAttributes.ReparsePoint));
    }
}
