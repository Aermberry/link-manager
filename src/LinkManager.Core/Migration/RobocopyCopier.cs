using System.Diagnostics;

namespace LinkManager.Core.Migration;

/// <summary>robocopy 复制器：真实进程调用，退出码 0-7 成功、≥8 失败。</summary>
public sealed class RobocopyCopier : ICopier
{
    /// <summary>robocopy 退出码 0-7 为成功（1=有文件复制，2=有多余项，4=有不匹配项）；≥8 表示有失败。</summary>
    public static bool IsExitCodeSuccess(int exitCode) => exitCode is >= 0 and < 8;

    public CopyStats Copy(string source, string target)
    {
        Directory.CreateDirectory(target);

        // /E 含空目录 /COPY:DAT 数据+属性+时间戳 /DCOPY:DAT 目录时间戳
        // /R:2 /W:2 重试策略 /XJ 不跟随 junction 防死循环 /NFL /NDL /NP 精简日志
        var psi = new ProcessStartInfo(
            "robocopy.exe",
            $"\"{source}\" \"{target}\" /E /COPY:DAT /DCOPY:DAT /R:2 /W:2 /XJ /NFL /NDL /NP")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var p = Process.Start(psi)
            ?? throw new MigrationException("无法启动 robocopy 进程");

        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();

        if (!IsExitCodeSuccess(p.ExitCode))
        {
            throw new MigrationException(
                $"robocopy 失败：退出码 {p.ExitCode}（≥8 表示有文件复制失败）。{stderr}{stdout}");
        }

        return CountTree(target);
    }

    /// <summary>统计目录树的文件数与字节数（不跟随 reparse point，防环）。</summary>
    public static CopyStats CountTree(string directory)
    {
        var info = new DirectoryInfo(directory);
        if (!info.Exists)
        {
            return new CopyStats(0, 0);
        }

        var fileCount = 0;
        long totalBytes = 0;
        var stack = new Stack<DirectoryInfo>();
        stack.Push(info);

        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            foreach (var f in dir.EnumerateFiles())
            {
                fileCount++;
                totalBytes += f.Length;
            }

            foreach (var d in dir.EnumerateDirectories())
            {
                // 不进入 reparse point（junction/symlink），与 /XJ 一致
                if ((d.Attributes & FileAttributes.ReparsePoint) == 0)
                {
                    stack.Push(d);
                }
            }
        }

        return new CopyStats(fileCount, totalBytes);
    }
}
