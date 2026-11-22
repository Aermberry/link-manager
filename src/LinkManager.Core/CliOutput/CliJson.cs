using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using LinkManager.Core.Health;
using LinkManager.Core.Registry;

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

/// <summary>create 的结构化结果：新登记条目 + 即时健康状态。</summary>
public sealed record CliCreateData
{
    [JsonPropertyName("source")]
    public required string Source { get; init; }

    [JsonPropertyName("target")]
    public required string Target { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("note")]
    public string Note { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public required string Status { get; init; }
}

/// <summary>错误明细：机器可读码 + 人可读中文消息。</summary>
public sealed record CliError
{
    /// <summary>错误码：usage | execution_failed | registry_error（见 docs/cli-conventions.md）。</summary>
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

    public static CliEnvelope<CliCreateData> Create(LinkEntry entry) => new()
    {
        Ok = true,
        Command = "create",
        Data = new CliCreateData
        {
            Source = entry.Source,
            Target = entry.Target,
            Type = entry.Type,
            Note = entry.Note,
            Status = LinkHealth.Check(entry.Source, entry.Type).ToJsonText(),
        },
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
