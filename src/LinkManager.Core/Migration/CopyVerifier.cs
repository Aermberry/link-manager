namespace LinkManager.Core.Migration;

/// <summary>默认核对器：目标树与复制统计（robocopy 后实数）一致才放行删源。</summary>
public sealed class CopyVerifier : ICopyVerifier
{
    public void Verify(string source, string target, CopyStats copied)
    {
        var actual = RobocopyCopier.CountTree(target);

        if (actual.FileCount != copied.FileCount)
        {
            throw new MigrationException(
                $"核对失败：文件数不一致（复制时报 {copied.FileCount}，实数 {actual.FileCount}）。源数据保持原样。");
        }

        if (actual.TotalBytes != copied.TotalBytes)
        {
            throw new MigrationException(
                $"核对失败：字节数不一致（复制时报 {copied.TotalBytes}，实数 {actual.TotalBytes}）。源数据保持原样。");
        }

        // 复制统计是 robocopy 后目标实数；再与源树比对一次，双保险
        var sourceStats = RobocopyCopier.CountTree(source);
        if (sourceStats.FileCount != actual.FileCount || sourceStats.TotalBytes != actual.TotalBytes)
        {
            throw new MigrationException(
                $"核对失败：目标与源不一致（源 {sourceStats.FileCount} 个文件 / {sourceStats.TotalBytes} 字节，" +
                $"目标 {actual.FileCount} 个文件 / {actual.TotalBytes} 字节）。源数据保持原样。");
        }
    }
}
