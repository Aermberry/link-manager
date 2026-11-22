using LinkMgr.Core;
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

    Console.WriteLine($"{"源路径",-45} → {"目标路径",-45}  {"类型",-10}  备注");
    foreach (var e in registry.Entries)
    {
        Console.WriteLine($"{e.Source,-45} → {e.Target,-45}  {e.Type,-10}  {e.Note}");
    }

    return 0;
}
