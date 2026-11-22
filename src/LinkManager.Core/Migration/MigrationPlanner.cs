using LinkManager.Core.Links;
using LinkManager.Core.Registry;

namespace LinkManager.Core.Migration;

/// <summary>迁移计划：可整体输出（dry-run）也可逐项执行的状态序列。</summary>
public sealed record MigrationPlan
{
    public required string Source { get; init; }
    public required string Target { get; init; }
    public required string Note { get; init; }
    public required string RegistryFile { get; init; }
    /// <summary>登记表根目录（传给 LinkRegistry.Load；null 用产品默认根）。</summary>
    public string? RegistryRoot { get; init; }
    public required string[] WatchedProcesses { get; init; }

    /// <summary>前置检查汇总的运行中进程（Plan 时探测一次，供 dry-run 展示）。</summary>
    public IReadOnlyList<RunningProcess> RunningProcesses { get; init; } = [];

    /// <summary>计划步骤的中文描述（按执行顺序）。</summary>
    public IReadOnlyList<string> Steps { get; init; } = [];

    public bool Ok => RunningProcesses.Count == 0;
}

/// <summary>迁移计划编制：只读探测，不改动文件系统。</summary>
public static class MigrationPlanner
{
    public static MigrationPlan Plan(string source, string target, string note = "",
        string? registryRoot = null, IProcessProbe? probe = null,
        string[]? watchList = null)
    {
        // 去掉末尾分隔符：robocopy 以引号包参，"C:\dir\" 会把 \" 转成转义引号破坏 argv
        var fullSource = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
        var fullTarget = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target));
        var watched = watchList ?? DefaultProcessProbe.DefaultWatchList;

        var steps = new List<string>
        {
            $"前置检查：源目录存在（{fullSource}）",
            $"前置检查：目标目录为空或不存在（{fullTarget}）",
            $"前置检查：相关进程未运行（{string.Join("、", watched)}）",
            $"robocopy 复制：{fullSource} → {fullTarget}",
            $"核对文件数与字节数一致",
            $"删除源目录（核对通过后才执行）",
            $"在原位置创建回指 junction：{fullSource} → {fullTarget}",
            $"登记新链接到登记表（{MigrationPlanFile(registryRoot)}）",
        };

        var running = (probe ?? new DefaultProcessProbe()).FindRunning(watched);

        return new MigrationPlan
        {
            Source = fullSource,
            Target = fullTarget,
            Note = note,
            RegistryFile = MigrationPlanFile(registryRoot),
            RegistryRoot = registryRoot,
            WatchedProcesses = watched,
            RunningProcesses = running,
            Steps = steps,
        };
    }

    private static string MigrationPlanFile(string? registryRoot)
    {
        var root = registryRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(root, LinkRegistry.DefaultFolderName, LinkRegistry.DefaultFileName);
    }
}
