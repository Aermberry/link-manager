using LinkMgr.Core.Registry;

namespace LinkMgr.Core.Links;

/// <summary>创建链接的护栏被违反（源不存在、目标非空、路径成环等）。</summary>
public sealed class LinkCreateException(string message) : Exception(message);

/// <summary>链接创建器：junction 创建 + 护栏 + 自动登记。</summary>
public static class LinkCreator
{
    /// <summary>在 target 处创建指向 source 的 junction 并登记。零特权需求。</summary>
    /// <exception cref="LinkCreateException">护栏被违反：源不存在、目标非空、路径成环。</exception>
    /// <exception cref="RegistryCorruptException">登记文件损坏。</exception>
    /// <exception cref="RegistryIoException">登记文件读写失败。</exception>
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

        var registry = LinkRegistry.Load(registryRoot);
        registry.Add(entry);
        registry.Save();

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
        if (Directory.Exists(target))
        {
            // 目标存在且为空目录：先删掉再建 junction（空目录无数据风险）
            Directory.Delete(target);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);

        var psi = new System.Diagnostics.ProcessStartInfo(
            "powershell.exe",
            $"-NoProfile -Command \"(New-Item -ItemType Junction -Path '{target}' -Target '{source}') | Out-Null\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = System.Diagnostics.Process.Start(psi)!;
        p.WaitForExit(15000);

        if (!Directory.Exists(target))
        {
            throw new LinkCreateException($"junction 创建失败（命令行工具返回非零或目标未出现）：{target} → {source}");
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
