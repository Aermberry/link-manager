using LinkManager.Core;
using LinkManager.Core.CliOutput;
using LinkManager.Core.Health;
using LinkManager.Core.Links;
using LinkManager.Core.Migration;
using LinkManager.Core.Registry;
using LinkManager.Core.Scan;
using LinkManager.Core.Settings;

// 退出码约定（docs/cli-conventions.md）：0 成功；1 用户错误（用法/参数）；2 执行失败；
// 3 登记表错误；4 仅 ls——成功但发现异常链接；64 未知命令。

// 输出统一 UTF-8：重定向/管道下 Windows 默认跟随系统 OEM 码页（GBK），会把中文备注/
// 错误消息变成乱码字节流。stdout 与 stderr 都要设置——文本模式失败消息走 stderr
//（stderr 是 TextWriter 包装，编码经 Console.Error 派生，无需单独设置；stdout 的
// OutputEncoding 设置在 Windows 上同时覆盖两者）。
// 输出统一 UTF-8：重定向/管道下 Windows 默认跟随系统 OEM 码页（GBK），中文备注/错误
// 消息会变成乱码字节流。仅设 Console.OutputEncoding 即可——stderr 的编码随 stdout
// 派生（已实测：设置后 stderr 为 UTF-8，注释掉该行则变 GBK）。
try
{
    Console.OutputEncoding = System.Text.Encoding.UTF8;
}
catch (IOException) { /* 极端宿主不支持时保持默认 */ }

if (args.Length == 0)
{
    // 交互向导是后续工单范围；当前无参数时打印帮助立即退出，绝不挂起等待输入。
    Console.WriteLine($"{AppInfo.Name} {AppInfo.Version}");
    Console.WriteLine("当前可用命令：version, ls, create, scan, migrate, config, note, remove, help（通用选项：--json, --registry <根目录>）");
    return 0;
}

var commandName = args[0];
var options = CliArgParser.Parse(args);

if (options.ParseError is not null)
{
    return Fail(options.Json, commandName, ExitCodes.UserError, options.ParseError);
}

