using LinkManager.Core.Health;
using LinkManager.Core.Migration;
using LinkManager.Core.Registry;

namespace LinkManager.Core.Links;

/// <summary>登记链接无法安全重建时抛出，消息可直接展示给中文用户。</summary>
public sealed class LinkRepairException(string message) : Exception(message);

/// <summary>断链重建结果：原登记条目及验证后的健康状态。</summary>
public sealed record LinkRepairResult(LinkEntry Entry, LinkStatus Status);

/// <summary>
/// 按登记表安全重建缺失的 junction 或 symlink。
/// 重建不会删除真实数据或修改登记表；仅在链接位置无占用时创建新的链接。
/// </summary>
public static class LinkRepairer
{
    /// <summary>检查并重建指定登记条目。登记条目不存在、数据目标不存在、源位置有占用或相关进程运行时拒绝执行。</summary>
    public static LinkRepairResult Repair(string source, string? registryRoot = null,
        IProcessProbe? processProbe = null, IDeveloperModeDetector? developerModeDetector = null)
    {
        var registry = LinkRegistry.Load(registryRoot);
        var entry = registry.FindEntry(source)
            ?? throw new LinkRepairException($"未找到该源路径的登记条目：{source}（可用 ls 查看现有条目）");

        ValidateEntry(entry, processProbe);

        try
        {
            switch (entry.Type)
            {
                case "junction":
                    LinkCreator.RecreateJunction(entry.Target, entry.Source);
                    break;
                case "symlink":
                    LinkCreator.RecreateSymlink(entry.Target, entry.Source, developerModeDetector);
                    break;
                default:
                    throw new LinkRepairException($"登记条目类型不支持修复：{entry.Type}（仅支持 junction 或 symlink）");
            }
        }
        catch (InvalidOperationException ex)
        {
            throw new LinkRepairException($"修复被拒绝：{ex.Message}");
        }
        catch (LinkCreateException ex)
        {
            throw new LinkRepairException($"修复失败：{ex.Message}");
        }

        var status = LinkHealth.Check(entry.Source, entry.Type);
        if (status != LinkStatus.Ok)
        {
            throw new LinkRepairException($"修复后健康检查未恢复正常：{status.ToChinese()}。请检查链接位置和目标路径。");
        }

        return new LinkRepairResult(entry, status);
    }

    private static void ValidateEntry(LinkEntry entry, IProcessProbe? processProbe)
    {
        if (!Directory.Exists(entry.Target))
        {
            throw new LinkRepairException($"数据真实位置不存在，无法修复：{entry.Target}");
        }

        if (File.Exists(entry.Source))
        {
            throw new LinkRepairException($"链接位置被同名文件占用，拒绝覆盖：{entry.Source}");
        }

        if (Directory.Exists(entry.Source) && Directory.EnumerateFileSystemEntries(entry.Source).Any())
        {
            throw new LinkRepairException($"链接位置目录非空，拒绝覆盖：{entry.Source}。请先清空或移走该目录。");
        }

        var running = (processProbe ?? new DefaultProcessProbe()).FindRunning(DefaultProcessProbe.DefaultWatchList);
        if (running.Count > 0)
        {
            throw new LinkRepairException($"相关进程仍在运行，拒绝修复：{string.Join("、", running.Select(p => p.Name))}");
        }
    }
}
