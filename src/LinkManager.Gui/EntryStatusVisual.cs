using LinkManager.Core.Health;

namespace LinkManager.Gui;

/// <summary>
/// 健康状态 → Fluent 浅色视觉映射（状态列图标符号 + 文字颜色）。
/// 纯映射、无 WPF 控件依赖，可单测；Brush 构造在 <see cref="EntryRow"/>。
/// </summary>
public static class EntryStatusVisual
{
    /// <summary>状态列图标符号：✓ 正常 / ✗ 断链 / ⚠ 缺失 / ◇ 伪装。</summary>
    public static string Glyph(LinkStatus status) => status switch
    {
        LinkStatus.Ok => "✓",
        LinkStatus.Broken => "✗",
        LinkStatus.Missing => "⚠",
        LinkStatus.Disguised => "◇",
        _ => "·",
    };

    /// <summary>状态文字颜色（Fluent 浅色色板：绿 / 危险红 / 橙 / 灰）。</summary>
    public static (byte r, byte g, byte b) Color(LinkStatus status) => status switch
    {
        LinkStatus.Ok => (0x0F, 0x7B, 0x0F),        // Fluent green
        LinkStatus.Broken => (0xC4, 0x2B, 0x1C),    // danger red（与向导危险步一致）
        LinkStatus.Missing => (0xCA, 0x50, 0x10),   // orange
        LinkStatus.Disguised => (0x5D, 0x5D, 0x63), // subtle gray
        _ => (0x1B, 0x1B, 0x1F),
    };
}
