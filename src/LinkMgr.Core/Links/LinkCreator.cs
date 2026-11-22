using System.Text;
using LinkMgr.Core.Registry;

namespace LinkMgr.Core.Links;

/// <summary>创建链接的护栏被违反（源不存在、目标非空、路径成环等）。</summary>
public sealed class LinkCreateException(string message) : Exception(message);

/// <summary>链接创建器：junction 创建 + 护栏 + 自动登记。</summary>
public static class LinkCreator
{
    /// <summary>在 target 处创建指向 source 的 junction 并登记。零特权需求。</summary>
    /// <exception cref="LinkCreateException">护栏被违反、junction 创建失败或登记失败（已回滚）。</exception>
    public static LinkEntry CreateJunction(string source, string target, string note, string? registryRoot = null)
    {
        Validate(source, target);

        RunJunctionTool(source, target);

        var entry = new LinkEntry
        {
            // 登记约定：Source 是链接路径，Target 是解析后的真实目录
            Source = Path.GetFullPath(target),
            Target = Path.GetFullPath(source),
            Type = "junction",
            Note = note,
        };

        try
        {
            var registry = LinkRegistry.Load(registryRoot);
            registry.Add(entry);
            registry.Save();
        }
        catch (Exception)
        {
            // 登记失败回滚：删掉刚建的 junction，不留"已创建未登记"的孤儿链接
            try { Directory.Delete(target); } catch (IOException) { }
            throw;
        }

        return entry;
    }

    /// <summary>CLI/GUI 共用的创建成功消息。</summary>
    public static string DescribeCreated(LinkEntry entry) =>
        $"已创建 junction：{entry.Source} → {entry.Target}（已自动登记）";

    private static void Validate(string source, string target)
    {
        // 环检查前置：源在目标内部时，"目标非空"只是副产物，真根因是循环链接
        if (IsWithin(target, source) || IsWithin(source, target))
        {
            throw new LinkCreateException($"源与目标互为嵌套，会造成循环链接：{source} / {target}");
        }

        if (!Directory.Exists(source))
        {
            throw new LinkCreateException($"源路径不存在：{source}");
        }

        if (Directory.Exists(target))
        {
            if (new DirectoryInfo(target).Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new LinkCreateException($"目标路径已存在且是一个链接：{target}");
            }

            if (Directory.EnumerateFileSystemEntries(target).Any())
            {
                throw new LinkCreateException($"目标路径非空，拒绝覆盖：{target}。请先清空或换一个目标路径。");
            }
        }
        else if (File.Exists(target))
        {
            throw new LinkCreateException($"目标路径已存在同名文件：{target}");
        }
    }

    private static void RunJunctionTool(string source, string target)
    {
        if (Directory.Exists(target))
        {
            // 目标存在且为空目录：先删掉再建 junction（Validate 已确认其为空，无数据风险）
            Directory.Delete(target);
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
        }
        catch (IOException ex)
        {
            throw new LinkCreateException($"创建目标目录失败：{ex.Message}");
        }

        // 用 -EncodedCommand 传命令：路径内容不会破坏 PS 字符串，杜绝引号注入
        var cmd = $"(New-Item -ItemType Junction -Path '{target}' -Target '{source}') | Out-Null";
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(cmd));

        var psi = new System.Diagnostics.ProcessStartInfo(
            "powershell.exe",
            $"-NoProfile -EncodedCommand {encoded}")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = System.Diagnostics.Process.Start(psi)
            ?? throw new LinkCreateException("无法启动 PowerShell 进程");

        var stderr = p.StandardError.ReadToEnd();
        var exited = p.WaitForExit(15000);
        if (!exited)
        {
            p.Kill();
            throw new LinkCreateException($"junction 创建超时：{target} → {source}");
        }

        // 读取 stdout 排空缓冲区，避免大输出与 WaitForExit 死锁；失败时把真实原因带出来
        _ = p.StandardOutput.ReadToEnd();

        if (p.ExitCode != 0 || !Directory.Exists(target))
        {
            throw new LinkCreateException($"junction 创建失败：{target} → {source}。原因：{(p.ExitCode != 0 ? $"exit {p.ExitCode}；" : "")}{stderr}");
        }
    }

    private static bool IsWithin(string candidate, string ancestor)
    {
        var fullCandidate = Path.GetFullPath(candidate).TrimEnd('\\') + '\\';
        var fullAncestor = Path.GetFullPath(ancestor).TrimEnd('\\') + '\\';
        return fullCandidate.StartsWith(fullAncestor, StringComparison.OrdinalIgnoreCase)
               && !string.Equals(fullCandidate, fullAncestor, StringComparison.OrdinalIgnoreCase);
    }
}
