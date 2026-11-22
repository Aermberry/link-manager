using LinkManager.Core.Registry;
using LinkManager.Core.Scan;

namespace LinkManager.Core.Tests;

/// <summary>
/// 扫描结果导入（issue #15）：候选 → 登记表。导入时再次查重（扫描与导入之间
/// 登记表可能变化），已登记的跳过且不产生重复条目。
/// </summary>
public class LinkImporterTests : IDisposable
{
    private readonly string _root;

    public LinkImporterTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"link-manager-import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static ScannedLink Candidate(string source, string target, string type = "junction") =>
        new() { Source = source, Target = target, Type = type };

    private ScanResult ResultOf(params ScannedLink[] links) => new() { Links = [.. links] };

    [Fact]
    public void Import_registers_every_candidate_and_saves()
    {
        var result = ResultOf(
            Candidate(@"C:\链接处", @"F:\真实目录"),
            Candidate(@"C:\另一个", @"F:\另一个真实", type: "symlink"));

        var registry = LinkRegistry.Load(_root);
        var imported = LinkImporter.Import(result, registry);

        Assert.Equal(2, imported);
        Assert.Equal(2, registry.Entries.Count);
        Assert.Equal(@"C:\链接处", registry.Entries[0].Source);
        Assert.Equal(@"F:\真实目录", registry.Entries[0].Target);
        Assert.Equal("symlink", registry.Entries[1].Type);

        // 已持久化（新实例读盘可见）
        Assert.Equal(2, LinkRegistry.Load(_root).Entries.Count);
    }

    [Fact]
    public void Import_skips_candidates_registered_between_scan_and_import()
    {
        var registry = LinkRegistry.Load(_root);
        registry.Add(new LinkEntry { Source = @"C:\已登记", Target = @"F:\真实", Type = "junction" });
        registry.Save();

        var result = ResultOf(
            Candidate(@"C:\已登记", @"F:\真实"),
            Candidate(@"C:\新链接", @"F:\新真实"));

        var imported = LinkImporter.Import(result, registry);

        Assert.Equal(1, imported);
        var entries = LinkRegistry.Load(_root).Entries;
        Assert.Equal(2, entries.Count);
        Assert.Single(entries, e => e.Source == @"C:\已登记");
    }

    [Fact]
    public void Import_one_registers_a_single_candidate()
    {
        // 交互模式逐条问询「要登记吗」→ 用户答是 → 导入这一条
        var registry = LinkRegistry.Load(_root);
        var candidate = Candidate(@"C:\链接处", @"F:\真实目录");

        var imported = LinkImporter.ImportOne(candidate, registry);

        Assert.True(imported);
        var entry = Assert.Single(registry.Entries);
        Assert.Equal(@"C:\链接处", entry.Source);
        Assert.Equal(@"F:\真实目录", entry.Target);
        Assert.Equal("junction", entry.Type);
        Assert.Equal("", entry.Note);
    }

    [Fact]
    public void Import_one_refuses_an_already_registered_source()
    {
        var registry = LinkRegistry.Load(_root);
        registry.Add(new LinkEntry { Source = @"C:\链接处", Target = @"F:\旧目标", Type = "junction" });
        registry.Save();

        var imported = LinkImporter.ImportOne(Candidate(@"C:\链接处", @"F:\新目标"), registry);

        Assert.False(imported);
        // 不覆盖已有条目（不重复登记）
        var entry = Assert.Single(registry.Entries);
        Assert.Equal(@"F:\旧目标", entry.Target);
    }

    [Fact]
    public void Import_persists_each_entry_so_a_later_failure_keeps_earlier_ones()
    {
        // 逐条落盘：中途失败（坏路径等）不丢已导入的条目
        var registry = LinkRegistry.Load(_root);
        var result = ResultOf(
            Candidate(@"C:\第一个", @"F:\真实1"),
            Candidate(@"C:\第二个", @"F:\真实2"));

        LinkImporter.Import(result, registry);

        var after = LinkRegistry.Load(_root);
        Assert.Equal(2, after.Entries.Count);
    }
}
