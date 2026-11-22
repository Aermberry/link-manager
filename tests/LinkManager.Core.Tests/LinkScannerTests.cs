using LinkManager.Core.Registry;
using LinkManager.Core.Scan;

namespace LinkManager.Core.Tests;

/// <summary>
/// 链接扫描器：对真实临时目录测试发现、深度控制、无权限跳过与去重（issue #15）。
/// 测试用 junction 经真创建（FileSystemInfo.CreateAsJunction / mklink），不触真实 %APPDATA%。
/// </summary>
public class LinkScannerTests : IDisposable
{
    private readonly string _root;

    public LinkScannerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"link-manager-scan-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    /// <summary>
    /// 在 parent 下建一个真实 junction（parent 自身必须已存在）。
    /// 用 cmd mklink /J 直接创建，不经 LinkCreator——模拟"不是本工具创建的存量链接"，
    /// 无登记副作用（LinkCreator 会自动登记，污染去重断言）。
    /// </summary>
    private static string NewJunction(string parent, string name, string realDir)
    {
        Directory.CreateDirectory(realDir);
        var link = Path.Combine(parent, name);
        var psi = new System.Diagnostics.ProcessStartInfo(
            "cmd.exe", $"/c mklink /J \"{link}\" \"{realDir}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = System.Diagnostics.Process.Start(psi)!;
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0 && Directory.Exists(link),
            $"mklink /J 失败：{link} → {realDir}。原因：{stderr}");
        return link;
    }

    /// <summary>在 parent 下建一个真实目录 symlink（mklink /D，开发者模式无需特权）。失败断言（本环境应可用）。</summary>
    private static string NewDirSymlink(string parent, string name, string realDir)
    {
        Directory.CreateDirectory(realDir);
        var link = Path.Combine(parent, name);
        var psi = new System.Diagnostics.ProcessStartInfo(
            "cmd.exe", $"/c mklink /D \"{link}\" \"{realDir}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = System.Diagnostics.Process.Start(psi)!;
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0 && Directory.Exists(link),
            $"mklink /D 失败：{link} → {realDir}。原因：{stderr}");
        return link;
    }

    /// <summary>把 source 直接写进登记表（模拟"此前已登记"）。</summary>
    private void Register(string source, string target, string type = "junction")
    {
        var registry = LinkRegistry.Load(_root);
        registry.Add(new LinkEntry { Source = source, Target = target, Type = type });
        registry.Save();
    }

    [Fact]
    public void Scan_finds_an_unregistered_junction()
    {
        var real = Path.Combine(_root, "real");
        var link = NewJunction(_root, "link", real);

        var result = LinkScanner.Scan(_root, LinkRegistry.Load(_root));

        var found = Assert.Single(result.Links);
        Assert.Equal(link, found.Source, ignoreCase: true);
        Assert.Equal(real, found.Target, ignoreCase: true);
        Assert.Equal("junction", found.Type);
    }

    [Fact]
    public void Scan_excludes_registered_links_from_candidates()
    {
        var real = Path.Combine(_root, "real");
        var link = NewJunction(_root, "registered", real);
        var link2 = NewJunction(_root, "fresh", Path.Combine(_root, "real2"));

        Register(link, real);

        var result = LinkScanner.Scan(_root, LinkRegistry.Load(_root));

        // 已登记的不作为候选出现；未登记的仍被发现
        var candidate = Assert.Single(result.Links);
        Assert.Equal(link2, candidate.Source, ignoreCase: true);
        Assert.Equal(1, result.RegisteredCount);
    }

    [Fact]
    public void Scan_dedup_is_case_insensitive_against_stored_entries()
    {
        // 登记表约定存反斜杠（MatchesSource 契约）；这里用不同大小写模拟登记时的路径差异
        var real = Path.Combine(_root, "real");
        var link = NewJunction(_root, "Link", real);

        Register(link.ToUpperInvariant(), real);

        var result = LinkScanner.Scan(_root, LinkRegistry.Load(_root));

        Assert.Empty(result.Links);
        Assert.Equal(1, result.RegisteredCount);
    }

    [Fact]
    public void Scan_without_registry_reports_all_links_as_candidates()
    {
        // Core API 要可被 GUI 直接调用：先扫后问询（登记时才建表）
        NewJunction(_root, "l1", Path.Combine(_root, "r1"));

        var result = LinkScanner.Scan(_root);

        Assert.Single(result.Links);
    }

    [Fact]
    public void Scan_respects_max_depth()
    {
        // 深度语义：depth=0 只扫根一层；嵌套更深的链接不出现
        Directory.CreateDirectory(Path.Combine(_root, "a", "b"));
        NewJunction(Path.Combine(_root, "a", "b"), "deep", Path.Combine(_root, "real-deep"));
        NewJunction(_root, "shallow", Path.Combine(_root, "real-shallow"));

        var shallowOnly = LinkScanner.Scan(_root, maxDepth: 0);
        var deepLink = Assert.Single(shallowOnly.Links);
        Assert.EndsWith("shallow", deepLink.Source, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, shallowOnly.ScannedDirectoryCount);

        var withDeep = LinkScanner.Scan(_root, maxDepth: 2);
        Assert.Equal(2, withDeep.Links.Count);
    }

