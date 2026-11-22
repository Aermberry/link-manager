using LinkManager.Core.Links;
using LinkManager.Core.Registry;

namespace LinkManager.Core.Tests;

/// <summary>
/// 删除路径测试：真实临时目录 + 真实 junction（走 LinkCreator 建），验收工单四条删除标准。
/// 全部路径显式 registryRoot，不触产品默认位置。
/// </summary>
public class LinkRemoverTests : IDisposable
{
    private readonly string _root;

    public LinkRemoverTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"link-manager-rm-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    /// <summary>建一个真实 junction 并登记，返回登记表。</summary>
    private LinkRegistry SeedWithJunction(string source, string target, string note = "")
    {
        Directory.CreateDirectory(source);
        LinkCreator.CreateJunction(source, target, note, registryRoot: _root);
        return LinkRegistry.Load(rootDirectory: _root);
    }

    // ---------- 仅删登记 ----------

    [Fact]
    public void RemoveEntryOnly_unregisters_but_keeps_the_filesystem_link()
    {
        var source = Path.Combine(_root, "src1");
        var target = Path.Combine(_root, "data1");
        var registry = SeedWithJunction(source, target);

        var removed = LinkRemover.RemoveEntryOnly(registry, target);

        Assert.True(removed);
        // 链接仍在文件系统上且可用
        Assert.True(Directory.Exists(target));
        Assert.True(new DirectoryInfo(target).Attributes.HasFlag(FileAttributes.ReparsePoint));
        Assert.True(Directory.Exists(source));
        // 登记已删
        Assert.Empty(LinkRegistry.Load(rootDirectory: _root).Entries);
    }

    [Fact]
    public void RemoveEntryOnly_returns_false_for_an_unknown_source()
    {
        var registry = LinkRegistry.Load(rootDirectory: _root);

        Assert.False(LinkRemover.RemoveEntryOnly(registry, Path.Combine(_root, "no-such")));
    }

    // ---------- 连同链接一起删 ----------

    [Fact]
    public void RemoveWithLink_without_explicit_confirm_is_refused()
    {
        var source = Path.Combine(_root, "src2");
        var target = Path.Combine(_root, "data2");
        var registry = SeedWithJunction(source, target);

        var ex = Assert.Throws<LinkRemovalException>(() =>
            LinkRemover.RemoveWithLink(registry, target, confirmDeleteLink: false));

        Assert.Contains("确认", ex.Message);
        // 什么都没动
        Assert.True(Directory.Exists(target));
        Assert.Single(LinkRegistry.Load(rootDirectory: _root).Entries);
    }

    [Fact]
    public void RemoveWithLink_confirmed_deletes_the_link_and_unregisters_but_keeps_real_data()
    {
        var source = Path.Combine(_root, "src3");
        var target = Path.Combine(_root, "data3");
        File.WriteAllText(Path.Combine(_root, "payload-marker"), "sentinel");
        var registry = SeedWithJunction(source, target);

        var removed = LinkRemover.RemoveWithLink(registry, target, confirmDeleteLink: true);

        Assert.True(removed);
        // 链接已删
        Assert.False(Directory.Exists(target));
        // 真实数据目录完好
        Assert.True(Directory.Exists(source));
        // 登记已删
        Assert.Empty(LinkRegistry.Load(rootDirectory: _root).Entries);
    }

    [Fact]
    public void RemoveWithLink_refuses_a_disguised_plain_directory()
    {
        // 登记条目指向普通目录（不是 reparse point）：拒绝删除，防误删真实数据
        var fakeLink = Path.Combine(_root, "fake-link");
        Directory.CreateDirectory(fakeLink);
        var registry = LinkRegistry.Load(rootDirectory: _root);
        registry.Add(new LinkEntry
        {
            Source = fakeLink,
            Target = Path.Combine(_root, "somewhere"),
            Type = "junction",
        });
        registry.Save();

        var ex = Assert.Throws<LinkRemovalException>(() =>
            LinkRemover.RemoveWithLink(registry, fakeLink, confirmDeleteLink: true));

        Assert.Contains("不是链接", ex.Message);
        Assert.True(Directory.Exists(fakeLink));
        Assert.Single(LinkRegistry.Load(rootDirectory: _root).Entries);
    }

    [Fact]
    public void RemoveWithLink_returns_false_for_an_unknown_source()
    {
        var registry = LinkRegistry.Load(rootDirectory: _root);

        Assert.False(LinkRemover.RemoveWithLink(registry, Path.Combine(_root, "ghost"), confirmDeleteLink: true));
    }

    [Fact]
    public void RemoveWithLink_removes_only_the_requested_entry()
    {
        var sourceA = Path.Combine(_root, "srcA");
        var targetA = Path.Combine(_root, "dataA");
        var sourceB = Path.Combine(_root, "srcB");
        var targetB = Path.Combine(_root, "dataB");
        SeedWithJunction(sourceA, targetA);
        SeedWithJunction(sourceB, targetB);
        var registry = LinkRegistry.Load(rootDirectory: _root);

        LinkRemover.RemoveWithLink(registry, targetA, confirmDeleteLink: true);

        var remaining = LinkRegistry.Load(rootDirectory: _root);
        var entry = Assert.Single(remaining.Entries);
        Assert.Equal(targetB, entry.Source);
        Assert.True(Directory.Exists(targetB));
        Assert.False(Directory.Exists(targetA));
    }
}

public class LinkRemoverMissingLinkTests : IDisposable
{
    private readonly string _root;

    public LinkRemoverMissingLinkTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"link-manager-rmmiss-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void RemoveWithLink_on_a_missing_link_still_unregisters()
    {
        // 登记的链接路径已不存在（用户手工删了链接）：确认删除应清理登记而非报错
        var registry = LinkRegistry.Load(rootDirectory: _root);
        registry.Add(new LinkEntry { Source = Path.Combine(_root, "gone-link"), Target = @"F:\Data", Type = "junction" });
        registry.Save();

        var removed = LinkRemover.RemoveWithLink(registry, Path.Combine(_root, "gone-link"), confirmDeleteLink: true);

        Assert.True(removed);
        Assert.Empty(LinkRegistry.Load(rootDirectory: _root).Entries);
    }
}
