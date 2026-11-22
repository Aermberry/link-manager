using System.Diagnostics;

namespace LinkManager.Core.Migration;

/// <summary>默认进程探测：按进程名扫描当前运行中的进程。</summary>
public sealed class DefaultProcessProbe : IProcessProbe
{
    /// <summary>迁移场景默认关注的进程名（与参考脚本一致）。</summary>
    public static readonly string[] DefaultWatchList = ["openmausbot", "magpie"];

    public IReadOnlyList<RunningProcess> FindRunning(IEnumerable<string> processNames)
    {
        var wanted = processNames
            .Select(n => n.Trim())
            .Where(n => n.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (wanted.Count == 0)
        {
            return [];
        }

        return Process.GetProcesses()
            .Where(p =>
            {
                try { return wanted.Contains(p.ProcessName); }
                catch (Exception ex) when (ex is
                    InvalidOperationException or
                    System.ComponentModel.Win32Exception or
                    NotSupportedException)
                {
                    // 进程已退出或无权读取名称：当作不在运行
                    return false;
                }
            })
            .Select(p => new RunningProcess(p.ProcessName))
            .DistinctBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
