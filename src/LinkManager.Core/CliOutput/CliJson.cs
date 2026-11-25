using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using LinkManager.Core.Health;
using LinkManager.Core.Migration;
using LinkManager.Core.Registry;
using LinkManager.Core.Scan;

namespace LinkManager.Core.CliOutput;

/// <summary>一条链接条目的结构化输出（--json 模式）：字段与登记表一致，状态为即时健康检查结果。</summary>
public sealed record CliLinkEntry
{
    [JsonPropertyName("source")]
    public required string Source { get; init; }

    [JsonPropertyName("target")]
    public required string Target { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("note")]
    public string Note { get; init; } = string.Empty;

    /// <summary>健康状态，英文小写：ok | broken | missing | disguised（见 docs/cli-conventions.md）。</summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }
}

/// <summary>ls 的结构化结果：条目列表 + 登记表统计。</summary>
public sealed record CliLsData
{
    [JsonPropertyName("entries")]
    public IReadOnlyList<CliLinkEntry> Entries { get; init; } = [];

    [JsonPropertyName("registry")]
    public required CliRegistryInfo Registry { get; init; }
}

/// <summary>登记表统计信息。</summary>
public sealed record CliRegistryInfo
{
    [JsonPropertyName("path")]
    public required string Path { get; init; }

    [JsonPropertyName("entryCount")]
    public int EntryCount { get; init; }

    [JsonPropertyName("abnormalCount")]
    public int AbnormalCount { get; init; }
}

/// <summary>version 的结构化结果。</summary>
public sealed record CliVersionData
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("version")]
    public required string Version { get; init; }
}

/// <summary>scan 的结构化结果：候选链接、跳过目录与汇总统计。</summary>
public sealed record CliScanData
{
    [JsonPropertyName("links")]
    public IReadOnlyList<ScannedLink> Links { get; init; } = [];

    [JsonPropertyName("skipped")]
    public IReadOnlyList<string> Skipped { get; init; } = [];

    [JsonPropertyName("summary")]
    public required CliScanSummary Summary { get; init; }
}

/// <summary>scan 的汇总统计。</summary>
public sealed record CliScanSummary
{
    [JsonPropertyName("candidateCount")]
    public int CandidateCount { get; init; }

    [JsonPropertyName("skippedCount")]
    public int SkippedCount { get; init; }

    [JsonPropertyName("registeredCount")]
    public int RegisteredCount { get; init; }

    [JsonPropertyName("scannedDirectoryCount")]
    public int ScannedDirectoryCount { get; init; }

    /// <summary>本次实际导入登记的条数（纯扫描/降级报告时为 0）。</summary>
    [JsonPropertyName("importedCount")]
    public int ImportedCount { get; init; }
}

/// <summary>migrate 的结构化结果：源/目标/计划步骤/阻碍进程/executed 标志（dry-run 与执行共用形状）。</summary>
public sealed record CliMigrateData
{
    [JsonPropertyName("source")]
    public required string Source { get; init; }

    [JsonPropertyName("target")]
    public required string Target { get; init; }

    [JsonPropertyName("steps")]
    public IReadOnlyList<string> Steps { get; init; } = [];

    [JsonPropertyName("runningProcesses")]
    public IReadOnlyList<string> RunningProcesses { get; init; } = [];

    /// <summary>false = dry-run（未改动文件系统）；true = 已执行迁移。</summary>
    [JsonPropertyName("executed")]
    public bool Executed { get; init; }

    public static CliMigrateData Of(MigrationPlan plan, bool execute) => new()
    {
        Source = plan.Source,
        Target = plan.Target,
        Steps = plan.Steps,
        RunningProcesses = [.. plan.RunningProcesses.Select(p => p.Name)],
        Executed = execute,
    };
}

/// <summary>config 的结构化结果：当前生效的登记文件位置；customRegistryFilePath 为用户自定义路径（无则 null）。</summary>
public sealed record CliConfigData
{
    [JsonPropertyName("registryFilePath")]
    public required string RegistryFilePath { get; init; }

