using LinkManager.Core;
using LinkManager.Core.Health;
using LinkManager.Core.Links;
using LinkManager.Core.Migration;
using LinkManager.Core.Registry;

if (args.Length == 0)
{
    Console.WriteLine($"{AppInfo.Name} {AppInfo.Version}");
    Console.WriteLine("当前可用命令：version, ls, create, migrate");
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

        case "migrate":
            return RunMigrate(args);

        default:
            Console.Error.WriteLine($"未知命令: {args[0]}（当前可用：version, ls, create, migrate）");
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
catch (MigrationException ex)
{
    Console.Error.WriteLine($"迁移失败：{ex.Message}");
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
    Console.WriteLine(LinkCreator.DescribeCreated(entry));
    return 0;
}

static int RunMigrate(string[] args)
{
    // 用法：migrate <src> <dst> [备注] [--execute]
    // dry-run 是默认行为：只输出完整执行计划，不做任何文件系统改动；
    // 显式 --execute 才实际执行迁移。
    var positional = new List<string>();
    var execute = false;
    foreach (var arg in args.Skip(1))
    {
        if (arg == "--execute")
        {
            execute = true;
        }
        else if (arg.StartsWith("--"))
        {
            Console.Error.WriteLine($"未知选项: {arg}（migrate 仅支持 --execute）");
            return 2;
        }
        else
        {
            positional.Add(arg);
        }
    }

    if (positional.Count < 2)
    {
        Console.Error.WriteLine("用法：migrate <源路径> <目标路径> [备注] [--execute]");
        Console.Error.WriteLine("默认 dry-run：只输出执行计划。加 --execute 才实际迁移。");
        return 2;
    }

    var source = positional[0];
    var target = positional[1];
    var note = positional.Count > 2 ? positional[2] : "";

    var runner = new MigrationRunner();
    var plan = MigrationPlanner.Plan(source, target, note: note);

    if (!execute)
    {
        var dryRun = runner.DryRun(plan);
        Console.WriteLine(dryRun.Report);
        return dryRun.Ok ? 0 : 1;
    }

    var result = runner.Run(plan);
    Console.WriteLine(result.Report);
    return 0;
}
