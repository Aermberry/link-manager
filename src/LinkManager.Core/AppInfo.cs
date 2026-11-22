namespace LinkManager.Core;

/// <summary>产品级元数据：名称与版本，CLI 与 GUI 共用。</summary>
public static class AppInfo
{
    public const string Name = "link-manager";

    public static string Version =>
        typeof(AppInfo).Assembly.GetName().Version!.ToString(3);
}
