using LinkManager.Core;
using LinkManager.Core.Health;
using LinkManager.Core.Links;
using LinkManager.Core.Registry;
using LinkManager.Core.Settings;

if (args.Length == 0)
{
    Console.WriteLine($"{AppInfo.Name} {AppInfo.Version}");
    Console.WriteLine("当前可用命令：version, ls, create, config, note, remove");
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

        case "config":
            return RunConfig(args);

        case "note":
            return RunNote(args);

        case "remove":
            return RunRemove(args);

        default:
            Console.Error.WriteLine($"未知命令: {args[0]}（当前可用：version, ls, create, config, note, remove）");
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
catch (SettingsCorruptException ex)
{
    Console.Error.WriteLine($"设置文件错误：{ex.Message}");
    return 3;
}
catch (SettingsIoException ex)
{
    Console.Error.WriteLine($"设置文件读写失败：{ex.Message}");
    return 3;
}
catch (RegistryMigrationException ex)
{
    Console.Error.WriteLine($"登记迁移失败：{ex.Message}");
    return 3;
}
catch (LinkCreateException ex)
{
    Console.Error.WriteLine($"创建失败：{ex.Message}");
    return 1;
}
catch (LinkRemovalException ex)
{
    Console.Error.WriteLine($"删除失败：{ex.Message}");
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

static int RunConfig(string[] args)
{
    // config            —— 显示当前登记位置
    // config set-registry <新登记文件路径> —— 迁移登记到新位置并记住
    if (args.Length == 1)
    {
        ShowRegistryLocation();
        return 0;
    }

    if (args.Length == 3 && args[1] == "set-registry")
    {
        return RunSetRegistry(args[2]);
    }

    Console.Error.WriteLine("用法：config | config set-registry <新登记文件路径>");
    return 2;
}

static void ShowRegistryLocation()
{
    var settings = LinkSettings.Load();
    Console.WriteLine($"登记文件位置：{LinkRegistry.ResolveRegistryFilePath(settings)}");
    if (settings.RegistryFilePath is not null)
    {
        Console.WriteLine($"（自定义：{settings.RegistryFilePath}；默认：{LinkRegistry.DefaultFilePath}）");
    }
    else
    {
        Console.WriteLine("（使用默认位置，可用 config set-registry <路径> 修改）");
    }
}

static int RunSetRegistry(string newPath)
{
    var settings = LinkSettings.Load();
    var current = LinkRegistry.ResolveRegistryFilePath(settings);

    Console.WriteLine($"当前登记位置：{current}");
    Console.WriteLine($"新登记位置：  {Path.GetFullPath(newPath)}");

    if (File.Exists(newPath))
    {
        Console.Error.WriteLine("新位置已存在登记文件，拒绝覆盖。请换一个路径或先移走该文件。");
        return 2;
    }

    RegistryMigrator.RelocateRegistry(newPath);
    Console.WriteLine("已迁移：旧数据搬到新位置，设置已更新。");
    Console.WriteLine($"登记文件位置：{LinkRegistry.ResolveRegistryFilePath(LinkSettings.Load())}");
    return 0;
}

static int RunNote(string[] args)
{
    // note <源路径> <新备注>
    if (args.Length < 3)
    {
        Console.Error.WriteLine("用法：note <源路径> <新备注>（备注传空串 \"\" 可清除）");
        return 2;
    }

    var source = args[1];
    var note = args[2];

    var registry = LinkRegistry.Load();
    if (!registry.UpdateNote(source, note))
    {
        Console.Error.WriteLine($"未找到该源路径的登记条目：{source}（可用 ls 查看现有条目）");
        return 2;
    }
    registry.Save();

    Console.WriteLine($"备注已更新：{source} → \"{note}\"");
    return 0;
}

static int RunRemove(string[] args)
{
    // remove <源路径> --keep-link            仅删登记
    // remove <源路径> --delete-link [--yes]  连同链接一起删（--yes 显式确认）
    if (args.Length < 2)
    {
        Console.Error.WriteLine("用法：remove <源路径> --keep-link | remove <源路径> --delete-link --yes");
        return 2;
    }

    var source = args[1];
    var keepLink = false;
    var deleteLink = false;
    var confirmed = false;
    foreach (var arg in args.Skip(2))
    {
        switch (arg)
        {
            case "--keep-link":
                keepLink = true;
                break;
            case "--delete-link":
                deleteLink = true;
                break;
            case "--yes":
                confirmed = true;
                break;
            default:
                Console.Error.WriteLine($"未知参数：{arg}。用法：remove <源路径> --keep-link | remove <源路径> --delete-link --yes");
                return 2;
        }
    }

    if (keepLink == deleteLink)
    {
        Console.Error.WriteLine("必须且只能选一种模式：--keep-link（仅删登记）或 --delete-link（连同链接一起删，需 --yes 确认）");
        return 2;
    }

    var registry = LinkRegistry.Load();

    if (keepLink)
    {
        var removed = LinkRemover.RemoveEntryOnly(registry, source);
        if (!removed)
        {
            Console.Error.WriteLine($"未找到该源路径的登记条目：{source}（可用 ls 查看现有条目）");
            return 2;
        }
        Console.WriteLine($"已删除登记条目（文件系统链接保留）：{source}");
        return 0;
    }

    if (!confirmed)
    {
        Console.Error.WriteLine($"拒绝删除链接：--delete-link 需搭配 --yes 显式确认。链接删除后真实数据目录不受影响。");
        return 2;
    }

    var removedLink = LinkRemover.RemoveWithLink(registry, source, confirmDeleteLink: true);
    if (!removedLink)
    {
        Console.Error.WriteLine($"未找到该源路径的登记条目：{source}（可用 ls 查看现有条目）");
        return 2;
    }
    Console.WriteLine($"已删除链接与登记条目（真实数据目录不受影响）：{source}");
    return 0;
}
