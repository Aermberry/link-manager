using System.IO;
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
    /// <summary>仅删登记：移除条目，不动文件系统链接。调用方负责 Save。</summary>
    public static bool RemoveEntryOnly(LinkRegistry registry, string source)
    {
        var removed = registry.Remove(source);
        registry.Save();
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
            throw new LinkRemovalException(
                $"拒绝删除链接：未获显式确认。如确认要删除链接本身，请加确认标志（--delete-link 需搭配 --yes）。");
        }

        var entryExists = registry.Entries.Any(e =>
            string.Equals(e.Source, source, StringComparison.OrdinalIgnoreCase)
            || string.Equals(e.Source, source.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase));
        if (!entryExists)
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

        if (LinkHealth.Check(source, "junction") == LinkStatus.Disguised)
        {
            throw new LinkRemovalException($"该路径不是链接而是普通目录/文件，拒绝删除：{source}。请检查登记条目是否过期。");
        }

        try
        {
            Directory.Delete(source, recursive: false);
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
}