    [JsonPropertyName("customRegistryFilePath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CustomRegistryFilePath { get; init; }
}

/// <summary>remove 的结构化结果：被移除的登记源路径；keepLink=true 表示仅删登记、链接保留。</summary>
public sealed record CliRemoveData
{
    [JsonPropertyName("source")]
    public required string Source { get; init; }

    [JsonPropertyName("keepLink")]
    public bool KeepLink { get; init; }
}

/// <summary>错误明细：机器可读码 + 人可读中文消息。</summary>
public sealed record CliError
{
    /// <summary>错误码：usage | execution_failed | registry_error | unknown_command（见 docs/cli-conventions.md）。</summary>
    [JsonPropertyName("code")]
    public required string Code { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }
}

/// <summary>
/// CLI --json 输出包络：ok + command + data（成功）或 error（失败）。成功包络无 error 字段，
/// 失败包络无 data 字段，方便 jq 判读（.ok）。
/// </summary>
public sealed record CliEnvelope<T>
{
    [JsonPropertyName("ok")]
    public required bool Ok { get; init; }

    [JsonPropertyName("command")]
    public required string Command { get; init; }

    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public T? Data { get; init; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CliError? Error { get; init; }
}

/// <summary>CLI --json 输出构造与序列化。Core 层辅助，CLI 与测试共用；只读，不碰业务状态。</summary>
public static class CliJson
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // 单行紧凑输出：agent 与 jq 逐行读取最稳
        WriteIndented = false,
        // 中文备注原样输出，不转义成 \uXXXX
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>把包络序列化成单行 JSON。输出到 stdout 供 jq 解析。</summary>
    public static string Serialize<T>(CliEnvelope<T> envelope) =>
        JsonSerializer.Serialize(envelope, JsonOptions);

    public static CliEnvelope<CliVersionData> Version() => new()
    {
        Ok = true,
        Command = "version",
        Data = new CliVersionData { Name = AppInfo.Name, Version = AppInfo.Version },
    };

    public static CliEnvelope<CliLsData> Ls(LinkRegistry registry)
    {
        var entries = new List<CliLinkEntry>();
        var abnormal = 0;
        foreach (var e in registry.Entries)
        {
            var status = LinkHealth.Check(e.Source, e.Type);
            if (status != LinkStatus.Ok)
            {
                abnormal++;
            }

            entries.Add(new CliLinkEntry
            {
                Source = e.Source,
                Target = e.Target,
                Type = e.Type,
                Note = e.Note,
                Status = status.ToJsonText(),
            });
        }

        return new CliEnvelope<CliLsData>
        {
            Ok = true,
            Command = "ls",
            Data = new CliLsData
            {
                Entries = entries,
                Registry = new CliRegistryInfo
                {
                    Path = registry.FilePath,
                    EntryCount = registry.Entries.Count,
                    AbnormalCount = abnormal,
                },
            },
        };
    }

    /// <summary>create 的结构化结果：新登记条目 + 即时健康状态（command 标 create）。</summary>
    public static CliEnvelope<CliLinkEntry> Create(LinkEntry entry) => EntryEnvelope("create", entry);

    /// <summary>note 的结构化结果：更新后的条目，形状同 create（command 标 note——
    /// 曾误用 create 包络，agent 按 .command 判读会拿到错误命令名）。</summary>
    public static CliEnvelope<CliLinkEntry> Note(LinkEntry entry) => EntryEnvelope("note", entry);

    /// <summary>条目型包络（create/note 共用形状，仅 command 不同）。</summary>
    private static CliEnvelope<CliLinkEntry> EntryEnvelope(string command, LinkEntry entry) => new()
    {
        Ok = true,
        Command = command,
        Data = new CliLinkEntry
        {
            Source = entry.Source,
            Target = entry.Target,
            Type = entry.Type,
            Note = entry.Note,
            Status = LinkHealth.Check(entry.Source, entry.Type).ToJsonText(),
        },
    };

    /// <summary>scan 的结构化结果：候选列表 + 跳过目录 + 汇总统计（issue #15）。</summary>
    public static CliEnvelope<CliScanData> Scan(ScanResult result, string command = "scan") => new()
    {
        Ok = true,
        Command = command,
        Data = new CliScanData
        {
            Links = [.. result.Links],
            Skipped = [.. result.Skipped],
            Summary = new CliScanSummary
            {
                CandidateCount = result.Links.Count,
                SkippedCount = result.Skipped.Count,
                RegisteredCount = result.RegisteredCount,
                ScannedDirectoryCount = result.ScannedDirectoryCount,
            },
        },
    };

    /// <summary>migrate dry-run 的结构化结果：计划要点与阻碍（报告全文在文本模式）。
    /// ok 恒为 true——dry-run 本身成功完成；阻碍与拒绝语义由退出码（2）与 runningProcesses 表达，
    /// 同 ls 退出码 4 的「成功但有异常」先例（失败包络无 data 的契约不被破坏）。</summary>
    public static CliEnvelope<CliMigrateData> MigrateDryRun(MigrationPlan plan) => new()
    {
        Ok = true,
        Command = "migrate",
        Data = CliMigrateData.Of(plan, execute: false),
    };

    /// <summary>migrate --execute 完成后的结构化结果（失败走 Error 包络，不进这里）。</summary>
    public static CliEnvelope<CliMigrateData> MigrateDone(MigrationPlan plan) => new()
    {
        Ok = true,
        Command = "migrate",
        Data = CliMigrateData.Of(plan, execute: true),
    };

    /// <summary>config 的结构化结果：登记文件当前解析位置与自定义路径（无则 null）。</summary>
    public static CliEnvelope<CliConfigData> ConfigLocation(string resolvedPath, string? customPath) => new()
    {
        Ok = true,
        Command = "config",
        Data = new CliConfigData { RegistryFilePath = resolvedPath, CustomRegistryFilePath = customPath },
    };

    /// <summary>remove 的结构化结果：被移除的源路径与是否保留链接。</summary>
    public static CliEnvelope<CliRemoveData> Removed(string source, bool keepLink) => new()
    {
        Ok = true,
        Command = "remove",
        Data = new CliRemoveData { Source = source, KeepLink = keepLink },
    };

    public static CliEnvelope<object?> Error(string command, string code, string message) => new()
    {
        Ok = false,
        Command = command,
        Data = null,
        Error = new CliError { Code = code, Message = message },
    };
}

/// <summary>LinkStatus 的 JSON 文本表示（英文小写，与 ToChinese 并列，供 agent 判读）。</summary>
public static class LinkStatusJsonText
{
    public static string ToJsonText(this LinkStatus status) => status switch
    {
        LinkStatus.Ok => "ok",
        LinkStatus.Broken => "broken",
        LinkStatus.Missing => "missing",
        LinkStatus.Disguised => "disguised",
        _ => "unknown",
    };
}
