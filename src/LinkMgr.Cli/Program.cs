using LinkMgr.Core;
using LinkMgr.Core.Health;
using LinkMgr.Core.Registry;

if (args.Length == 0)
{
    Console.WriteLine($"{AppInfo.Name} {AppInfo.Version}");
    Console.WriteLine("交互向导尚未实现（T3 起），当前可用命令：version, ls");
    return 0;
}

try
{
    switch (args[0])
    {
        case "version":
            Console.WriteLine($"{AppInfo.Name} {AppInfo.Version}");
            return 0;

        case "ls":
            return RunLs();

        default:
            Console.Error.WriteLine($"未知命令: {args[0]}（当前可用：version, ls）");
            return 2;
    }
}
catch (RegistryCorruptException ex)
{
    Console.Error.WriteLine($"登记文件错误：{ex.Message}");
    return 3;
}
catch (RegistryIoException ex)
{
    Console.Error.WriteLine($"登记文件读写失败：{ex.Message}");
    return 3;
}

static int RunLs()
{
    var registry = LinkRegistry.Load();

    if (registry.Entries.Count == 0)
    {
        Console.WriteLine("（登记表为空——尚未登记任何链接）");
        return 0;
    }

    Console.WriteLine($"{"状态",-8}  {"源路径",-45} → {"目标路径",-45}  {"类型",-10}  备注");
    var abnormal = 0;
    foreach (var e in registry.Entries)
    {
        var status = LinkHealth.Check(e.Source, e.Type);
        if (status != LinkStatus.Ok)
        {
            abnormal++;
        }

        var statusText = status switch
        {
            LinkStatus.Ok => "正常",
            LinkStatus.Broken => "断链",
            LinkStatus.Missing => "链接缺失",
            LinkStatus.Disguised => "伪装目录",
            _ => "未知",
        };
        Console.WriteLine($"{statusText,-8}  {e.Source,-45} → {e.Target,-45}  {e.Type,-10}  {e.Note}");
    }

    return abnormal > 0 ? 1 : 0;
}
