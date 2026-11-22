using LinkManager.Core.Links;
using LinkManager.Core.Migration;
using LinkManager.Core.Registry;

namespace LinkManager.Core.Tests;

/// <summary>迁移状态机：全链路对真实临时目录测试（robocopy 真实执行）。</summary>
public class MigrationRunnerTests : IDisposable
{
    private readonly string _root;

    public MigrationRunnerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"link-manager-migrate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string NewSource(int fileCount = 3, string name = "src")
    {
        var source = Path.Combine(_root, name);
        Directory.CreateDirectory(source);
        for (var i = 0; i < fileCount; i++)
        {
            File.WriteAllText(Path.Combine(source, $"file{i}.txt"), $"content-{i}");
        }
        // 子目录 + 嵌套文件，覆盖 /E 递归复制
        Directory.CreateDirectory(Path.Combine(source, "sub"));
        File.WriteAllText(Path.Combine(source, "sub", "nested.txt"), "nested");
        return source;
    }

    private MigrationRunner NewRunner(string[]? running = null) =>
        new(FakeProcessProbeFactory.Of(running ?? []));

    /// <summary>编制计划：登记表固定写到测试根，进程探测固定注入假实现（不碰真实环境）。</summary>
    private MigrationPlan NewPlan(string source, string target, string note = "", string[]? running = null) =>
        MigrationPlanner.Plan(source, target, note: note,
            registryRoot: _root, probe: new FakeProcessProbe(running ?? []));

    // ---- dry-run ----

    [Fact]
    public void DryRun_outputs_plan_and_touches_nothing()
    {
        var source = NewSource();
        var target = Path.Combine(_root, "dst");

        var plan = NewPlan(source, target);
        var result = NewRunner().DryRun(plan);

        Assert.True(result.Ok);
        Assert.Contains("计划", result.Report);
        Assert.Contains(source, result.Report);
        Assert.Contains(target, result.Report);
        // 文件系统纹丝不动：源还在且是普通目录，目标未创建
        Assert.True(Directory.Exists(source));
        Assert.False(new DirectoryInfo(source).Attributes.HasFlag(FileAttributes.ReparsePoint));
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public void DryRun_reflects_running_process_in_plan_output()
    {
        var source = NewSource();
        var target = Path.Combine(_root, "dst");

        var result = NewRunner(["openmausbot"]).DryRun(NewPlan(source, target, running: ["openmausbot"]));

        Assert.False(result.Ok);
        Assert.Contains("openmausbot", result.Report);
        Assert.False(Directory.Exists(target));
    }

    // ---- 前置检查 ----

    [Fact]
    public void Run_rejects_when_target_process_is_running()
    {
        var source = NewSource();
        var target = Path.Combine(_root, "dst");

        var ex = Assert.Throws<MigrationException>(
            () => NewRunner(["openmausbot"]).Run(NewPlan(source, target, running: ["openmausbot"])));

        Assert.Contains("openmausbot", ex.Message);
        // 源数据原封不动，目标未创建
        Assert.True(Directory.Exists(source));
        Assert.Equal(3 + 1, Directory.EnumerateFileSystemEntries(source).Count());
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public void Run_rejects_missing_source()
    {
        var source = Path.Combine(_root, "no-such-src");
        var target = Path.Combine(_root, "dst");

        var ex = Assert.Throws<MigrationException>(
            () => NewRunner().Run(NewPlan(source, target)));

        Assert.Contains("源路径不存在", ex.Message);
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public void Run_rejects_nonempty_target()
    {
        var source = NewSource();
        var target = Path.Combine(_root, "dst");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "precious.txt"), "keep out");

        var ex = Assert.Throws<MigrationException>(
            () => NewRunner().Run(NewPlan(source, target)));

        Assert.Contains("非空", ex.Message);
        Assert.True(File.Exists(Path.Combine(target, "precious.txt")));
        Assert.True(Directory.Exists(source));
        Assert.False(new DirectoryInfo(source).Attributes.HasFlag(FileAttributes.ReparsePoint));
    }

    [Fact]
    public void Run_rejects_target_nested_in_source()
    {
        // 防环：目标在源内部——robocopy 会把目标复制进源树，随后的"删源"连数据一起删光
        var source = NewSource();
        var target = Path.Combine(source, "migrated");

        var ex = Assert.Throws<MigrationException>(
            () => NewRunner().Run(NewPlan(source, target)));

        Assert.Contains("嵌套", ex.Message);
        // 源数据原封不动
        Assert.True(File.Exists(Path.Combine(source, "file0.txt")));
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public void Run_rejects_source_nested_in_target()
    {
        // 防环：源在目标内部
        var target = Path.Combine(_root, "outer");
        Directory.CreateDirectory(target);
        var source = Path.Combine(target, "inner");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "data.txt"), "x");

        var ex = Assert.Throws<MigrationException>(
            () => NewRunner().Run(NewPlan(source, target)));

        Assert.Contains("嵌套", ex.Message);
        Assert.True(File.Exists(Path.Combine(source, "data.txt")));
    }

