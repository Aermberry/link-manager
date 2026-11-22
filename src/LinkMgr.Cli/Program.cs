using LinkMgr.Core;
using LinkMgr.Core.Health;
using LinkMgr.Core.Links;
using LinkMgr.Core.Registry;

if (args.Length == 0)
{
    Console.WriteLine($"{AppInfo.Name} {AppInfo.Version}");
    Console.WriteLine("当前可用命令：version, ls, create");
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

        case "create":
            return RunCreate(args);

        default:
            Console.Error.WriteLine($"未知命令: {args[0]}（当前可用：version, ls, create）");
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
catch (LinkCreateException ex)
{
    Console.Error.WriteLine($"创建失败：{ex.Message}");
    return 1;
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

        Console.WriteLine($"{status.ToChinese(),-8}  {e.Source,-45} → {e.Target,-45}  {e.Type,-10}  {e.Note}");
    }

    return abnormal > 0 ? 1 : 0;
}

static int RunCreate(string[] args)
{
    if (args.Length < 3)
    {
        Console.Error.WriteLine("用法：create <源路径> <目标路径> [备注]");
        return 2;
    }

    var source = args[1];
    var target = args[2];
    var note = args.Length > 3 ? args[3] : "";

    var entry = LinkCreator.CreateJunction(source, target, note);
    Console.WriteLine($"已创建 junction：{target} → {source}（链接 {entry.Source} → 真实 {entry.Target}）");
    Console.WriteLine("已自动登记。可用 `ls` 查看。");
    return 0;
}
