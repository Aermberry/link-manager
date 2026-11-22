using LinkManager.Core;
using LinkManager.Core.CliOutput;
using LinkManager.Core.Health;
using LinkManager.Core.Links;
using LinkManager.Core.Migration;
using LinkManager.Core.Registry;

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
    Console.WriteLine("当前可用命令：version, ls, create, migrate, help（通用选项：--json, --registry <根目录>）");
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
            return RunCreate(options.Positional, options.Json, options.RegistryRoot);

        case "help" or "--help" or "-h":
            return RunHelp(options.Json);

        case "migrate":
            return RunMigrate(args);

        default:
            return Fail(options.Json, commandName, ExitCodes.UnknownCommand,
                $"未知命令: {commandName}（可用：version, ls, create, migrate, help）");
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
catch (LinkCreateException ex)
{
    return Fail(options.Json, commandName, ExitCodes.ExecutionFailed, ex.Message);
}
catch (MigrationException ex)
{
    Console.Error.WriteLine($"迁移失败：{ex.Message}");
    return 1;
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

static int RunCreate(IReadOnlyList<string> positional, bool json, string? registryRoot)
{
    if (positional.Count < 2)
    {
        return Fail(json, "create", ExitCodes.UserError, "用法：create <源路径> <目标路径> [备注] [--json] [--registry <根目录>]");
    }

    var source = positional[0];
    var target = positional[1];
    var note = positional.Count > 2 ? positional[2] : "";

    var entry = LinkCreator.CreateJunction(source, target, note, registryRoot);

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

/// <summary>退出码约定。唯一权威定义；文档在 docs/cli-conventions.md，改动须同步。</summary>
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

    /// <summary>首个解析错误（未知选项、--registry 缺参）；无错误为 null。</summary>
    public string? ParseError { get; init; }
}

/// <summary>全局选项解析（唯一一处）：--json（结构化输出）、--registry <root>（登记表根目录
/// 重定向，agent 沙箱用）。独立成类以便单元测试（经 InternalsVisibleTo）。</summary>
internal static class CliArgParser
{
    public static CliOptions Parse(string[] args)
    {
        // --json 可出现在任意位置（含坏选项之后），先整体扫描，保证失败包络也是 JSON
        var json = args.Any(a => string.Equals(a, "--json", StringComparison.OrdinalIgnoreCase));

        string? registryRoot = null;
        var positional = new List<string>();
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
                parseError = $"未知选项：{args[i]}（支持：--json, --registry <根目录>）";
                break;
            }

            positional.Add(args[i]);
        }

        return new CliOptions { Json = json, RegistryRoot = registryRoot, Positional = positional, ParseError = parseError };
    }
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