try
{
    switch (commandName)
    {
        case "version":
            return RunVersion(options.Json);

        case "ls":
            return RunLs(options.Json, options.RegistryRoot);

        case "create":
            return RunCreate(options.Positional, options.CommandFlags, options.Json, options.RegistryRoot);

        case "scan":
            return RunScan(options.Positional, options.CommandFlags, options.CommandFlagValues, options.Json, options.RegistryRoot);

        case "help" or "--help" or "-h":
            return RunHelp(options.Json);

        case "migrate":
            return RunMigrate(options.Positional, options.CommandFlags, options.Json, options.RegistryRoot);

        case "config":
            return RunConfig(options.Positional, options.CommandFlags, options.Json, options.RegistryRoot);

        case "note":
            return RunNote(options.Positional, options.Json, options.RegistryRoot);

        case "remove":
            return RunRemove(options.Positional, options.CommandFlags, options.Json, options.RegistryRoot);

        default:
            return Fail(options.Json, commandName, ExitCodes.UnknownCommand,
                $"未知命令: {commandName}（可用：version, ls, create, scan, migrate, config, note, remove, help）");
    }
}
catch (RegistryCorruptException ex)
{
    return Fail(options.Json, commandName, ExitCodes.RegistryError, ex.Message);
}
catch (RegistryIoException ex)
{
    return Fail(options.Json, commandName, ExitCodes.RegistryError, ex.Message);
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
{
    // 登记目录无法创建/读写（坏盘符、权限）等原始 IO 异常，归入登记表错误
    return Fail(options.Json, commandName, ExitCodes.RegistryError, $"登记表读写失败：{ex.Message}");
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
    return Fail(options.Json, commandName, ExitCodes.ExecutionFailed, ex.Message);
}
catch (MigrationException ex)
{
    // migrate --execute 的失败出口（护栏拒绝/复制核对失败/登记失败已回滚）：
    // 文本模式走 stderr 报告（沿用既有约定），--json 走失败包络（stdout 单行 JSON）
    return Fail(options.Json, commandName, ExitCodes.ExecutionFailed, $"迁移失败：{ex.Message}");
}
catch (LinkRemovalException ex)
{
    return Fail(options.Json, commandName, ExitCodes.ExecutionFailed, $"删除失败：{ex.Message}");
}

static int RunVersion(bool json)
{
    if (json)
    {
        Console.WriteLine(CliJson.Serialize(CliJson.Version()));
        return ExitCodes.Success;
    }

    Console.WriteLine($"{AppInfo.Name} {AppInfo.Version}");
    return ExitCodes.Success;
}

static int RunLs(bool json, string? registryRoot)
{
    var registry = LinkRegistry.Load(registryRoot);
    var result = CliJson.Ls(registry);

    if (json)
    {
        Console.WriteLine(CliJson.Serialize(result));
        return result.Data!.Registry.AbnormalCount > 0 ? ExitCodes.AbnormalLinks : ExitCodes.Success;
    }

    if (registry.Entries.Count == 0)
    {
        Console.WriteLine("（登记表为空——尚未登记任何链接）");
        return ExitCodes.Success;
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

    return abnormal > 0 ? ExitCodes.AbnormalLinks : ExitCodes.Success;
}

static int RunCreate(IReadOnlyList<string> positional, IReadOnlyList<string> commandFlags, bool json, string? registryRoot)
{
    var useSymlink = commandFlags.Any(a => string.Equals(a, "--symlink", StringComparison.OrdinalIgnoreCase));

    if (positional.Count < 2)
    {
        return Fail(json, "create", ExitCodes.UserError, "用法：create <源路径> <目标路径> [备注] [--symlink] [--json] [--registry <根目录>]");
    }

    var source = positional[0];
    var target = positional[1];
    var note = positional.Count > 2 ? positional[2] : "";

    try
    {
        var entry = useSymlink
            ? LinkCreator.CreateSymlink(source, target, note)
            : LinkCreator.CreateJunction(source, target, note, registryRoot);

        if (json)
        {
            Console.WriteLine(CliJson.Serialize(CliJson.Create(entry)));
        }
        else
        {
            Console.WriteLine(LinkCreator.DescribeCreated(entry));
        }

        return ExitCodes.Success;
    }
    catch (InvalidOperationException ex)
    {
        // 开发者模式门槛：拒绝创建并给出中文指引（exit 2，用法/前置条件类错误）
        return Fail(json, "create", ExitCodes.UserError, $"创建被拒绝：{ex.Message}");
    }
}
/// <summary>scan 子命令（issue #15）：扫描目录树发现存量 junction/symlink。
/// --yes 非交互全量导入；默认交互模式逐条问询「要登记吗」（stdin 不可读时自动降级为只报告，
/// 绝不挂起——非交互保证）。退出码：1 用法错误；2 目录不存在；3 登记表错误（全局捕获）。</summary>
static int RunScan(IReadOnlyList<string> positional, IReadOnlyList<string> commandFlags,
    IReadOnlyDictionary<string, string> commandFlagValues, bool json, string? registryRoot)
{
    var yes = commandFlags.Any(a => string.Equals(a, "--yes", StringComparison.OrdinalIgnoreCase));
    int? maxDepth = null;
    if (commandFlagValues.TryGetValue("--max-depth", out var depthValue))
    {
        if (!int.TryParse(depthValue, out var parsed) || parsed < 0)
        {
            return Fail(json, "scan", ExitCodes.UserError, "--max-depth 需要一个非负整数：--max-depth <层数> 或 --max-depth=<层数>（0 = 只扫第一层）");
        }

        maxDepth = parsed;
    }

    if (positional.Count != 1)
    {
        return Fail(json, "scan", ExitCodes.UserError, "用法：scan <目录> [--yes] [--max-depth <层数>] [--json] [--registry <根目录>]");
    }

    var dir = positional[0];
    if (!Directory.Exists(dir))
    {
        return Fail(json, "scan", ExitCodes.UserError, $"目录不存在：{dir}");
    }

    var registry = LinkRegistry.Load(registryRoot);
    var result = LinkScanner.Scan(dir, registry, maxDepth: maxDepth ?? -1);

    // 无权限目录的汇总：--json 模式只进 data.skipped（包络契约：stdout 仅一行 JSON）；
    // 文本模式把跳过明细写到 stderr（文本模式的错误通道，见 docs/cli-conventions.md）。
    if (!json)
    {
        foreach (var skipped in result.Skipped)
        {
            Console.Error.WriteLine($"无权限访问，已跳过：{skipped}");
        }
    }

    if (result.Links.Count == 0)
    {
        var message = $"扫描完成：未发现待登记的链接（已登记 {result.RegisteredCount} 条）。";
        if (json)
        {
            Console.WriteLine(CliJson.Serialize(CliJson.Scan(result)));
        }
        else
        {
            Console.WriteLine(message);
        }

        return ExitCodes.Success;
    }

    if (yes)
    {
        // 非交互全量导入：跳过问询
        var imported = LinkImporter.Import(result, registry);
        return EmitScanDone(json, result, imported);
    }

    // 交互问询：stdin 不可读（重定向/管道/CI）时绝不挂起，降级为只报告
    if (!CanPrompt())
    {
        Console.Error.WriteLine("stdin 不可读，跳过问询。非交互全量导入请加 --yes。");
        if (json)
        {
            Console.WriteLine(CliJson.Serialize(CliJson.Scan(result)));
        }
        else
        {
            ReportScanText(result);
        }

        return ExitCodes.Success;
    }

    // 交互问询：全部对话输出走 stderr，保证 --json 模式 stdout 仍只有一行包络
    var count = 0;
    foreach (var candidate in result.Links)
    {
        Console.Error.WriteLine($"[{count + 1}/{result.Links.Count}] {candidate.Type}：{candidate.Source} → {candidate.Target}");
        Console.Error.Write("要登记吗？[y/N] ");
        var answer = Console.ReadLine()?.Trim().ToLowerInvariant();
        if (answer is "y" or "yes")
        {
            if (LinkImporter.ImportOne(candidate, registry))
            {
                Console.Error.WriteLine("  已登记。");
                count++;
            }
            else
            {
                Console.Error.WriteLine("  该链接已在登记表中，跳过。");
            }
        }
        else
        {
            Console.Error.WriteLine("  跳过。");
        }
    }

    return EmitScanDone(json, result, count);
}

/// <summary>导入完成的统一出口：--json 输出含 importedCount 的扫描包络；文本输出中文汇总。</summary>
static int EmitScanDone(bool json, ScanResult result, int imported)
{
    if (json)
    {
        var envelope = CliJson.Scan(result);
        Console.WriteLine(CliJson.Serialize(envelope with
        {
            Data = envelope.Data! with { Summary = envelope.Data.Summary with { ImportedCount = imported } },
        }));
    }
    else
    {
        Console.WriteLine($"扫描完成：导入 {imported} 条，跳过 {result.Links.Count - imported} 条候选（已登记 {result.RegisteredCount} 条不重复出现）。");
        ReportScanText(result);
    }

    return ExitCodes.Success;
}

/// <summary>文本模式的候选清单输出（交互问询前/降级报告共用）。</summary>
static void ReportScanText(ScanResult result)
{
    var i = 1;
    foreach (var candidate in result.Links)
    {
        Console.WriteLine($"  [{i++}] {candidate.Type}：{candidate.Source} → {candidate.Target}");
    }
}

/// <summary>stdin 是否可交互读取（重定向/管道/关闭时 false）。用于非交互环境自动降级。</summary>
static bool CanPrompt()
{
    try
    {
        _ = Console.KeyAvailable;
        return true;
    }
    catch (InvalidOperationException)
    {
        return false;
    }
}

static int RunHelp(bool json)
{
    if (json)
    {
        var data = new
        {
            commands = new[]
            {
                new { name = "version", description = "打印名称与版本" },
                new { name = "ls", description = "列出登记链接与健康状态" },
                new { name = "create", description = "创建 junction 并自动登记" },
                new { name = "scan", description = "扫描目录树发现存量链接并问询登记（--yes 全量导入）" },
                new { name = "note", description = "更新登记条目备注" },
                new { name = "remove", description = "删除登记条目（--delete-link 连链接一起删）" },
                new { name = "config", description = "显示/迁移登记文件位置" },
                new { name = "migrate", description = "迁移目录并回指 junction（默认 dry-run）" },
                new { name = "help", description = "打印本帮助" },
            },
            options = new[] { "--json", "--registry <根目录>" },
        };
        Console.WriteLine(CliJson.Serialize(new CliEnvelope<object> { Ok = true, Command = "help", Data = data }));
        return ExitCodes.Success;
    }

    Console.WriteLine($"{AppInfo.Name} — Windows 链接（junction/symlink）管理");
    Console.WriteLine("用法：link-manager <命令> [参数] [--json] [--registry <根目录>]");
    Console.WriteLine("  version            打印名称与版本");
    Console.WriteLine("  ls                 列出登记链接与健康状态");
    Console.WriteLine("  create <源> <目标> [备注]  创建 junction 并自动登记");
    Console.WriteLine("  scan <目录> [--yes] [--max-depth <层数>]  扫描存量链接并问询登记（--yes 全量导入）");
    Console.WriteLine("  note <源> <新备注>  更新登记条目备注");
    Console.WriteLine("  remove <源> --keep-link | --delete-link [--yes]  删除登记条目/连链接");
    Console.WriteLine("  config | config set-registry <路径>  显示/迁移登记文件位置");
    Console.WriteLine("  migrate <源> <目标> [备注] [--execute]  迁移目录（默认 dry-run）");
    Console.WriteLine("  help               打印本帮助");
    Console.WriteLine("通用选项：--json 输出结构化 JSON；--registry <根目录> 重定向登记表位置（默认 %APPDATA%\\link-manager，agent 沙箱用）");
    Console.WriteLine("约定详情（退出码/JSON 形状）：docs/cli-conventions.md");
    return ExitCodes.Success;
}

/// <summary>失败出口：--json 输出结构化错误包络到 stdout；文本模式输出中文消息到 stderr。</summary>
static int Fail(bool json, string command, int exitCode, string message)
{
    if (json)
    {
        Console.WriteLine(CliJson.Serialize(CliJson.Error(command, CliErrorCodes.For(exitCode), message)));
    }
    else
    {
        Console.Error.WriteLine(message);
    }

    return exitCode;
}

/// <summary>
/// migrate 子命令（T5 收尾：从 raw args 改走全局解析器——--registry/--json 生效）。
/// dry-run 是默认行为：只输出完整执行计划，不做任何文件系统改动；显式 --execute 才实际执行。
/// --json 输出结构化计划（源/目标/步骤/阻碍），文本模式保持原有报告格式。
/// 执行失败抛 MigrationException 由全局捕获（exit 1，文本 stderr；--json 走包络）。
/// </summary>
static int RunMigrate(IReadOnlyList<string> positional, IReadOnlyList<string> commandFlags, bool json, string? registryRoot)
{
    var execute = commandFlags.Any(a => string.Equals(a, "--execute", StringComparison.OrdinalIgnoreCase));

    if (positional.Count < 2)
    {
        return Fail(json, "migrate", ExitCodes.ExecutionFailed, "用法：migrate <源路径> <目标路径> [备注] [--execute]（默认 dry-run：只输出执行计划，加 --execute 才实际迁移）");
    }

    var source = positional[0];
    var target = positional[1];
    var note = positional.Count > 2 ? positional[2] : "";

    var runner = new MigrationRunner();
    var plan = MigrationPlanner.Plan(source, target, note: note, registryRoot: registryRoot);

    if (!execute)
    {
        var dryRun = runner.DryRun(plan);
        if (json)
        {
            Console.WriteLine(CliJson.Serialize(CliJson.MigrateDryRun(plan)));
        }
        else
        {
            Console.WriteLine(dryRun.Report);
        }

        // 计划被阻碍（进程运行中等）→ ExecutionFailed：dry-run 报告仍完整输出
        return dryRun.Ok ? ExitCodes.Success : ExitCodes.ExecutionFailed;
    }

    var result = runner.Run(plan);
    if (json)
    {
        Console.WriteLine(CliJson.Serialize(CliJson.MigrateDone(plan)));
    }
    else
    {
        Console.WriteLine(result.Report);
    }

    return ExitCodes.Success;
}

/// <summary>
/// config 子命令（T5 收尾：从 raw args 改走全局解析器）。显式 --registry 根下读写
/// settings.json（沙箱隔离）；未指定时维持 LinkSettings 默认位置行为。
/// config            —— 显示当前登记位置
/// config set-registry <新登记文件路径> —— 迁移登记到新位置并记住
/// </summary>
static int RunConfig(IReadOnlyList<string> positional, IReadOnlyList<string> commandFlags, bool json, string? registryRoot)
{
    // 沙箱隔离：--registry 时设置文件也落在该根下（默认场景仍用 %APPDATA% 位置，行为不变）
    var settingsFile = registryRoot is null
        ? null
        : Path.Combine(registryRoot, LinkSettings.DefaultFolderName, LinkSettings.DefaultFileName);

    if (positional.Count == 0)
    {
        var settings = LinkSettings.Load(settingsFile);
        var current = LinkRegistry.ResolveRegistryFilePath(settings, registryRoot);
        if (json)
        {
            Console.WriteLine(CliJson.Serialize(CliJson.ConfigLocation(current, settings.RegistryFilePath)));
        }
        else
        {
            Console.WriteLine($"登记文件位置：{current}");
            if (settings.RegistryFilePath is not null)
            {
                Console.WriteLine($"（自定义：{settings.RegistryFilePath}；默认：{LinkRegistry.DefaultFilePath}）");
            }
            else
            {
                Console.WriteLine("（使用默认位置，可用 config set-registry <路径> 修改）");
            }
        }

        return ExitCodes.Success;
    }

    if (positional.Count == 2 && string.Equals(positional[0], "set-registry", StringComparison.OrdinalIgnoreCase))
    {
        return RunSetRegistry(positional[1], settingsFile, registryRoot, json);
    }

    return Fail(json, "config", ExitCodes.ExecutionFailed, "用法：config | config set-registry <新登记文件路径>");
}

/// <summary>set-registry：把登记表迁到新位置。settingsFile 指定时设置写沙箱（--registry 生效）；
/// null 时走产品默认位置。迁移后新位置由设置（或沙箱根）给出。</summary>
static int RunSetRegistry(string newPath, string? settingsFile, string? registryRoot, bool json)
{
    var settings = LinkSettings.Load(settingsFile);
    var current = LinkRegistry.ResolveRegistryFilePath(settings, registryRoot);
    var fullNew = Path.GetFullPath(newPath);

    if (json)
    {
        if (File.Exists(fullNew))
        {
            return Fail(json, "config", ExitCodes.ExecutionFailed, "新位置已存在登记文件，拒绝覆盖。请换一个路径或先移走该文件。");
        }

        RegistryMigrator.RelocateRegistry(fullNew, explicitRoot: registryRoot, settingsFilePath: settingsFile);
        var after = LinkSettings.Load(settingsFile);
        Console.WriteLine(CliJson.Serialize(CliJson.ConfigLocation(
            LinkRegistry.ResolveRegistryFilePath(after, registryRoot), after.RegistryFilePath)));
        return ExitCodes.Success;
    }

    Console.WriteLine($"当前登记位置：{current}");
    Console.WriteLine($"新登记位置：  {fullNew}");

    if (File.Exists(fullNew))
    {
        Console.Error.WriteLine("新位置已存在登记文件，拒绝覆盖。请换一个路径或先移走该文件。");
        return ExitCodes.ExecutionFailed;
    }

    RegistryMigrator.RelocateRegistry(fullNew, explicitRoot: registryRoot, settingsFilePath: settingsFile);
    var updated = LinkSettings.Load(settingsFile);
    Console.WriteLine("已迁移：旧数据搬到新位置，设置已更新。");
    Console.WriteLine($"登记文件位置：{LinkRegistry.ResolveRegistryFilePath(updated, registryRoot)}");
    return ExitCodes.Success;
}

/// <summary>note 子命令（T5 收尾：从 raw args 改走全局解析器——--registry/--json 生效；
/// --registry 曾被当成备注文本吞掉）。</summary>
static int RunNote(IReadOnlyList<string> positional, bool json, string? registryRoot)
{
    // note <源路径> <新备注>
    if (positional.Count < 2)
    {
        return Fail(json, "note", ExitCodes.ExecutionFailed, "用法：note <源路径> <新备注>（备注传空串 \"\" 可清除）");
    }

    var source = positional[0];
    var note = positional[1];

    var registry = LinkRegistry.Load(registryRoot);
    if (!registry.UpdateNote(source, note))
    {
        return Fail(json, "note", ExitCodes.ExecutionFailed, $"未找到该源路径的登记条目：{source}（可用 ls 查看现有条目）");
    }
    registry.Save();

    if (json)
    {
        var entry = registry.FindEntry(source) ?? throw new InvalidOperationException("UpdateNote 成功但条目不可见");
        Console.WriteLine(CliJson.Serialize(CliJson.Note(entry)));
        return ExitCodes.Success;
    }

    Console.WriteLine($"备注已更新：{source} → \"{note}\"");
    return ExitCodes.Success;
}

/// <summary>remove 子命令（T5 收尾：从 raw args 改走全局解析器——--registry 沙箱生效，
/// 曾写真实 %APPDATA% 登记表）。</summary>
static int RunRemove(IReadOnlyList<string> positional, IReadOnlyList<string> commandFlags, bool json, string? registryRoot)
{
    // remove <源路径> --keep-link            仅删登记
    // remove <源路径> --delete-link [--yes]  连同链接一起删（--yes 显式确认）
    if (positional.Count < 1)
    {
        return Fail(json, "remove", ExitCodes.ExecutionFailed, "用法：remove <源路径> --keep-link | remove <源路径> --delete-link --yes");
    }

    var source = positional[0];
    var keepLink = commandFlags.Any(a => string.Equals(a, "--keep-link", StringComparison.OrdinalIgnoreCase));
    var deleteLink = commandFlags.Any(a => string.Equals(a, "--delete-link", StringComparison.OrdinalIgnoreCase));
    var confirmed = commandFlags.Any(a => string.Equals(a, "--yes", StringComparison.OrdinalIgnoreCase));

    if (keepLink == deleteLink)
    {
        return Fail(json, "remove", ExitCodes.ExecutionFailed, "必须且只能选一种模式：--keep-link（仅删登记）或 --delete-link（连同链接一起删，需 --yes 确认）");
    }

    var registry = LinkRegistry.Load(registryRoot);

    if (keepLink)
    {
        var removed = LinkRemover.RemoveEntryOnly(registry, source);
        if (!removed)
        {
            return Fail(json, "remove", ExitCodes.ExecutionFailed, $"未找到该源路径的登记条目：{source}（可用 ls 查看现有条目）");
        }

        if (json)
        {
            Console.WriteLine(CliJson.Serialize(CliJson.Removed(source, keepLink: true)));
        }
        else
        {
            Console.WriteLine($"已删除登记条目（文件系统链接保留）：{source}");
        }

        return ExitCodes.Success;
    }

    if (!confirmed)
    {
        return Fail(json, "remove", ExitCodes.ExecutionFailed, "拒绝删除链接：--delete-link 需搭配 --yes 显式确认。链接删除后真实数据目录不受影响。");
    }

    var removedLink = LinkRemover.RemoveWithLink(registry, source, confirmDeleteLink: true);
    if (!removedLink)
    {
        return Fail(json, "remove", ExitCodes.ExecutionFailed, $"未找到该源路径的登记条目：{source}（可用 ls 查看现有条目）");
    }

    if (json)
    {
        Console.WriteLine(CliJson.Serialize(CliJson.Removed(source, keepLink: false)));
    }
    else
    {
        Console.WriteLine($"已删除链接与登记条目（真实数据目录不受影响）：{source}");
    }

    return ExitCodes.Success;
}
internal static class ExitCodes
{
    public const int Success = 0;
    public const int UserError = 1;
    public const int ExecutionFailed = 2;
    public const int RegistryError = 3;

    /// <summary>仅 ls：命令成功但登记表中发现异常链接（断链/缺失/伪装）。</summary>
    public const int AbnormalLinks = 4;

    public const int UnknownCommand = 64;
}

/// <summary>退出码 → JSON error.code 映射。与 ExitCodes / docs/cli-conventions.md 同步维护；
/// 未列出的退出码按执行失败归类（Success/AbnormalLinks 不会进入失败出口）。</summary>
internal static class CliErrorCodes
{
    public static string For(int exitCode) => exitCode switch
    {
        ExitCodes.UserError => "usage",
        ExitCodes.ExecutionFailed => "execution_failed",
        ExitCodes.RegistryError => "registry_error",
        ExitCodes.UnknownCommand => "unknown_command",
        _ => "execution_failed",
    };
}

/// <summary>解析后的全局选项与位置参数。</summary>
internal sealed record CliOptions
{
    public bool Json { get; init; }

    /// <summary>--registry 指定的登记表根目录；未指定为 null（用默认 AppData）。</summary>
    public string? RegistryRoot { get; init; }

    public IReadOnlyList<string> Positional { get; init; } = [];

    /// <summary>放行给子命令的自有布尔选项 token（原样，含 = 形式）；校验归命令自解析。</summary>
    public IReadOnlyList<string> CommandFlags { get; init; } = [];

    /// <summary>子命令自有带值选项的原始值（如 --max-depth=3 / --max-depth 3 → "3"）。</summary>
    public IReadOnlyDictionary<string, string> CommandFlagValues { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>首个解析错误（未知选项、--registry 缺参）；无错误为 null。</summary>
    public string? ParseError { get; init; }
}

/// <summary>各子命令的自有选项（不含全局 --json/--registry）。全局解析器据此放行并解析
/// 带值选项（空格/等号两种形式），具体校验由命令自解析。新命令加自有选项时同步登记在此。</summary>
internal static class CliCommandFlags
{
    /// <summary>子命令名（小写）→ 该命令可用的自有布尔选项集合。</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> BooleanByCommand =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["create"] = NewSet("--symlink"),
            ["migrate"] = NewSet("--execute"),
            ["remove"] = NewSet("--keep-link", "--delete-link", "--yes"),
            ["scan"] = NewSet("--yes"),
            ["config"] = NewSet(),
            ["note"] = NewSet(),
            ["version"] = NewSet(),
            ["ls"] = NewSet(),
            ["help"] = NewSet(),
        };

    /// <summary>子命令名 → 自有带值选项（值为字符串，语义归命令解释，如 --max-depth=3）。</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> ValuedByCommand =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["scan"] = NewSet("--max-depth"),
        };

    /// <summary>token 是否为 command 的自有布尔选项（--x= 形式也算 --x）。</summary>
    public static bool IsBooleanFlag(string command, string token)
    {
        if (!BooleanByCommand.TryGetValue(command, out var flags))
        {
            return false;
        }

        var eq = token.IndexOf('=');
        var bare = eq > 0 ? token[..eq] : token;
        return flags.Contains(bare.ToLowerInvariant());
    }

    /// <summary>token 是否为 command 的自有带值选项。</summary>
    public static bool IsValuedFlag(string command, string token)
    {
        if (!ValuedByCommand.TryGetValue(command, out var flags))
        {
            return false;
        }

        var eq = token.IndexOf('=');
        var bare = eq > 0 ? token[..eq] : token;
        return flags.Contains(bare.ToLowerInvariant());
    }

    private static IReadOnlySet<string> NewSet(params string[] flags) =>
        new HashSet<string>(flags, StringComparer.OrdinalIgnoreCase);
}

/// <summary>全局选项解析（唯一一处）：--json（结构化输出）、--registry <root>（登记表根目录
/// 重定向，agent 沙箱用）、子命令自有选项放行（校验归命令自己）。独立成类以便单元测试（经 InternalsVisibleTo）。</summary>
internal static class CliArgParser
{
    public static CliOptions Parse(string[] args)
    {
        // --json 可出现在任意位置（含坏选项之后），先整体扫描，保证失败包络也是 JSON
        var json = args.Any(a => string.Equals(a, "--json", StringComparison.OrdinalIgnoreCase));

        string? registryRoot = null;
        var positional = new List<string>();
        var commandFlags = new List<string>();
        var commandFlagValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? parseError = null;

        for (var i = 1; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(args[i], "--registry", StringComparison.OrdinalIgnoreCase))
            {
                // 下一个 token 是选项（以 -- 开头）或不存在：视为缺参，而不是吞掉它当路径
                if (i + 1 >= args.Length || args[i + 1].StartsWith("--"))
                {
                    parseError = "--registry 需要一个根目录参数：--registry <根目录>";
                    break;
                }

                registryRoot = args[++i];
                continue;
            }

            if (args[i].StartsWith("--registry=", StringComparison.OrdinalIgnoreCase))
            {
                var value = args[i]["--registry=".Length..];
                if (value.Length == 0)
                {
                    parseError = "--registry 需要一个根目录参数：--registry <根目录>";
                    break;
                }

                registryRoot = value;
                continue;
            }

            if (args[i].StartsWith("--"))
            {
                // 子命令自有选项放行；校验归命令自解析。
                // 命令名不在已知列表（未知命令）时不放行——未知命令/选项优先报错
                var command = args.Length > 0 ? args[0] : "";

                if (CliCommandFlags.IsBooleanFlag(command, args[i]))
                {
                    commandFlags.Add(args[i]);
                    continue;
                }

                if (CliCommandFlags.IsValuedFlag(command, args[i]))
                {
                    var eq = args[i].IndexOf('=');
                    if (eq > 0)
                    {
                        // --max-depth=3 形式
                        commandFlagValues[args[i][..eq].ToLowerInvariant()] = args[i][(eq + 1)..];
                        continue;
                    }

                    // --max-depth 3 形式：下一个 token 是选项或不存在视为缺参（同 --registry 约定）
                    if (i + 1 >= args.Length || args[i + 1].StartsWith("--"))
                    {
                        parseError = $"{args[i]} 需要一个值：{args[i]} <值> 或 {args[i]}=<值>";
                        break;
                    }

                    commandFlagValues[args[i].ToLowerInvariant()] = args[++i];
                    continue;
                }

                parseError = $"未知选项：{args[i]}（支持：--json, --registry <根目录>）";
                break;
            }

            positional.Add(args[i]);
        }

        return new CliOptions { Json = json, RegistryRoot = registryRoot, Positional = positional, CommandFlags = commandFlags, CommandFlagValues = commandFlagValues, ParseError = parseError };
    }
}


