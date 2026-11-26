using LinkManager.Core.Health;
using LinkManager.Core.Links;
using LinkManager.Core.Registry;

namespace LinkManager.Gui;

/// <summary>GUI 修复入口的纯逻辑：只允许真实断链（目标已消失、链接位置仍是链接）触发重建。</summary>
public static class RepairDialogLogic
{
    public static bool CanRepair(LinkStatus status) => status == LinkStatus.Broken;

    public static (string Title, string Message) Confirmation(LinkEntry entry) =>
        ("确认修复断链", $"将按登记信息重建 {entry.Type}。\n\n链接位置（源）：{entry.Source}\n数据真实位置（目标）：{entry.Target}\n类型：{entry.Type}\n\n不会删除真实数据或修改登记表。是否继续？");

    public static string Success(LinkEntry entry) =>
        $"修复成功\n\n已重建 {entry.Type}：\n{entry.Source} → {entry.Target}\n\n健康检查已恢复正常。";
}
