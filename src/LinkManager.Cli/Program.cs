using LinkManager.Core;
using LinkManager.Core.Health;
using LinkManager.Core.Links;
using LinkManager.Core.Registry;

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
    var positional = args.Skip(1).Where(a => a != "--symlink").ToArray();
    var useSymlink = args.Skip(1).Contains("--symlink");

    if (positional.Length < 2)
    {
        Console.Error.WriteLine("用法：create <源路径> <目标路径> [备注] [--symlink]");
        return 2;
    }

    var source = positional[0];
    var target = positional[1];
    var note = positional.Length > 2 ? positional[2] : "";

    try
    {
        var entry = useSymlink
            ? LinkCreator.CreateSymlink(source, target, note)
            : LinkCreator.CreateJunction(source, target, note);
        Console.WriteLine(LinkCreator.DescribeCreated(entry));
        return 0;
    }
    catch (InvalidOperationException ex)
    {
        // 开发者模式门槛：拒绝创建并给出中文指引（exit 2，用法/前置条件类错误）
        Console.Error.WriteLine($"创建被拒绝：{ex.Message}");
        return 2;
    }
}
