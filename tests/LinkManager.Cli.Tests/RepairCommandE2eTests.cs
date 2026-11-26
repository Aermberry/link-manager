using LinkManager.Core.Registry;

namespace LinkManager.Cli.Tests;

public class RepairCommandE2eTests : IDisposable
{
    private readonly string _tree = CliE2e.NewSandbox("link-manager-repair-tree");
    private readonly string _registryRoot = CliE2e.NewSandbox("link-manager-repair-registry");

    public void Dispose()
    {
        try { Directory.Delete(_tree, recursive: true); } catch (IOException) { }
        try { Directory.Delete(_registryRoot, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Repair_recreates_registered_broken_junction_and_returns_json_envelope()
    {
        var target = Path.Combine(_tree, "real");
        Directory.CreateDirectory(target);
        var source = Path.Combine(_tree, "link");
        var registry = LinkRegistry.Load(_registryRoot);
        registry.Add(new LinkEntry { Source = source, Target = target, Type = "junction" });
        registry.Save();

        var run = CliE2e.Run("repair", source, "--registry", _registryRoot, "--json");

        Assert.True(run.ExitCode is 0 or 2,
            $"修复应成功，或因当前环境受监控进程阻碍而拒绝：exit={run.ExitCode}, stdout={run.Stdout}, stderr={run.Stderr}");
        using var doc = run.Json();
        if (run.ExitCode == 2)
        {
            Assert.Equal("execution_failed", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
            Assert.Contains("相关进程仍在运行", doc.RootElement.GetProperty("error").GetProperty("message").GetString());
            return;
        }

        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("repair", doc.RootElement.GetProperty("command").GetString());
        Assert.Equal("ok", doc.RootElement.GetProperty("data").GetProperty("status").GetString());
        Assert.True(Directory.Exists(source));
    }

    [Fact]
    public void Repair_unknown_entry_returns_execution_failed_json()
    {
        var run = CliE2e.Run("repair", Path.Combine(_tree, "not-registered"), "--registry", _registryRoot, "--json");

        Assert.Equal(2, run.ExitCode);
        using var doc = run.Json();
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("repair", doc.RootElement.GetProperty("command").GetString());
        Assert.Equal("execution_failed", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void Repair_without_source_returns_usage_exit_code()
    {
        var run = CliE2e.Run("repair", "--registry", _registryRoot, "--json");

        Assert.Equal(1, run.ExitCode);
        using var doc = run.Json();
        Assert.Equal("usage", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
    }
}
