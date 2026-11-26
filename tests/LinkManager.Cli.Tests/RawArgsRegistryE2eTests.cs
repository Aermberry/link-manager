using LinkManager.Core.Registry;

namespace LinkManager.Cli.Tests;

/// <summary>
/// raw-args 回归（T5 收尾发现）：note/config/remove/migrate 曾绕过全局解析器直接读 args，
/// 全局 --registry（沙箱隔离）与 --json 对它们不生效。约定（docs/cli-conventions.md）：
/// --registry 放在命令名之后的任意位置都须生效。
/// </summary>
public class RawArgsRegistryE2eTests : IDisposable
{
    private readonly string _tree;
    private readonly string _registryRoot;

    public RawArgsRegistryE2eTests()
    {
        _tree = CliE2e.NewSandbox("link-manager-raw-tree");
        _registryRoot = CliE2e.NewSandbox("link-manager-raw-reg");
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

    /// <summary>在沙箱登记表登记一条指向真实 junction 的条目，返回源路径。</summary>
    private string SeedRegistry()
    {
        var source = CliE2e.NewJunction(_tree, "link", Path.Combine(_tree, "real"));
        var registry = LinkRegistry.Load(_registryRoot);
        registry.Add(new LinkEntry { Source = source, Target = Path.Combine(_tree, "real"), Type = "junction" });
        registry.Save();
        return source;
    }

    [Fact]
    public void Note_updates_entry_in_the_sandbox_registry()
    {
        // 回归：note 曾用 LinkRegistry.Load()（默认位置）——--registry 被无视；
        // 更糟：note <源> <备注> 形态下 --registry 会被当成备注文本吞掉。
        var source = SeedRegistry();

        var run = CliE2e.Run("note", source, "测试备注", "--registry", _registryRoot, "--json");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("备注", run.Stdout);
        Assert.Equal("测试备注", Assert.Single(LinkRegistry.Load(_registryRoot).Entries).Note);
    }

    [Fact]
    public void Note_json_envelope_is_labeled_note_not_create()
    {
        // 回归（v1.0.6 实测发现）：note 的 --json 信封曾误标 "command":"create"
        // （RunNote 复用 CliJson.Create）。约定（docs/cli-conventions.md）：
        // 信封 command 字段 = 命令名，agent 按 .command 判读。
        var source = SeedRegistry();

        var run = CliE2e.Run("note", source, "测试备注", "--registry", _registryRoot, "--json");

        Assert.Equal(0, run.ExitCode);
        using var doc = run.Json();
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("note", doc.RootElement.GetProperty("command").GetString());
        Assert.Equal("测试备注", doc.RootElement.GetProperty("data").GetProperty("note").GetString());
    }

    [Fact]
    public void Remove_entry_only_respects_the_sandbox_registry()
    {
        // 回归：remove 曾用 LinkRegistry.Load()——沙箱 --registry 被无视，会删真实登记表条目
        var source = SeedRegistry();
        Assert.True(Directory.Exists(source), "前置：junction 应真实存在");

        var run = CliE2e.Run("remove", source, "--keep-link", "--registry", _registryRoot, "--json");

        Assert.Equal(0, run.ExitCode);
        // 沙箱登记表条目已删
        Assert.Empty(LinkRegistry.Load(_registryRoot).Entries);
        // --keep-link：文件系统链接仍在
        Assert.True(Directory.Exists(source));
    }

    [Fact]
    public void Config_set_registry_writes_settings_into_the_sandbox()
    {
        // 回归：config 曾用 LinkSettings.Load()（默认位置）——--registry 沙箱对它无效
        var settingsPath = Path.Combine(_registryRoot, LinkManager.Core.Settings.LinkSettings.DefaultFolderName,
            LinkManager.Core.Settings.LinkSettings.DefaultFileName);
        var newRegistryFile = Path.Combine(_registryRoot, "elsewhere", "registry.json");
        SeedRegistry();

        var run = CliE2e.Run("config", "set-registry", newRegistryFile, "--registry", _registryRoot, "--json");

        Assert.Equal(0, run.ExitCode);
        Assert.True(File.Exists(settingsPath), $"设置文件应落在沙箱：{settingsPath}");
        Assert.True(File.Exists(newRegistryFile), "登记表应已迁到新位置");
    }

    [Fact]
    public void Migrate_dry_run_honors_the_sandbox_registry_in_plan_output()
    {
        // migrate 默认 dry-run（只读不改盘）；曾绕过全局解析器——--registry 报"未知选项"退出 2。
        // 退出码不设断言：本机 magpie/OpenMausBot 在跑时计划被阻碍 → 2，否则 0（均合法）。
        // 回归断言：JSON 包络可用 + 计划里的登记表路径落在 --registry 沙箱。
        var src = Path.Combine(_tree, "m-src");
        var dst = Path.Combine(_tree, "m-dst");
        Directory.CreateDirectory(src);

        var run = CliE2e.Run("migrate", src, dst, "--registry", _registryRoot, "--json");

        Assert.True(run.ExitCode is 0 or 2, $"退出码应 0（无阻碍）或 2（进程阻碍）：实际 {run.ExitCode}，stderr：{run.Stderr}");
        using var doc = run.Json();
        var data = doc.RootElement.GetProperty("data");
        Assert.True(data.GetProperty("executed").GetBoolean() is false);
        var steps = data.GetProperty("steps").EnumerateArray().Select(s => s.GetString()!).ToList();
        Assert.Contains(steps, s => s.Contains(_registryRoot, StringComparison.OrdinalIgnoreCase));
    }
}
