using LinkMgr.Core;
using LinkMgr.Core.Health;
using LinkMgr.Core.Registry;

namespace LinkMgr.Core.Tests;

public class LinkHealthTests : IDisposable
{
    private readonly string _root;

    public LinkHealthTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"linkmgr-health-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string NewTargetDir(string name)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private void NewJunction(string source, string target)
    {
        // Windows junction：PowerShell New-Item，无需特权（mklink 经 Process.Start 传参在 CI 下不稳定）
        var psi = new System.Diagnostics.ProcessStartInfo(
            "powershell.exe",
            $"-NoProfile -Command \"(New-Item -ItemType Junction -Path '{source}' -Target '{target}') | Out-Null\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = System.Diagnostics.Process.Start(psi)!;
        p.WaitForExit(15000);
        Assert.True(Directory.Exists(source), $"junction 创建失败: {source}");
    }

    private string NewSourceDir(string name) => Path.Combine(_root, name);

    [Fact]
    public void Healthy_junction_reports_ok()
    {
        var target = NewTargetDir("real-target");
        var source = NewSourceDir("link-ok");
        NewJunction(source, target);

        var status = LinkHealth.Check(source, "junction");

        Assert.Equal(LinkStatus.Ok, status);
    }

    [Fact]
    public void Junction_with_deleted_target_reports_broken()
    {
        var target = NewTargetDir("doomed-target");
        var source = NewSourceDir("link-broken");
        NewJunction(source, target);
        Directory.Delete(target);

        var status = LinkHealth.Check(source, "junction");

        Assert.Equal(LinkStatus.Broken, status);
    }

    [Fact]
    public void Missing_link_reports_missing()
    {
        var source = NewSourceDir("never-created");

        var status = LinkHealth.Check(source, "junction");

        Assert.Equal(LinkStatus.Missing, status);
    }

    [Fact]
    public void Plain_directory_registered_as_link_reports_disguised()
    {
        var source = NewSourceDir("plain-dir");
        Directory.CreateDirectory(source);

        var status = LinkHealth.Check(source, "junction");

        Assert.Equal(LinkStatus.Disguised, status);
    }
}
