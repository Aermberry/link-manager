

namespace LinkMgr.Core.Health;

/// <summary>链接状态的人类可读表示。Core 层统一，CLI 与 GUI 共用。</summary>
public static class LinkStatusText
{
    public static string ToChinese(this LinkStatus status) => status switch
    {
        LinkStatus.Ok => "正常",
        LinkStatus.Broken => "断链",
        LinkStatus.Missing => "链接缺失",
        LinkStatus.Disguised => "伪装目录",
        _ => "未知",
    };

    /// <summary>状态颜色（GUI 用）。用 System.Drawing 的 Color 而非 WPF Brush，保持 Core 无 UI 框架依赖。</summary>
    public static (byte r, byte g, byte b) ToRgb(this LinkStatus status) => status switch
    {
        LinkStatus.Ok => (0, 128, 0),        // green
        LinkStatus.Broken => (255, 0, 0),    // red
        LinkStatus.Missing => (255, 165, 0), // orange
        LinkStatus.Disguised => (128, 128, 128), // gray
        _ => (0, 0, 0),
    };
}
