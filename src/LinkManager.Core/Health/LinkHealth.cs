namespace LinkManager.Core.Health;

/// <summary>链接健康状态。</summary>
public enum LinkStatus
{
    /// <summary>链接存在且目标存在。</summary>
    Ok,

    /// <summary>链接存在但目标已不存在（断链）。</summary>
    Broken,

    /// <summary>登记的链接本身已不存在。</summary>
    Missing,

    /// <summary>路径存在但是普通目录/文件，不是链接（伪装条目）。</summary>
    Disguised,
}

/// <summary>链接健康检查：判定登记条目的真实状态。</summary>
public static class LinkHealth
{
    /// <summary>判定 source 处的链接状态。</summary>
    public static LinkStatus Check(string source, string linkType)
    {
        if (!Directory.Exists(source) && !File.Exists(source))
        {
            return LinkStatus.Missing;
        }

        var link = ResolveTarget(source);
        if (link is null)
        {
            return LinkStatus.Disguised;
        }

        // 目标存在性按登记的类型判：junction 必为目录；symlink 可能指向文件
        var targetExists = linkType == "symlink"
            ? Directory.Exists(link) || File.Exists(link)
            : Directory.Exists(link);

        return targetExists ? LinkStatus.Ok : LinkStatus.Broken;
    }

    /// <summary>解析链接的真实目标；路径不是 reparse point（普通目录/文件）返回 null。</summary>
    private static string? ResolveTarget(string source)
    {
        var attrs = new FileInfo(source).Attributes;
        if ((attrs & FileAttributes.ReparsePoint) == 0)
        {
            return null;
        }

        var resolved = new DirectoryInfo(source).ResolveLinkTarget(returnFinalTarget: true);
        return resolved?.FullName;
    }
}
