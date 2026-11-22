using LinkManager.Core.Links;
using LinkManager.Core.Registry;

namespace LinkManager.Core.Migration;

/// <summary>迁移执行结果：成功摘要或失败报告。</summary>
public sealed record MigrationResult
{
    public required bool Ok { get; init; }
    public required string Report { get; init; }
}

/// <summary>
/// 迁移状态机：前置检查 → robocopy 复制 → 核对 → 删源 → 建回指 junction → 登记。
/// 任一步失败源数据保持原样并清理目标；成功后源位置为 junction 且登记表新增条目。
/// </summary>
public sealed class MigrationRunner
{
    private readonly Func<string[], IProcessProbe> _probeFactory;
    private readonly ICopier _copier;
    private readonly ICopyVerifier _verifier;

    public MigrationRunner(
        Func<string[], IProcessProbe>? probeFactory = null,
        ICopier? copier = null,
        ICopyVerifier? verifier = null)
    {
        _probeFactory = probeFactory ?? (_ => new DefaultProcessProbe());
        _copier = copier ?? new RobocopyCopier();
        _verifier = verifier ?? new CopyVerifier();
    }

    /// <summary>dry-run：只输出完整执行计划，不做任何文件系统改动。</summary>
    public MigrationResult DryRun(MigrationPlan plan)
    {
        var lines = new List<string> { "=== 迁移计划（dry-run，未做任何改动） ===" };
        lines.Add($"源：{plan.Source}");
        lines.Add($"目标：{plan.Target}");

        if (plan.RunningProcesses.Count > 0)
        {
            lines.Add("⚠ 检测到相关进程仍在运行，现在执行会被拒绝：");
            foreach (var p in plan.RunningProcesses)
            {
                lines.Add($"  - {p.Name}");
            }
        }

        lines.Add("将执行：");
        foreach (var (step, i) in plan.Steps.Select((s, i) => (s, i)))
        {
            lines.Add($"  {i + 1}. {step}");
        }

        lines.Add(plan.Ok
            ? "以上仅为计划。加 --execute 才会实际执行。"
            : "当前存在阻碍项，实际执行将被拒绝。");

        return new MigrationResult
        {
            Ok = plan.Ok,
            Report = string.Join(Environment.NewLine, lines),
        };
    }