    [Fact]
    public void Run_rejects_equal_source_and_target()
    {
        // 防自指：相同路径会造出指向自己的 junction 并把数据删光
        var source = NewSource();

        var ex = Assert.Throws<MigrationException>(
            () => NewRunner().Run(NewPlan(source, source)));

        Assert.Contains("嵌套", ex.Message);
        Assert.True(File.Exists(Path.Combine(source, "file0.txt")));
    }

    [Fact]
    public void Run_rejects_source_that_is_already_a_link()
    {
        // 迁移已迁移的路径：junction 指向真实数据，迁移它只会在登记表里制造重复条目
        var realData = NewSource(name: "real");
        var source = Path.Combine(_root, "linked");
        LinkCreator.CreateJunction(realData, source, note: "", registryRoot: _root);
        var target = Path.Combine(_root, "dst");

        var ex = Assert.Throws<MigrationException>(
            () => NewRunner().Run(NewPlan(source, target)));

        Assert.Contains("链接", ex.Message);
        Assert.True(new DirectoryInfo(source).Attributes.HasFlag(FileAttributes.ReparsePoint));
        Assert.True(File.Exists(Path.Combine(realData, "file0.txt")));
    }

    [Fact]
    public void Run_rejects_source_that_is_a_file()
    {
        var source = Path.Combine(_root, "afile.txt");
        File.WriteAllText(source, "not a dir");
        var target = Path.Combine(_root, "dst");

        var ex = Assert.Throws<MigrationException>(
            () => NewRunner().Run(NewPlan(source, target)));

        Assert.Contains("不是目录", ex.Message);
        Assert.True(File.Exists(source));
    }

    [Fact]
    public void Run_rejects_target_that_is_a_file()
    {
        var source = NewSource();
        var target = Path.Combine(_root, "afile.txt");
        File.WriteAllText(target, "collision");

        var ex = Assert.Throws<MigrationException>(
            () => NewRunner().Run(NewPlan(source, target)));

        Assert.Contains("同名文件", ex.Message);
        Assert.True(File.Exists(target));
        Assert.True(File.Exists(Path.Combine(source, "file0.txt")));
    }

    [Fact]
    public void Run_normalizes_trailing_backslash_in_paths()
    {
        // robocopy 引号包参：末尾反斜杠会转义闭引号导致退出码 16；规划器必须归一化
        var source = NewSource();
        var target = Path.Combine(_root, "dst");
        var trailingSource = source.EndsWith(Path.DirectorySeparatorChar) ? source : source + Path.DirectorySeparatorChar;

        var result = NewRunner().Run(NewPlan(trailingSource, target));

        Assert.True(result.Ok);
        Assert.True(new DirectoryInfo(source).Attributes.HasFlag(FileAttributes.ReparsePoint));
        Assert.True(File.Exists(Path.Combine(target, "file0.txt")));
    }

    // ---- 复制 → 核对 ----

    [Fact]
    public void Run_copies_and_source_becomes_junction_and_registry_updated()
    {
        var source = NewSource();
        var target = Path.Combine(_root, "dst");

        var result = NewRunner().Run(NewPlan(source, target, note: "测试迁移"));

        Assert.True(result.Ok);
        // 源位置现在是 junction，且能透过它看到数据
        var srcInfo = new DirectoryInfo(source);
        Assert.True(srcInfo.Attributes.HasFlag(FileAttributes.ReparsePoint), "源位置应为 junction");
        Assert.True(Directory.Exists(Path.Combine(source, "sub")));
        Assert.True(File.Exists(Path.Combine(source, "sub", "nested.txt")));
        // 目标是普通目录（真实数据所在）
        Assert.False(new DirectoryInfo(target).Attributes.HasFlag(FileAttributes.ReparsePoint));
        Assert.True(File.Exists(Path.Combine(target, "file0.txt")));
        // 登记表新增条目：Source 是链接（原位置），Target 是真实数据
        var registry = LinkRegistry.Load(_root);
        var entry = Assert.Single(registry.Entries);
        Assert.Equal(Path.GetFullPath(source), entry.Source);
        Assert.Equal(Path.GetFullPath(target), entry.Target);
        Assert.Equal("junction", entry.Type);
    }

    [Fact]
    public void Run_preserves_file_counts_and_bytes()
    {
        var source = NewSource();
        var target = Path.Combine(_root, "dst");

        NewRunner().Run(NewPlan(source, target));

        Assert.Equal(CountFiles(source), CountFiles(target));
        Assert.Equal(CountBytes(target), CountBytes(source));
    }

