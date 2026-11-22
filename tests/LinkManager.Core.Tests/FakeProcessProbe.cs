using LinkManager.Core.Migration;

namespace LinkManager.Core.Tests;

/// <summary>把「进程未运行」的探测做成可控：注入运行中/未运行两种结果。</summary>
internal sealed class FakeProcessProbe(string[] runningProcesses) : IProcessProbe
{
    public static readonly FakeProcessProbe NothingRunning = new([]);

    public IReadOnlyList<RunningProcess> FindRunning(IEnumerable<string> processNames)
    {
        return processNames
            .Where(name => runningProcesses.Any(r =>
                r.Equals(name, StringComparison.OrdinalIgnoreCase)))
            .Select(name => new RunningProcess(name))
            .ToList();
    }
}
