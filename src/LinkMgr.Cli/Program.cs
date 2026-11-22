using LinkMgr.Core;

if (args.Length == 0)
{
    Console.WriteLine($"{AppInfo.Name} {AppInfo.Version}");
    Console.WriteLine("交互向导尚未实现（T2 起），当前可用命令：version");
    return 0;
}

switch (args[0])
{
    case "version":
        Console.WriteLine($"{AppInfo.Name} {AppInfo.Version}");
        return 0;
    default:
        Console.Error.WriteLine($"未知命令: {args[0]}（当前可用：version）");
        return 2;
}