    /// <summary>执行完整迁移链路。任一步失败：源保持原样、目标清理。</summary>
    /// <exception cref="MigrationException">前置检查被违反、复制/核对/删源失败，或登记失败（已回滚，数据仅在目标）。</exception>
    public MigrationResult Run(MigrationPlan plan)
    {
        Precheck(plan);

        // ---- 复制 ----
        CopyStats stats;
        try
        {
            stats = _copier.Copy(plan.Source, plan.Target);
        }
        catch (MigrationException)
        {
            CleanupTarget(plan.Target);
            throw;
        }

        // ---- 核对 ----
        try
        {
            _verifier.Verify(plan.Source, plan.Target, stats);
        }
        catch (MigrationException)
        {
            CleanupTarget(plan.Target);
            throw;
        }

        // ---- 删源 ----
        try
        {
            Directory.Delete(plan.Source, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 删源中途失败（文件被占用等）：源可能已被部分删除，无法回滚该步，
            // 但核对已确认目标保存着完整副本——把真相与恢复路径一并报出
            throw new MigrationException(
                $"删除源目录失败：{ex.Message}。注意：源可能已被部分删除；" +
                $"完整数据已核对并保存在 {plan.Target}，可从那里拷回缺失文件。");
        }

        // ---- 建回指 junction ----
        try
        {
            LinkCreator.RunJunctionToolForMigration(linkPath: plan.Source, dataPath: plan.Target);
        }
        catch (LinkCreateException)
        {
            // junction 失败无法回滚删源（源已删），但目标数据完好——报告手工恢复路径
            throw new MigrationException(
                $"junction 创建失败，数据已完整复制到 {plan.Target}。" +
                $"请手工在原位置建立 junction：mklink /J \"{plan.Source}\" \"{plan.Target}\"（源目录已删除）。");
        }

        // ---- 登记 ----
        try
        {
            var registry = LinkRegistry.Load(plan.RegistryRoot);
            registry.Add(new LinkEntry
            {
                Source = plan.Source,
                Target = plan.Target,
                Type = "junction",
                Note = plan.Note,
            });
            registry.Save();
        }
        catch (Exception ex) when (ex is RegistryCorruptException or RegistryIoException)
        {
            // 登记失败回滚：删 junction、把目录壳还原回源位置，数据仍在目标。
            // 必须把"数据只在目标"说清楚：回滚后重跑会被"目标非空"挡下，
            // 而那条报错若被照做（清空目标）会删掉唯一的数据副本。
            try { Directory.Delete(plan.Source); }
            catch (Exception rbEx) when (rbEx is IOException or UnauthorizedAccessException) { }
            Directory.CreateDirectory(plan.Source);
            throw new MigrationException(
                $"登记失败（{ex.Message}）。已回滚：原位置恢复为普通目录，未建立链接。" +
                $"完整数据只在 {plan.Target}，源位置现在是空目录壳。" +
                $"请先修复登记文件，再手工迁移或删除目标目录，切勿直接清空目标。", ex);
        }

        return new MigrationResult
        {
            Ok = true,
            Report = $"迁移完成：{plan.Source} → {plan.Target}（{stats.FileCount} 个文件 / {stats.TotalBytes} 字节）。" +
                     $"原位置已建 junction，登记表已更新。",
        };
    }

    private void Precheck(MigrationPlan plan)
    {
        // 文件检查在先：Directory.Exists 对文件路径返回 false，顺序反了会把"是文件"误报成"不存在"
        if (File.Exists(plan.Source))
        {
            throw new MigrationException($"源路径是文件不是目录：{plan.Source}");
        }

        if (!Directory.Exists(plan.Source))
        {
            throw new MigrationException($"源路径不存在：{plan.Source}");
        }

        // 环/嵌套检查前置：目标在源内部时，robocopy 会把复制目标搬进源树，随后的
        // "删源"连目标数据一起删光；源在目标内部同理。相等的源与目标会造出自指 junction。
        if (IsSamePath(plan.Source, plan.Target)
            || IsWithin(plan.Target, plan.Source)
            || IsWithin(plan.Source, plan.Target))
        {
            throw new MigrationException(
                $"源与目标互为嵌套或相同，会造成循环迁移与数据丢失：{plan.Source} / {plan.Target}");
        }

        if (new DirectoryInfo(plan.Source).Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new MigrationException(
                $"源路径已是一个链接，请迁移它指向的真实目录：{plan.Source}");
        }

        if (Directory.Exists(plan.Target))
        {
            if (new DirectoryInfo(plan.Target).Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new MigrationException($"目标路径已存在且是一个链接：{plan.Target}");
            }

            if (Directory.EnumerateFileSystemEntries(plan.Target).Any())
            {
                throw new MigrationException($"目标路径非空，拒绝覆盖：{plan.Target}。请先清空或换一个目标路径。");
            }
        }
        else if (File.Exists(plan.Target))
        {
            throw new MigrationException($"目标路径已存在同名文件：{plan.Target}");
        }

        var running = _probeFactory(plan.WatchedProcesses).FindRunning(plan.WatchedProcesses);
        if (running.Count > 0)
        {
            var names = string.Join("、", running.Select(p => p.Name));
            throw new MigrationException(
                $"以下程序还在运行，请先退出它们再迁移：{names}");
        }
    }

    /// <summary>候选是否在祖先目录内部（严格前缀，路径末尾分隔符归一化，不区分大小写）。</summary>
    internal static bool IsWithin(string candidate, string ancestor)
    {
        var fullCandidate = Path.GetFullPath(candidate).TrimEnd('\\') + '\\';
        var fullAncestor = Path.GetFullPath(ancestor).TrimEnd('\\') + '\\';
        return fullCandidate.StartsWith(fullAncestor, StringComparison.OrdinalIgnoreCase)
               && !string.Equals(fullCandidate, fullAncestor, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSamePath(string a, string b) =>
        string.Equals(
            Path.GetFullPath(a).TrimEnd('\\'),
            Path.GetFullPath(b).TrimEnd('\\'),
            StringComparison.OrdinalIgnoreCase);

    private static void CleanupTarget(string target)
    {
        try
        {
            if (Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 清理失败不掩盖原始错误；目标残留由报错方带出
        }
    }
}
