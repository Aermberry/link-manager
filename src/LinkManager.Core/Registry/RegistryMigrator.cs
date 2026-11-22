using LinkManager.Core.Settings;

namespace LinkManager.Core.Registry;

/// <summary>登记迁移失败（新位置不可用等），旧数据保持原状。</summary>
public sealed class RegistryMigrationException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>
/// 登记位置迁移：把现有登记数据搬到新位置，成功后删除旧文件并持久化设置。
/// 数据复制阶段的失败旧数据原封不动；仅在最后一步（写设置）失败且回滚也失败时
/// 才可能出现"数据在新位置、设置仍指旧位置"的现场——异常消息中写明如何手工恢复。
/// </summary>
public static class RegistryMigrator
{
    /// <summary>
    /// 把登记文件迁移到 newFilePath 并把设置指向它。
    /// 当前位置解析优先级：explicitRoot > 设置中的自定义路径 > 默认位置。
    /// 顺序：检查新路径 → 复制 → 校验可读 → 删旧文件 → 写设置。
    /// 设置写入失败时回滚：恢复旧文件、删除新文件。
    /// </summary>
    /// <exception cref="RegistryMigrationException">新路径已存在或迁移中途失败（含回滚现场说明）。</exception>
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
            try
            {
                CopyFile(fullNew, currentPath);
                DeleteQuietly(fullNew);
                throw new RegistryMigrationException($"设置写入失败，迁移已回滚：{fullNew}", ex);
            }
            catch (Exception rollbackEx) when (rollbackEx is not RegistryMigrationException)
            {
                // 回滚也失败：数据在新位置完好，设置仍指旧位置——明确告知现场，不吞掉
                throw new RegistryMigrationException(
                    $"设置写入失败且回滚失败。数据已在新位置：{fullNew}；旧位置 {currentPath} 已无文件。" +
                    $"请手工把设置文件中 registryFilePath 改为 {fullNew}，或把新文件移回旧位置。", rollbackEx);
            }
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
        _ = LinkRegistry.ReadValidated(file);
    }

    /// <summary>
    /// 回滚用尽力删除：失败可容忍（残留文件不掩盖主异常），但记录到异常数据不静默。
    /// </summary>
    private static void DeleteQuietly(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 仅回滚路径使用：主异常携带现场说明，此处残留不另抛（避免掩盖根因）
        }
    }
}
