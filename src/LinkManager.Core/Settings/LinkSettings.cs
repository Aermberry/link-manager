using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LinkManager.Core.Settings;

/// <summary>设置文件损坏（存在但无法解析）。</summary>
public sealed class SettingsCorruptException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>设置文件读写失败（IO/权限）。</summary>
public sealed class SettingsIoException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>
/// 产品设置。文件固定在默认根（用户 AppData）下 link-manager/settings.json——
/// 它指向登记文件的位置，自身不能随登记迁移，否则找不到自己。
/// 当前只有一项：自定义登记文件路径（null = 用默认位置）。
/// </summary>
public sealed class LinkSettings
{
    public const string DefaultFolderName = LinkManager.Core.Registry.LinkRegistry.DefaultFolderName;
    public const string DefaultFileName = "settings.json";

    /// <summary>默认设置文件完整路径（不触发创建）。</summary>
    public static string DefaultFilePath =>
        Path.Combine(GetDefaultRoot(), DefaultFolderName, DefaultFileName);

    /// <summary>自定义登记文件完整路径；null = 使用默认登记位置。</summary>
    [JsonPropertyName("registryFilePath")]
    public string? RegistryFilePath { get; private init; }

    /// <summary>本实例对应的设置文件路径；Load 时确定，Save 写回该处。</summary>
    [JsonIgnore]
    public string FilePath { get; private init; } = DefaultFilePath;

    private static string GetDefaultRoot() =>
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>读取设置；文件不存在返回默认值（不创建文件）。</summary>
    /// <exception cref="SettingsCorruptException">设置文件存在但无法解析。</exception>
    /// <exception cref="SettingsIoException">读取失败。</exception>
    public static LinkSettings Load(string? settingsFilePath = null)
    {
        var file = settingsFilePath ?? DefaultFilePath;

        if (!File.Exists(file))
        {
            return new LinkSettings { FilePath = file };
        }

        string json;
        try
        {
            json = File.ReadAllText(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new SettingsIoException($"无法读取设置文件：{file}", ex);
        }

        SettingsDto parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<SettingsDto>(json, JsonOptions)
                     ?? throw new SettingsCorruptException($"设置文件为空或不是有效的设置：{file}");
        }
        catch (JsonException ex)
        {
            throw new SettingsCorruptException($"设置文件损坏，无法解析：{file}。请检查该文件或删除后重跑（将重建默认设置）。", ex);
        }

        return new LinkSettings { RegistryFilePath = parsed.RegistryFilePath, FilePath = file };
    }

    /// <summary>持久化设置（覆盖写入 Load 时确定的文件路径）。</summary>
    /// <exception cref="SettingsIoException">写入失败。</exception>
    public void Save()
    {
        var file = FilePath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
            File.WriteAllText(file, JsonSerializer.Serialize(
                new SettingsDto { RegistryFilePath = RegistryFilePath }, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new SettingsIoException($"无法写入设置文件：{file}", ex);
        }
    }

    /// <summary>返回带新自定义登记路径的设置副本；null 表示清除自定义、回到默认位置。</summary>
    public LinkSettings WithRegistryFilePath(string? registryFilePath) =>
        new() { RegistryFilePath = Normalize(registryFilePath), FilePath = FilePath };

    /// <summary>路径规范化：空白 → null；非空 → 绝对路径字符串。</summary>
    private static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return Path.GetFullPath(path);
    }

    private sealed class SettingsDto
    {
        [JsonPropertyName("registryFilePath")]
        public string? RegistryFilePath { get; init; }
    }
}
