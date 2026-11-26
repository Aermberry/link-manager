namespace LinkManager.Gui;

/// <summary>扫描对话框纯逻辑（无 WPF 依赖，可单测）。</summary>
public static class ScanDialogLogic
{
    /// <summary>
    /// 校验深度输入：空 = 不限（null）；非负整数有效；其余返回中文错误原因。
    /// </summary>
    public static int? ParseDepth(string? text, out string? error)
    {
        error = null;
        var depthText = (text ?? "").Trim();
        if (depthText.Length == 0)
        {
            return null;
        }

        if (!int.TryParse(depthText, out var depth) || depth < 0)
        {
            error = "深度需为非负整数：0 = 只扫这一层，1 = 连一层子目录；留空 = 不限深度。";
            return null;
        }

        return depth;
    }

    /// <summary>扫描开始前的目录校验：目录不存在返回中文提示。</summary>
    public static string? ValidateRoot(string? text)
    {
        var root = (text ?? "").Trim();
        if (root.Length == 0 || !System.IO.Directory.Exists(root))
        {
            return $"扫描目录不存在：{root}";
        }
        return null;
    }

    /// <summary>
    /// 扫描完成后的汇总行（含已登记跳过、无权限跳过、空状态文案）。
    /// </summary>
    public static string SummaryLine(int found, int registeredCount, int scannedDirectories,
        int skipped, bool hasImportedThisSession)
    {
        var skippedNote = skipped > 0 ? $"，{skipped} 个目录无权限跳过" : "";
        if (hasImportedThisSession)
        {
            return $"已登记 {found} 条。";
        }
        return found > 0
            ? $"发现 {found} 条待登记链接（已登记 {registeredCount} 条不再列出，共扫过 {scannedDirectories} 个目录{skippedNote}）。勾选后点「登记所选」。"
            : $"未发现待登记的链接（已登记 {registeredCount} 条，共扫过 {scannedDirectories} 个目录{skippedNote}）。";
    }

    /// <summary>
    /// 扫描空状态（列表下覆盖提示）的标题与说明；null = 非空状态。
    /// 空状态分两种：未扫描过（引导去扫描）与扫描完无结果（说明目录里没有未登记链接）。
    /// </summary>
    public static (string title, string detail)? EmptyState(bool hasScannedThisSession, int foundCount)
    {
        if (foundCount > 0)
        {
            return null;
        }
        return hasScannedThisSession
            ? ("该目录下没有待登记的链接",
                "扫描完成：这里没有发现未登记的 junction / symlink。已登记的链接不会重复列出。\n想登记别的目录？换一个目录再扫。")
            : ("还没有扫描结果",
                "选择要扫描的目录后点「开始扫描」。发现的链接会列在这里，勾选后可批量登记。");
    }

    /// <summary>登记按钮可用性：未扫描或列表为空时禁用。</summary>
    public static bool ImportEnabled(bool hasScannedThisSession, int candidateCount)
        => hasScannedThisSession && candidateCount > 0;

    /// <summary>登记完成后的汇总行（部分条目已在表中时说明未重复登记）。</summary>
    public static string ImportedLine(int imported, int attempted)
        => imported == attempted
            ? $"已登记 {imported} 条。"
            : $"已登记 {imported} 条（其余已在登记表中，未重复登记）。";
}