    [Fact]
    public void Scan_finds_multiple_links_and_reports_count()
    {
        NewJunction(_root, "l1", Path.Combine(_root, "r1"));
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        NewJunction(Path.Combine(_root, "sub"), "l2", Path.Combine(_root, "r2"));

        var result = LinkScanner.Scan(_root);

        Assert.Equal(2, result.Links.Count);
        // 枚举过的目录：根 + r1 + sub + r2（junction 本身不入栈）
        Assert.Equal(4, result.ScannedDirectoryCount);
    }

    [Fact]
    public void Scan_on_empty_root_reports_no_candidates()
    {
        var result = LinkScanner.Scan(_root);

        Assert.Empty(result.Links);
        Assert.Empty(result.Skipped);
        Assert.Equal(0, result.RegisteredCount);
    }

    [Fact]
    public void Scan_on_missing_root_returns_empty_result_without_throwing()
    {
        var missing = Path.Combine(_root, "no-such-dir");

        var result = LinkScanner.Scan(missing);

        Assert.Empty(result.Links);
    }

    [Fact]
    public void Scan_skips_unreadable_directory_and_reports_it()
    {
        // 真实 ACL 拒绝枚举：子目录里的链接不可见，目录进 Skipped，其余部分继续扫
        // RD（List Directory）拒绝对自身生效——锁住 dir 自身的枚举
        var locked = Path.Combine(_root, "locked");
        Directory.CreateDirectory(Path.Combine(locked, "inner"));
        NewJunction(Path.Combine(locked, "inner"), "hidden", Path.Combine(_root, "real-hidden"));
        NewJunction(_root, "visible", Path.Combine(_root, "real-visible"));

        var result = ScanWithDeniedAccess(locked);

        try
        {
            Assert.DoesNotContain(result.Links, l => l.Source.Contains("hidden", StringComparison.OrdinalIgnoreCase));
            var skipped = Assert.Single(result.Skipped);
            Assert.Equal(locked, skipped, ignoreCase: true);
            var visible = Assert.Single(result.Links);
            Assert.EndsWith("visible", visible.Source, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            // 先恢复访问再清理，否则 Dispose 的递归删除失败
            GrantFullControl(locked);
        }
    }

    [Fact]
    public void Scan_on_fully_locked_root_reports_root_as_skipped()
    {
        var result = ScanWithDeniedAccess(_root);

        try
        {
            Assert.Empty(result.Links);
            Assert.Single(result.Skipped);
        }
        finally
        {
            GrantFullControl(_root);
        }
    }

    /// <summary>拒绝当前用户枚举 dir（RD = List Directory），返回拒绝是否真的生效。
    /// 主体用 *SID 形式——裸用户名会被 icacls 解析成孤立主体（CC\ ），deny 不生效。</summary>
    private static bool DenyEnumeration(string dir)
    {
        var sid = System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
        RunIcacls(dir, $"/deny *{sid}:(RD)");
        // 探针：确认拒绝真的生效（管理员/备份语义进程会绕过，此时测试自动跳过）
        try
        {
            _ = Directory.EnumerateFileSystemEntries(dir).Any();
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>对 dir 施加 RD 拒绝后扫描 _root；拒绝不生效（管理员/备份语义）则整测跳过。</summary>
    private ScanResult ScanWithDeniedAccess(string dir)
    {
        if (!DenyEnumeration(dir))
        {
            GrantFullControl(dir);
            Skip.If(true, "当前进程（管理员/备份语义）不受 RD 拒绝影响，无法构造无权限环境");
        }

        return LinkScanner.Scan(_root, LinkRegistry.Load(_root));
    }

    private static void GrantFullControl(string dir)
    {
        var sid = System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
        RunIcacls(dir, $"/remove:d *{sid}");
    }

    private static void RunIcacls(string dir, string arguments)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(
            "icacls.exe", $"\"{dir}\" {arguments}")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = System.Diagnostics.Process.Start(psi)!;
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, $"icacls {arguments} 失败：{stderr}");
    }
    [Fact]
    public void Scan_classifies_directory_symlink_as_symlink_not_junction()
    {
        // 回归：目录 symlink 曾被按"目录 reparse point => junction"误归类（review finding）
        var real = Path.Combine(_root, "real-ds");
        var link = NewDirSymlink(_root, "dirsym", real);

        var result = LinkScanner.Scan(_root);

        var found = Assert.Single(result.Links);
        Assert.Equal("symlink", found.Type);
        Assert.Equal(real, found.Target, ignoreCase: true);
    }
}
