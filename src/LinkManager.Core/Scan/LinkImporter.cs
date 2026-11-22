using LinkManager.Core.Registry;

namespace LinkManager.Core.Scan;

/// <summary>
/// 扫描结果导入（issue #15）：把候选链接写入登记表。导入时逐条再次查重——
/// 扫描与导入之间登记表可能变化（并发 CLI/GUI），保证不产生重复条目。
/// 逐条落盘：中途失败不丢已导入的条目。非交互全量与交互逐条问询共用。
/// </summary>
public static class LinkImporter
{
    /// <summary>导入全部候选；返回实际新登记的条数。已登记的源路径被跳过。</summary>
    public static int Import(ScanResult result, LinkRegistry registry)
    {
        var imported = 0;
        foreach (var candidate in result.Links)
        {
            if (ImportOne(candidate, registry))
            {
                imported++;
            }
        }

        return imported;
    }

    /// <summary>登记单个候选；源路径已登记时拒绝（不覆盖）并返回 false。</summary>
    public static bool ImportOne(ScannedLink candidate, LinkRegistry registry)
    {
        if (registry.ContainsEntry(candidate.Source))
        {
            return false;
        }

        registry.Add(new LinkEntry
        {
            Source = candidate.Source,
            Target = candidate.Target,
            Type = candidate.Type,
        });
        registry.Save();
        return true;
    }
}
