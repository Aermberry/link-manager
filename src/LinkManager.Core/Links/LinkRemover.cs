using LinkManager.Core.Health;
using LinkManager.Core.Registry;

namespace LinkManager.Core.Links;

/// <summary>删除被拒绝（未确认、条目不是链接等）。</summary>
public sealed class LinkRemovalException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>
/// 登记条目删除器，两种路径：
/// - 仅删登记：只从登记表移除条目，文件系统链接原样保留；
/// - 连同链接一起删：删除链接本身并移除登记，需要调用方显式确认。
/// 真实数据（链接指向的源目录）任何路径都不删。
/// </summary>
public static class LinkRemover
{
    /// <summary>仅删登记：移除条目并落盘，不动文件系统链接。未命中时不写盘。</summary>
    public static bool RemoveEntryOnly(LinkRegistry registry, string source)
    {
        var removed = registry.Remove(source);
        if (removed)
        {
            registry.Save();
        }

        return removed;
    }

    /// <summary>
    /// 连同链接一起删：删除 source 处的链接并移除登记条目。
    /// 必须显式 confirmDeleteLink=true；条目必须是真实链接（reparse point），
    /// 普通目录/文件一律拒绝——登记可能是过期的，删除真实数据永远不由本方法代办。
    /// 顺序：全部检查与文件系统删除成功后，才变更登记并落盘。
    /// </summary>
    /// <exception cref="LinkRemovalException">未确认、条目不是链接、或链接删除失败。</exception>
    public static bool RemoveWithLink(LinkRegistry registry, string source, bool confirmDeleteLink)
    {
        if (!confirmDeleteLink)
        {
            throw new LinkRemovalException("拒绝删除链接：未获显式确认。删除链接属于破坏性操作，调用方必须先取得用户确认。");
        }

        if (!registry.ContainsEntry(source))
        {
            return false;
        }

        if (!Directory.Exists(source) && !File.Exists(source))
        {
            // 条目登记的链接已不存在（Missing）：登记删除即完成
            registry.Remove(source);
            registry.Save();
            return true;
        }

        var entry = registry.Entries.First(e => MatchesRegistrySource(e, source));
        if (LinkHealth.Check(source, entry.Type) == LinkStatus.Disguised)
        {
            throw new LinkRemovalException($"该路径不是链接而是普通目录/文件，拒绝删除：{source}。请检查登记条目是否过期。");
        }

        try
        {
            // 只删链接本身（非递归）：目录链接用目录删，文件链接（symlink→file）用文件删
            if (File.Exists(source) && !Directory.Exists(source))
            {
                File.Delete(source);
            }
            else
            {
                Directory.Delete(source, recursive: false);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new LinkRemovalException($"链接删除失败：{source}", ex);
        }

        // 链接已删除，最后才移除登记并落盘
        registry.Remove(source);
        registry.Save();
        return true;
    }

    private static bool MatchesRegistrySource(Registry.LinkEntry entry, string source) =>
        string.Equals(entry.Source, source, StringComparison.OrdinalIgnoreCase)
        || string.Equals(entry.Source, source.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase);
}
