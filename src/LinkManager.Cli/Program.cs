using LinkManager.Core;
using LinkManager.Core.CliOutput;
using LinkManager.Core.Links;
using LinkManager.Core.Registry;

// 退出码约定（docs/cli-conventions.md）：0 成功；1 用户错误（用法/参数）；2 执行失败；
// 3 登记表错误；4 仅 ls——成功但发现异常链接；64 未知命令。

// 输出统一 UTF-8：重定向/管道下 Windows 默认跟随系统 OEM 码页（GBK），会导致
// 中文备注/错误消息变成乱码字节流。agent 消费场景固定 UTF-8。
try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch (IOException) { /* 极端宿主不支持时保持默认 */ }

if (args.Length == 0)
{
    // 交互向导是后续工单范围；当前无参数时打印帮助立即退出，绝不挂起等待输入。
    Console.WriteLine($"{AppInfo.Name} {AppInfo.Version}");
    Console.WriteLine("当前可用命令：version, ls, create, help（通用选项：--json, --registry <根目录>）");
    return 0;
}

var commandName = args[0];

// --json 可出现在任意位置（含坏选项之后），先整体扫描，保证失败包络也是 JSON
var json = args.Any(a => string.Equals(a, "--json", StringComparison.OrdinalIgnoreCase));

// 全局选项解析（唯一一处）：--json（结构化输出）、--registry <root>（登记表根目录
// 重定向，agent 沙箱用）。位置参数收集进 positional，供 create 等命令使用。
string? registryRoot = null;
var positional = new List<string>();
var parseError = (string?)null;
for (var i = 1; i < args.Length; i++)
{
    if (string.Equals(args[i], "--json", StringComparison.OrdinalIgnoreCase))
    {
        continue;
    }

    if (string.Equals(args[i], "--registry", StringComparison.OrdinalIgnoreCase))
    {
        if (i + 1 >= args.Length)
        {
            parseError = "--registry 需要一个根目录参数：--registry <根目录>";
            break;
        }

        registryRoot = args[++i];
        continue;
    }

    if (args[i].StartsWith("--registry=", StringComparison.OrdinalIgnoreCase))
    {
        registryRoot = args[i]["--registry=".Length..];
        if (registryRoot.Length == 0)
        {
            parseError = "--registry 需要一个根目录参数：--registry <根目录>";
        }

        continue;
    }

    if (args[i].StartsWith("--"))
    {
        parseError = $"未知选项：{args[i]}（支持：--json, --registry <根目录>）";
        break;
    }

    positional.Add(args[i]);
}

if (parseError is not null)
{
    return Fail(json, commandName, ExitCodes.UserError, parseError);
}

try
{
    switch (commandName)
    {
        case "version":
            return RunVersion(json);

        case "ls":
            return RunLs(json, registryRoot);

        case "create":
            return RunCreate(positional, json, registryRoot);

        case "help" or "--help" or "-h":
            return RunHelp(json);

        default:
            return Fail(json, commandName, ExitCodes.UnknownCommand,
                $"未知命令: {commandName}（可用：version, ls, create, help）");
    }
}
catch (RegistryCorruptException ex)
{
    return Fail(json, commandName, ExitCodes.RegistryError, ex.Message);
}
catch (RegistryIoException ex)
{
    return Fail(json, commandName, ExitCodes.RegistryError, ex.Message);
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
{
    // 登记目录无法创建/读写（坏盘符、权限）等原始 IO 异常，归入登记表错误
    return Fail(json, commandName, ExitCodes.RegistryError, $"登记表读写失败：{ex.Message}");
}
catch (LinkCreateException ex)
{
    return Fail(json, commandName, ExitCodes.ExecutionFailed, ex.Message);
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
    }
    else
    {
        if (registry.Entries.Count == 0)
        {
            Console.WriteLine("（登记表为空——尚未登记任何链接）");
            return ExitCodes.Success;
        }

        Console.WriteLine($"{"状态",-8}  {"源路径",-45} → {"目标路径",-45}  {"类型",-10}  备注");
        foreach (var e in result.Data!.Entries)
        {
            Console.WriteLine($"{TextStatus(e.Status),-8}  {e.Source,-45} → {e.Target,-45}  {e.Type,-10}  {e.Note}");
        }
    }

    return result.Data!.Registry.AbnormalCount > 0 ? ExitCodes.AbnormalLinks : ExitCodes.Success;
}

/// <summary>英文 JSON 状态值 → 中文表头显示（文本模式专用）。</summary>
static string TextStatus(string jsonStatus) => jsonStatus switch
{
    "ok" => "正常",
    "broken" => "断链",
    "missing" => "链接缺失",
    "disguised" => "伪装目录",
    _ => "未知",
};

static int RunCreate(List<string> positional, bool json, string? registryRoot)
{
    if (positional.Count < 2)
    {
        return Fail(json, "create", ExitCodes.UserError, "用法：create <源路径> <目标路径> [备注] [--json]");
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
            options = new[] { "--json" },
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
        Console.WriteLine(CliJson.Serialize(CliJson.Error(command, CodeFor(exitCode), message)));
    }
    else
    {
        Console.Error.WriteLine(message);
    }

    return exitCode;
}

static string CodeFor(int exitCode) => exitCode switch
{
    ExitCodes.UserError => "usage",
    ExitCodes.ExecutionFailed => "execution_failed",
    ExitCodes.RegistryError => "registry_error",
    ExitCodes.UnknownCommand => "unknown_command",
    _ => "execution_failed",
};

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
