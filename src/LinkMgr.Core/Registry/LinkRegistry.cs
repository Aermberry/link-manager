using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;

namespace LinkMgr.Core.Registry;

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
/// 登记表的加载/保存/增删查。存储位置：root 目录下 linkmgr/registry.json。
/// 首次加载时若无文件则创建空表。
/// </summary>
public sealed class LinkRegistry
{
    public const string DefaultFolderName = "linkmgr";
    public const string DefaultFileName = "registry.json";

    /// <summary>默认登记文件完整路径（不触发创建）。产品默认根为用户 AppData 目录。</summary>
    public static string DefaultFilePath => FileAt(GetDefaultRoot());

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
    public static LinkRegistry Load(string? rootDirectory = null)
    {
        var root = rootDirectory ?? GetDefaultRoot();
        var file = FileAt(root);

        if (!File.Exists(file))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var empty = new RegistryFile();
            File.WriteAllText(file, JsonSerializer.Serialize(empty, JsonOptions));
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

    public bool Remove(string source) =>
        _entries.RemoveAll(e => string.Equals(e.Source, source, StringComparison.OrdinalIgnoreCase)) > 0;

    private static string GetDefaultRoot() => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
}
