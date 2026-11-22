using LinkManager.Core.Registry;
using LinkManager.Core.Scan;

namespace LinkManager.Cli.Tests;

/// <summary>
/// scan 子命令端到端（issue #15 验收）：真实 exe 子进程 + 真实 junction + --registry 沙箱。
/// stdin 是一条无数据的管道——非交互约定（docs/cli-conventions.md）：CLI 必须立即完成、
/// 明确退出码、绝不挂起等待输入。
/// </summary>
public class ScanCommandE2eTests : IDisposable
{
    private readonly string _tree;
    private readonly string _registryRoot;

    public ScanCommandE2eTests()
    {
        _tree = CliE2e.NewSandbox("link-manager-scan-tree");
        _registryRoot = CliE2e.NewSandbox("link-manager-scan-reg");
    }

    public void Dispose()
    {
        TryDelete(_tree);
        TryDelete(_registryRoot);
    }

    private static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Scan_with_piped_stdin_reports_candidates_imports_nothing_and_exits_0()
    {
        // 非交互降级（验收补测）：管道 stdin → 不问询、不导入、不挂起、退出码 0
        var link = CliE2e.NewJunction(_tree, "link", Path.Combine(_tree, "real"));

        var run = CliE2e.Run("scan", _tree, "--json", "--registry", _registryRoot);

        Assert.Equal(0, run.ExitCode);
        using var doc = run.Json();
        var data = doc.RootElement.GetProperty("data");
        var found = Assert.Single(data.GetProperty("links").EnumerateArray());
        Assert.Equal(link, found.GetProperty("source").GetString(), ignoreCase: true);
        Assert.Equal(0, data.GetProperty("summary").GetProperty("importedCount").GetInt32());

        // 降级提示进 stderr（stdout 只有一行 JSON 包络）
        Assert.Contains("stdin", run.Stderr);

        // 登记表为空：候选一条都没登记
        Assert.Empty(LinkRegistry.Load(_registryRoot).Entries);
    }

    [Fact]
    public void Scan_yes_imports_candidates_noninteractively()
    {
        // 全量导入（验收：CLI 非交互模式可全量导入，跳过问询）
        var link = CliE2e.NewJunction(_tree, "imported", Path.Combine(_tree, "real"));

        var run = CliE2e.Run("scan", _tree, "--yes", "--json", "--registry", _registryRoot);

        Assert.Equal(0, run.ExitCode);
        using var doc = run.Json();
        var summary = doc.RootElement.GetProperty("data").GetProperty("summary");
        Assert.Equal(1, summary.GetProperty("importedCount").GetInt32());

        var entry = Assert.Single(LinkRegistry.Load(_registryRoot).Entries);
        Assert.Equal(link, entry.Source, ignoreCase: true);
    }

    [Fact]
    public void Scan_yes_second_run_finds_no_new_candidates_and_never_duplicates()
    {
        // 已登记的链接不重复登记（验收）：第一次导入后，第二次扫描应报 0 候选 + registeredCount=1
        CliE2e.NewJunction(_tree, "once", Path.Combine(_tree, "real"));
        Assert.Equal(0, CliE2e.Run("scan", _tree, "--yes", "--json", "--registry", _registryRoot).ExitCode);

        var second = CliE2e.Run("scan", _tree, "--yes", "--json", "--registry", _registryRoot);

        Assert.Equal(0, second.ExitCode);
        using var doc = second.Json();
        var data = doc.RootElement.GetProperty("data");
        Assert.Empty(data.GetProperty("links").EnumerateArray());
        var summary = data.GetProperty("summary");
        Assert.Equal(0, summary.GetProperty("importedCount").GetInt32());
        Assert.Equal(1, summary.GetProperty("registeredCount").GetInt32());
        Assert.Single(LinkRegistry.Load(_registryRoot).Entries);
    }

    [Fact]
    public void Scan_max_depth_limits_discovery_and_invalid_value_is_a_user_error()
    {
        // 深度可控（验收）：--max-depth 0 只扫第一层；坏值退出码 1（usage）
        Directory.CreateDirectory(Path.Combine(_tree, "a", "b"));
        CliE2e.NewJunction(Path.Combine(_tree, "a", "b"), "deep", Path.Combine(_tree, "rd"));
        CliE2e.NewJunction(_tree, "shallow", Path.Combine(_tree, "rs"));

        var shallow = CliE2e.Run("scan", _tree, "--max-depth", "0", "--json", "--registry", _registryRoot);

        Assert.Equal(0, shallow.ExitCode);
        using (var doc = shallow.Json())
        {
            var links = doc.RootElement.GetProperty("data").GetProperty("links").EnumerateArray().ToList();
            var found = Assert.Single(links);
            Assert.EndsWith("shallow", found.GetProperty("source").GetString(), StringComparison.OrdinalIgnoreCase);
        }

        var all = CliE2e.Run("scan", _tree, "--json", "--registry", _registryRoot);
        Assert.Equal(0, all.ExitCode);
        using (var doc = all.Json())
        {
            Assert.Equal(2, doc.RootElement.GetProperty("data").GetProperty("summary").GetProperty("candidateCount").GetInt32());
        }

        var bad = CliE2e.Run("scan", _tree, "--max-depth", "abc", "--json", "--registry", _registryRoot);
        Assert.Equal(1, bad.ExitCode);
        using (var doc = bad.Json())
        {
            Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        }
    }
}