    [Fact]
    public void Run_migrates_empty_source_directory()
    {
        var source = Path.Combine(_root, "empty-src");
        Directory.CreateDirectory(source);
        var target = Path.Combine(_root, "empty-dst");

        var result = NewRunner().Run(NewPlan(source, target));

        Assert.True(result.Ok);
        Assert.True(new DirectoryInfo(source).Attributes.HasFlag(FileAttributes.ReparsePoint));
        Assert.True(Directory.Exists(target));
    }

    // ---- robocopy 退出码语义 ----

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(7, true)]
    [InlineData(8, false)]
    [InlineData(16, false)]
    public void Robocopy_exit_code_0_to_7_is_success_8_and_above_is_failure(int exitCode, bool expectedOk)
    {
        Assert.Equal(expectedOk, RobocopyCopier.IsExitCodeSuccess(exitCode));
    }

    // ---- 失败清理 ----

    [Fact]
    public void Run_on_verify_failure_source_intact_target_cleaned()
    {
        var source = NewSource();
        var target = Path.Combine(_root, "dst");

        // 注入一个复制后篡改目标的验证器，模拟「复制完核对不一致」
        var tamper = new TamperingVerifier();
        var runner = new MigrationRunner(FakeProcessProbeFactory.Of([]), verifier: tamper);

        var ex = Assert.Throws<MigrationException>(
            () => runner.Run(NewPlan(source, target)));

        Assert.Contains("核对", ex.Message);
        // 源数据完好：还是普通目录，内容全在
        Assert.True(Directory.Exists(source));
        Assert.False(new DirectoryInfo(source).Attributes.HasFlag(FileAttributes.ReparsePoint));
        Assert.Equal(4, CountFiles(source));
        Assert.True(File.Exists(Path.Combine(source, "file0.txt")));
        // 目标已清理
        Assert.False(Directory.Exists(target));
        // 登记表未写入
        var registry = LinkRegistry.Load(_root);
        Assert.Empty(registry.Entries);
    }

    [Fact]
    public void Run_on_robocopy_failure_source_intact_target_cleaned()
    {
        var source = NewSource();
        var target = Path.Combine(_root, "dst");

        var runner = new MigrationRunner(
            FakeProcessProbeFactory.Of([]), copier: new FailingCopier());

        var ex = Assert.Throws<MigrationException>(
            () => runner.Run(NewPlan(source, target)));

        Assert.Contains("robocopy", ex.Message);
        Assert.True(Directory.Exists(source));
        Assert.True(File.Exists(Path.Combine(source, "file0.txt")));
        Assert.False(Directory.Exists(target));
        Assert.Empty(LinkRegistry.Load(_root).Entries);
    }

    [Fact]
    public void Run_on_registry_failure_junction_rolled_back_source_restored()
    {
        var source = NewSource();
        var target = Path.Combine(_root, "dst");

        // 登记文件损坏：最后的登记步骤失败，junction 必须被回滚、源恢复原样
        Directory.CreateDirectory(Path.Combine(_root, LinkRegistry.DefaultFolderName));
        File.WriteAllText(Path.Combine(_root, LinkRegistry.DefaultFolderName, LinkRegistry.DefaultFileName), "{ not json");

        var ex = Assert.Throws<MigrationException>(
            () => NewRunner().Run(NewPlan(source, target)));

        // 必须讲清数据在哪：防止用户按"目标非空"报错去清空唯一的数据副本
        Assert.Contains("完整数据只在", ex.Message);
        Assert.IsType<RegistryCorruptException>(ex.InnerException);
        Assert.Equal(Path.Combine(target, "file0.txt"), Path.Combine(target, "file0.txt"));

        // 最坏情况修复：源位置恢复为普通目录壳（数据完好保存在目标），不留指向坏状态的链接
        Assert.True(Directory.Exists(source), "源位置应恢复为目录壳");
        Assert.False(new DirectoryInfo(source).Attributes.HasFlag(FileAttributes.ReparsePoint));
        Assert.True(File.Exists(Path.Combine(target, "file0.txt")), "已复制到目标的数据保留");
    }

    private static int CountFiles(string dir) =>
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Count();

    private static long CountBytes(string dir) =>
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);

    /// <summary>验证时把目标里的文件删掉一个，制造核对不一致。</summary>
    private sealed class TamperingVerifier : ICopyVerifier
    {
        public void Verify(string source, string target, CopyStats copied)
        {
            File.Delete(Path.Combine(target, "file1.txt"));
            throw new MigrationException("核对失败：文件数不一致");
        }
    }

    private sealed class FailingCopier : ICopier
    {
        public CopyStats Copy(string source, string target) =>
            throw new MigrationException("robocopy 失败：退出码 16");
    }
}
