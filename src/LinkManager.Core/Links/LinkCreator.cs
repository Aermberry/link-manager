using System.Text;
using LinkManager.Core.Registry;

namespace LinkManager.Core.Links;

/// <summary>创建链接的护栏被违反（源不存在、目标非空、路径成环等）。</summary>
public sealed class LinkCreateException(string message) : Exception(message);

/// <summary>链接创建器：junction/symlink 创建 + 护栏 + 自动登记。</summary>
public static class LinkCreator
{
    /// <summary>在 target 处创建指向 source 的 junction 并登记。零特权需求。</summary>
    /// <exception cref="LinkCreateException">护栏被违反、junction 创建失败或登记失败（已回滚）。</exception>
    public static LinkEntry CreateJunction(string source, string target, string note, string? registryRoot = null)
    {
        Validate(source, target);

        RunJunctionTool(source, target);

        return Register(new LinkEntry
        {
            // 登记约定：Source 是链接路径，Target 是解析后的真实目录
            Source = Path.GetFullPath(target),
            Target = Path.GetFullPath(source),
            Type = "junction",
            Note = note,
        }, registryRoot);
    }

    /// <summary>
    /// 在 target 处创建指向 source 的目录 symlink 并登记。需要 Windows 开发者模式
    /// （门槛经 <paramref name="detector"/> 注入，测试可模拟；传 null 用注册表真值）。
    /// GUI 可经 <see cref="IDeveloperModeDetector"/> 预查询门槛状态以禁用选项。
    /// </summary>
    /// <exception cref="InvalidOperationException">开发者模式未开启（消息含中文指引）。</exception>
    /// <exception cref="LinkCreateException">护栏被违反、symlink 创建失败或登记失败（已回滚）。</exception>
    public static LinkEntry CreateSymlink(
        string source, string target, string note,
        string? registryRoot = null, IDeveloperModeDetector? detector = null)
    {
        // 门槛前置：未开开发者模式时一切创建请求都被拒，且不做任何文件系统副作用
        WindowsDeveloperModeDetector.Options.Require(detector ?? new WindowsDeveloperModeDetector());

        // issue #18：symlink 仅接受绝对路径，拒绝相对路径
        RequireAbsolute(source);
        RequireAbsolute(target);

        Validate(source, target);

        RunSymlinkTool(source, target);

        return Register(new LinkEntry
        {
            Source = Path.GetFullPath(target),
            Target = Path.GetFullPath(source),
            Type = "symlink",
            Note = note,
        }, registryRoot);
    }

    /// <summary>CLI/GUI 共用的创建成功消息。</summary>
    public static string DescribeCreated(LinkEntry entry) =>
        entry.Type == "symlink"
            ? $"已创建 symlink：{entry.Source} → {entry.Target}（已自动登记）"
            : $"已创建 junction：{entry.Source} → {entry.Target}（已自动登记）";

    /// <summary>登记新链接；登记失败时回滚删除刚创建的链接，不留孤儿。</summary>
    private static LinkEntry Register(LinkEntry entry, string? registryRoot)
    {
        try
        {
            var registry = LinkRegistry.Load(registryRoot);
            registry.Add(entry);
            registry.Save();
        }
        catch (Exception)
        {
            // 登记失败回滚：删掉刚建的链接，不留"已创建未登记"的孤儿链接
            try { Directory.Delete(entry.Source); } catch (Exception ex)
                when (ex is IOException or UnauthorizedAccessException) { }
            throw;
        }

        return entry;
    }

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
        // 用 -EncodedCommand 传命令：路径内容不会破坏 PS 字符串，杜绝引号注入
        var cmd = $"(New-Item -ItemType Junction -Path '{target}' -Target '{source}') | Out-Null";
        RunLinkTool(cmd, "junction", target, source);
    }

    private static void RunSymlinkTool(string source, string target)
    {
        // 不用 New-Item -ItemType SymbolicLink：Windows PowerShell 5.1 对 symlink
        // 一律要求管理员权限，无视开发者模式；cmd 的 mklink（CreateSymbolicLinkW）
        // 在开发者模式开启时无需特权，与注册表门槛一致。
        // mklink 只认反斜杠路径（正斜杠报"语法不正确"），先归一化。
        var cmd = $"cmd /c mklink /D \"{Path.GetFullPath(target)}\" \"{Path.GetFullPath(source)}\"";
        RunLinkTool(cmd, "symlink", target, source);
    }

    /// <summary>junction/symlink 共用的创建通道：清空目标、建父目录、PowerShell -EncodedCommand 执行命令。</summary>
    private static void RunLinkTool(string cmd, string label, string target, string source)
    {
        if (Directory.Exists(target))
        {
            // 目标存在且为空目录：先删掉再建链接（Validate 已确认其为空，无数据风险）
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
            throw new LinkCreateException($"{label} 创建超时：{target} → {source}");
        }

        // 读取 stdout 排空缓冲区，避免大输出与 WaitForExit 死锁；失败时把真实原因带出来
        _ = p.StandardOutput.ReadToEnd();

        if (p.ExitCode != 0 || !Directory.Exists(target))
        {
            throw new LinkCreateException($"{label} 创建失败：{target} → {source}。原因：{(p.ExitCode != 0 ? $"exit {p.ExitCode}；" : "")}{stderr}");
        }
    }

    private static void RequireAbsolute(string path)
    {
        if (!Path.IsPathFullyQualified(path))
        {
            throw new LinkCreateException($"symlink 仅接受绝对路径，拒绝相对路径：{path}");
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
