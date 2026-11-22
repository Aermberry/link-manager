using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using LinkManager.Core.Settings;

namespace LinkManager.Core.Registry;

/// <summary>一条已登记的链接：源路径（链接所在处）与目标路径（真实数据所在处）。</summary>
public sealed record LinkEntry
{
    [JsonPropertyName("source")]
    public required string Source { get; init; }

    [JsonPropertyName("target")]
    public required string Target { get; init; }

    /// <summary>链接类型：junction 或 symlink。</summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("note")]
    public string Note { get; init; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
}

/// <summary>登记表根对象。</summary>
public sealed record RegistryFile
{
    [JsonPropertyName("version")]
    public int Version { get; init; } = 1;

    [JsonPropertyName("entries")]
    public List<LinkEntry> Entries { get; init; } = [];
}

/// <summary>
/// 登记表的加载/保存/增删查。存储位置优先级：显式 rootDirectory > 设置中的自定义登记路径 > 默认位置。
/// 首次加载时若无文件则创建空表。
/// </summary>
public sealed class LinkRegistry
{
    public const string DefaultFolderName = "link-manager";
    public const string DefaultFileName = "registry.json";

    /// <summary>默认登记文件完整路径（不触发创建）。产品默认根为用户 AppData 目录。</summary>
    public static string DefaultFilePath => FileAt(GetDefaultRoot());

    /// <summary>
    /// 按设置解析登记文件的实际位置：设置含自定义路径用之，否则用默认位置。
    /// explicitRoot 非空时优先（显式指定的 root 覆盖设置）。
    /// </summary>
    public static string ResolveRegistryFilePath(LinkSettings? settings, string? explicitRoot = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitRoot))
        {
            return FileAt(explicitRoot);
        }

        var custom = settings?.RegistryFilePath;
        return string.IsNullOrWhiteSpace(custom) ? FileAt(GetDefaultRoot()) : custom;
    }

    private static string FileAt(string root) =>
        Path.Combine(root, DefaultFolderName, DefaultFileName);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string RootDirectory { get; }
    public string FilePath { get; }
    public IReadOnlyList<LinkEntry> Entries => _entries;

    private readonly List<LinkEntry> _entries;

    private LinkRegistry(string rootDirectory, string filePath, List<LinkEntry> entries)
    {
        RootDirectory = rootDirectory;
        FilePath = filePath;
        _entries = entries;
    }

    /// <exception cref="RegistryCorruptException">登记文件存在但无法解析。</exception>
    /// <exception cref="SettingsCorruptException">设置文件存在但无法解析（仅当未显式指定 root 时读取设置）。</exception>
    /// <exception cref="SettingsIoException">设置文件读取失败（仅当未显式指定 root 时读取设置）。</exception>
    public static LinkRegistry Load(string? rootDirectory = null, string? settingsFilePath = null)
    {
        string file;
        string root;
        if (rootDirectory is not null)
        {
            // 显式 root：显式指定的场景（测试/嵌入方），优先于设置
            file = FileAt(rootDirectory);
            root = rootDirectory;
        }
        else
        {
            var settings = LinkSettings.Load(settingsFilePath);
            file = ResolveRegistryFilePath(settings);
            root = Path.GetDirectoryName(Path.GetFullPath(file))!;
        }

        if (!File.Exists(file))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            WriteEmptyFile(file);
            return new LinkRegistry(root, file, []);
        }

        string json;
        try
        {
            json = File.ReadAllText(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new RegistryIoException($"无法读取登记文件：{file}", ex);
        }

        RegistryFile parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<RegistryFile>(json, JsonOptions)
                     ?? throw new RegistryCorruptException($"登记文件为空或不是有效的登记表：{file}");
        }
        catch (JsonException ex)
        {
            throw new RegistryCorruptException($"登记文件损坏，无法解析：{file}。请检查该文件或删除后重跑（将重建空表）。", ex);
        }

        return new LinkRegistry(root, file, parsed.Entries);
    }

    /// <exception cref="RegistryIoException">写入失败。</exception>
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var file = new RegistryFile { Entries = [.. _entries] };
        File.WriteAllText(FilePath, JsonSerializer.Serialize(file, JsonOptions));
    }

    public void Add(LinkEntry entry) => _entries.Add(entry);

    public bool Remove(string source)
    {
        var removed = _entries.RemoveAll(e => MatchesSource(e, source));
        return removed > 0;
    }

    /// <summary>是否存在指定源路径的条目（大小写与分隔符方向不敏感）。</summary>
    public bool ContainsEntry(string source) =>
        _entries.Any(e => MatchesSource(e, source));

    /// <summary>按源路径查找条目（大小写与分隔符方向不敏感，同 ContainsEntry）；无则 null。</summary>
    public LinkEntry? FindEntry(string source) =>
        _entries.FirstOrDefault(e => MatchesSource(e, source));

    /// <summary>更新指定源路径条目的备注；返回是否找到并更新。备注可为空串（清除）。</summary>
    public bool UpdateNote(string source, string note)
    {
        var index = _entries.FindIndex(e => MatchesSource(e, source));
        if (index < 0)
        {
            return false;
        }

        _entries[index] = _entries[index] with { Note = note };
        return true;
    }

    /// <summary>源路径匹配：不区分大小写与分隔符方向（登记存反斜杠，用户可传正斜杠）。</summary>
    private static bool MatchesSource(LinkEntry entry, string source)
    {
        if (string.Equals(entry.Source, source, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 归一化分隔符方向后再比（Path.GetFullPath 不改分隔符，需手动统一）
        var normalized = source.Replace('/', '\\');
        return string.Equals(entry.Source, normalized, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>在指定路径写入一个空登记表文件（覆盖已有内容）。供 Load 与迁移器使用。</summary>
    internal static void WriteEmptyFile(string filePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
        var empty = new RegistryFile();
        File.WriteAllText(filePath, JsonSerializer.Serialize(empty, JsonOptions));
    }

    /// <summary>读取指定路径的登记文件并解析校验；无效内容抛异常。供迁移器校验新位置文件。</summary>
    internal static RegistryFile ReadValidated(string filePath)
    {
        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<RegistryFile>(json, JsonOptions)
               ?? throw new RegistryCorruptException($"登记文件为空或不是有效的登记表：{filePath}");
    }

    private static string GetDefaultRoot() => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
}
