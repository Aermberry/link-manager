using LinkManager.Core.Registry;

namespace LinkManager.Core.Migration;

/// <summary>迁移护栏被违反或某一步失败（源数据保持原样）。</summary>
public sealed class MigrationException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>迁移前检查发现的目标程序运行中进程。</summary>
public sealed record RunningProcess(string Name);

/// <summary>
/// 迁移前置检查的进程探测接口：给定关注的进程名列表，返回其中仍在运行的。
/// 唯一注入接缝——测试借此模拟「目标程序运行中」的拒绝路径。
/// </summary>
public interface IProcessProbe
{
    /// <summary>返回 processNames 中正在运行的进程（不区分大小写匹配进程名）。</summary>
    IReadOnlyList<RunningProcess> FindRunning(IEnumerable<string> processNames);
}

/// <summary>复制结果统计：文件数与字节数（核对用）。</summary>
public sealed record CopyStats(int FileCount, long TotalBytes);

/// <summary>复制器接口：把 source 完整复制到 target，返回统计。失败抛异常。</summary>
public interface ICopier
{
    CopyStats Copy(string source, string target);
}

/// <summary>复制核对器：确认目标与源的文件数、字节数一致。</summary>
public interface ICopyVerifier
{
    /// <exception cref="MigrationException">核对不一致。</exception>
    void Verify(string source, string target, CopyStats copied);
}
