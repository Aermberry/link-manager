using LinkManager.Core.Settings;

namespace LinkManager.Core.Registry;

/// <summary>登记迁移失败（新位置不可用等），旧数据保持原状。</summary>
public sealed class RegistryMigrationException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>
/// 登记位置迁移：把现有登记数据搬到新位置，成功后删除旧文件并持久化设置。
/// 任一步失败即中止，旧数据原封不动（不留半态）。
/// </summary>
public static class RegistryMigrator
{
    /// <summary>
    /// 把登记文件迁移到 newFilePath 并把设置指向它。
    /// 当前位置解析优先级：explicitRoot > 设置中的自定义路径 > 默认位置。
    /// 顺序：检查新路径 → 复制 → 校验可读 → 删旧文件 → 写设置。
    /// 设置写入失败时回滚：恢复旧文件、删除新文件。
    /// </summary>
    /// <exception cref="RegistryMigrationException">新路径已存在或迁移中途失败（旧数据保持原状）。</exception>
    /// <exception cref="SettingsCorruptException">设置文件存在但无法解析。</exception>
    public static void RelocateRegistry(string newFilePath, string? explicitRoot = null, string? settingsFilePath = null)
    {
        var settings = LinkSettings.Load(settingsFilePath);
        var currentPath = LinkRegistry.ResolveRegistryFilePath(settings, explicitRoot);

        var fullNew = Path.GetFullPath(newFilePath);
        if (string.Equals(Path.GetFullPath(currentPath), fullNew, StringComparison.OrdinalIgnoreCase))
        {
            // 同位置：无需迁移，但设置仍要落盘（幂等）
            settings.WithRegistryFilePath(fullNew).Save();
            return;
        }

        if (File.Exists(fullNew))
        {
            throw new RegistryMigrationException($"目标位置已存在登记文件，拒绝覆盖：{fullNew}");
        }

        // 旧文件可能不存在（全新安装）：直接在新位置落一个空表并指向它
        if (!File.Exists(currentPath))
        {
            LinkRegistry.WriteEmptyFile(fullNew);
            try
            {
                settings.WithRegistryFilePath(fullNew).Save();
            }
            catch (Exception)
            {
                DeleteQuietly(fullNew);
                throw;
            }
            return;
        }

        CopyFile(currentPath, fullNew);

        // 校验新文件确实可用（能解析）再动旧文件
        try
        {
            VerifyLoaded(fullNew);
        }
        catch (Exception ex)
        {
            DeleteQuietly(fullNew);
            throw new RegistryMigrationException($"新位置写入后校验失败，已回滚：{fullNew}", ex);
        }

        DeleteQuietly(currentPath);
        try
        {
            settings.WithRegistryFilePath(fullNew).Save();
        }
        catch (Exception ex)
        {
            // 回滚：恢复旧文件、删除新文件，登记位置保持不变
            CopyFile(fullNew, currentPath);
            DeleteQuietly(fullNew);
            throw new RegistryMigrationException($"设置写入失败，迁移已回滚：{fullNew}", ex);
        }
    }

    /// <summary>
    /// 把登记文件从 oldFilePath 移到 newFilePath：复制→校验→删旧。
    /// 失败时旧文件保持原状；新位置已有文件则拒绝覆盖。
    /// </summary>
    /// <exception cref="RegistryMigrationException">新路径已存在或迁移失败。</exception>
    public static void MigrateTo(string oldFilePath, string newFilePath)
    {
        var fullNew = Path.GetFullPath(newFilePath);
        if (File.Exists(fullNew))
        {
            throw new RegistryMigrationException($"目标位置已存在登记文件，拒绝覆盖：{fullNew}");
        }

        CopyFile(oldFilePath, fullNew);
        try
        {
            VerifyLoaded(fullNew);
        }
        catch (Exception ex)
        {
            DeleteQuietly(fullNew);
            throw new RegistryMigrationException($"新位置写入后校验失败，已回滚：{fullNew}", ex);
        }

        try
        {
            File.Delete(oldFilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 删旧失败不算迁移失败：数据已在新位置，只报异常让调用方知晓
            throw new RegistryMigrationException($"数据已迁移到新位置，但旧文件删除失败：{oldFilePath}", ex);
        }
    }

    private static void CopyFile(string from, string to)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(to))!);
            File.Copy(from, to, overwrite: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new RegistryMigrationException($"无法把登记文件复制到新位置：{from} → {to}", ex);
        }
    }

    private static void VerifyLoaded(string file)
    {
        var json = File.ReadAllText(file);
        _ = System.Text.Json.JsonSerializer.Deserialize<RegistryFile>(
            json, LinkRegistry.JsonOptions)
            ?? throw new RegistryMigrationException($"新位置文件不是有效的登记表：{file}");
    }

    private static void DeleteQuietly(string file)
    {
        try { File.Delete(file); } catch (IOException) { }
    }
}
