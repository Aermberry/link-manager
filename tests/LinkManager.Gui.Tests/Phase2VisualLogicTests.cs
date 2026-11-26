using LinkManager.Core.Health;
using LinkManager.Gui;

namespace LinkManager.Gui.Tests;

/// <summary>
/// v1.0.7 phase2 视觉层纯逻辑回归：健康状态图标/颜色映射、主窗口状态栏文案、
/// 扫描对话框校验/汇总/空状态。视觉映射是状态列唯一数据源，错了整个列表跟着错。
/// </summary>
public class Phase2VisualLogicTests
{
    // ---- EntryStatusVisual：状态列图标 + 颜色 ----

    [Theory]
    [InlineData(LinkStatus.Ok, "✓")]
    [InlineData(LinkStatus.Broken, "✗")]
    [InlineData(LinkStatus.Missing, "⚠")]
    [InlineData(LinkStatus.Disguised, "◇")]
    public void Glyph_maps_each_health_status(LinkStatus status, string expected)
        => Assert.Equal(expected, EntryStatusVisual.Glyph(status));

    [Fact]
    public void Broken_and_missing_use_danger_family_colors_not_pure_rgb()
    {
        // Fluent 色板（红 #C42B1C / 橙 #CA5010），不再是 phase1 前的纯红/纯橙
        var broken = EntryStatusVisual.Color(LinkStatus.Broken);
        Assert.Equal((0xC4, 0x2B, 0x1C), broken);
        var missing = EntryStatusVisual.Color(LinkStatus.Missing);
        Assert.Equal((0xCA, 0x50, 0x10), missing);
    }

    [Fact]
    public void Ok_is_green_and_disguised_is_subtle_gray()
    {
        Assert.Equal((0x0F, 0x7B, 0x0F), EntryStatusVisual.Color(LinkStatus.Ok));
        Assert.Equal((0x5D, 0x5D, 0x63), EntryStatusVisual.Color(LinkStatus.Disguised));
    }

    // ---- MainWindowLogic：状态栏一行 ----

    [Fact]
    public void StatusLine_reports_all_ok_with_path()
        => Assert.Equal("3 条登记，全部正常 · C:\\reg\\registry.json",
            MainWindowLogic.StatusLine(3, 0, "C:\\reg\\registry.json"));

    [Fact]
    public void StatusLine_reports_broken_count()
        => Assert.Equal("2 条登记，1 条异常 · C:\\reg\\registry.json",
            MainWindowLogic.StatusLine(2, 1, "C:\\reg\\registry.json"));

    // ---- ScanDialogLogic：深度/目录校验 ----

    [Theory]
    [InlineData("", null)]
    [InlineData("  ", null)]
    [InlineData("0", 0)]
    [InlineData("2", 2)]
    public void ParseDepth_accepts_blank_and_nonnegative_ints(string text, int? expected)
    {
        var depth = ScanDialogLogic.ParseDepth(text, out var error);
        Assert.Null(error);
        Assert.Equal(expected, depth);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("1.5")]
    public void ParseDepth_rejects_negative_and_noninteger_with_chinese_error(string text)
    {
        var depth = ScanDialogLogic.ParseDepth(text, out var error);
        Assert.Null(depth);
        Assert.NotNull(error);
        Assert.Contains("非负整数", error);
    }

    [Fact]
    public void ValidateRoot_rejects_missing_directory()
    {
        var error = ScanDialogLogic.ValidateRoot(@"Z:\no-such-dir-8f21");
        Assert.NotNull(error);
        Assert.Contains("扫描目录不存在", error);
    }

    // ---- ScanDialogLogic：汇总与空状态 ----

    [Fact]
    public void SummaryLine_found_shows_counts_and_hint()
        => Assert.Equal(
            "发现 2 条待登记链接（已登记 1 条不再列出，共扫过 5 个目录，3 个目录无权限跳过）。勾选后点「登记所选」。",
            ScanDialogLogic.SummaryLine(2, 1, 5, 3, hasImportedThisSession: false));

    [Fact]
    public void SummaryLine_empty_says_no_links_found()
        => Assert.Equal(
            "未发现待登记的链接（已登记 4 条，共扫过 9 个目录）。",
            ScanDialogLogic.SummaryLine(0, 4, 9, 0, hasImportedThisSession: false));

    [Fact]
    public void EmptyState_hidden_when_rows_exist()
        => Assert.Null(ScanDialogLogic.EmptyState(hasScannedThisSession: true, foundCount: 2));

    [Fact]
    public void EmptyState_before_scan_guides_to_start()
    {
        var state = ScanDialogLogic.EmptyState(hasScannedThisSession: false, foundCount: 0);
        Assert.NotNull(state);
        Assert.Contains("还没有扫描结果", state.Value.title);
        Assert.Contains("开始扫描", state.Value.detail);
    }

    [Fact]
    public void EmptyState_after_scan_without_results_says_directory_clean()
    {
        var state = ScanDialogLogic.EmptyState(hasScannedThisSession: true, foundCount: 0);
        Assert.NotNull(state);
        Assert.Contains("没有待登记的链接", state.Value.title);
        Assert.Contains("不会重复列出", state.Value.detail);
    }

    [Theory]
    [InlineData(false, 0, false)]
    [InlineData(true, 0, false)]
    [InlineData(false, 3, false)]
    [InlineData(true, 3, true)]
    public void ImportEnabled_requires_scan_and_rows(bool scanned, int rows, bool expected)
        => Assert.Equal(expected, ScanDialogLogic.ImportEnabled(scanned, rows));

    [Fact]
    public void ImportedLine_distinguishes_partial_import()
    {
        Assert.Equal("已登记 2 条。", ScanDialogLogic.ImportedLine(2, 2));
        Assert.Equal("已登记 1 条（其余已在登记表中，未重复登记）。",
            ScanDialogLogic.ImportedLine(1, 2));
    }
}
