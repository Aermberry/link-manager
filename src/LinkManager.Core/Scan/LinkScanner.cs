using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using LinkManager.Core.Health;
using LinkManager.Core.Registry;

namespace LinkManager.Core.Scan;

/// <summary>扫描发现的待登记候选：链接路径、解析出的真实目标、类型与登记状态。</summary>
public sealed record ScannedLink
{
    /// <summary>链接所在路径（对应登记表 Source）。</summary>
    [JsonPropertyName("source")]
    public required string Source { get; init; }

    /// <summary>解析出的真实目标路径（对应登记表 Target）。</summary>
    [JsonPropertyName("target")]
    public required string Target { get; init; }

    /// <summary>链接类型：junction 或 symlink。</summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }
}

/// <summary>一次扫描的汇总：候选列表、无权限跳过的目录、统计。纯 Core 结果对象；
/// --json 形状由 CliJson.Scan 映射产出（本对象不直接序列化）。</summary>
public sealed record ScanResult
{
    /// <summary>待问询/导入的候选链接（已登记的不在其中）。</summary>
    public List<ScannedLink> Links { get; init; } = [];

    /// <summary>因无权限/IO 错误而无法枚举、被跳过的目录。</summary>
    public List<string> Skipped { get; init; } = [];

    /// <summary>已登记（不再重复出现为候选）的链接数。</summary>
    public int RegisteredCount { get; internal set; }

    /// <summary>实际枚举过的目录数（含根；被跳过的目录不计入）。</summary>
    public int ScannedDirectoryCount { get; internal set; }
}

/// <summary>
/// 存量链接扫描器（issue #15）：遍历目录树发现 junction/symlink（含非本工具创建的），
/// 逐条产出候选。不深入 reparse point（防 junction 环）；已登记的链接不进入候选，
/// 计入 RegisteredCount。纯只读——发现与登记解耦，交互问询与非交互导入都在调用方（CLI/GUI）。
/// </summary>
public static class LinkScanner
{
    /// <summary>扫描 root 目录树；registry 为 null 时全部候选按未登记处理。</summary>
    /// <param name="maxDepth">扫描深度：0 只扫根一层，1 = 根 + 一层子目录，以此类推；负值=不限。</summary>
    public static ScanResult Scan(string root, LinkRegistry? registry = null, int maxDepth = -1)
    {
        var result = new ScanResult();
        Walk(new DirectoryInfo(root), registry, maxDepth, 0, result);
        return result;
    }

    private static void Walk(DirectoryInfo dir, LinkRegistry? registry, int maxDepth, int depth, ScanResult result)
    {
        result.ScannedDirectoryCount++;
        IEnumerable<FileSystemInfo> children;
        try
        {
            children = dir.EnumerateFileSystemInfos();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            result.Skipped.Add(dir.FullName);
            return;
        }

        foreach (var child in children)
        {
            if ((child.Attributes & FileAttributes.ReparsePoint) == 0)
            {
                if (child is DirectoryInfo plain && (maxDepth < 0 || depth < maxDepth))
                {
                    Walk(plain, registry, maxDepth, depth + 1, result);
                }
                continue;
            }

            var target = child.ResolveLinkTarget(returnFinalTarget: true)?.FullName;
            var type = GetLinkType(child.FullName);

            // 已登记的不重复出现在候选里（issue #15：已登记的链接不重复登记）
            if (registry is not null && registry.ContainsEntry(child.FullName))
            {
                result.RegisteredCount++;
                continue;
            }

            result.Links.Add(new ScannedLink
            {
                Source = child.FullName,
                Target = target ?? string.Empty,
                Type = type,
            });
        }
    }

    // Windows reparse tag 常量（winnt.h）：symlink 与 junction（mount point）区分开，
    // 目录 symlink 曾被误归为 junction——类型会进入登记表并影响健康判定/删除行为。
    internal const uint IoReparseTagSymlink = 0xA000000Cu;
    internal const uint IoReparseTagMountPoint = 0xA0000003u;

    /// <summary>按 reparse tag 判定链接类型：symlink → "symlink"，mount point → "junction"，
    /// 其他/未知 tag（非 surrogate 或第三方）按目录形态兜底归类（junction 与 mount point 同 tag，
    /// 目录形态链接除 symlink 外基本都是 junction）。读 tag 失败同样按形态兜底。</summary>
    internal static string GetLinkType(string linkPath)
    {
        var got = TryGetReparseTag(linkPath, out var isDirectory, out var tag);
        return (got, tag) switch
        {
            (true, IoReparseTagSymlink) => "symlink",
            (true, IoReparseTagMountPoint) => "junction",
            _ => isDirectory ? "junction" : "symlink",
        };
    }

    /// <summary>读取路径的 reparse tag（Windows DeviceIoControl FSCTL_GET_REPARSE_POINT）。
    /// 成功返回 true 并给出 tag 与是否目录形态；路径不存在/非链接/读取失败返回 false。</summary>
    private static unsafe bool TryGetReparseTag(string linkPath, out bool isDirectory, out uint tag)
    {
        isDirectory = Directory.Exists(linkPath);
        tag = 0;
        var attrs = new FileInfo(linkPath).Attributes;
        if ((attrs & FileAttributes.ReparsePoint) == 0)
        {
            return false;
        }

        var hFile = NativeMethods.CreateFileW(linkPath,
            0, // 不需要访问权：只需查询元数据；目录必须用 backup 语义打开
            NativeMethods.FileShareReadWriteDelete,
            IntPtr.Zero, FileMode.Open,
            NativeMethods.FileFlagOpenReparsePoint | NativeMethods.FileFlagBackupSemantics,
            IntPtr.Zero);
        if (hFile == NativeMethods.InvalidHandleValue)
        {
            return false;
        }

        try
        {
            // FSCTL_GET_REPARSE_POINT 要求输出缓冲足以容纳完整 REPARSE_DATA_BUFFER
            //（含路径数据，最大 16KB）；过小的缓冲会失败——曾用 16 字节缓冲踩过这个坑。
            var buffer = new byte[16 * 1024];
            fixed (byte* p = buffer)
            {
                if (!NativeMethods.DeviceIoControl(hFile, NativeMethods.FsctlGetReparsePoint,
                        null, 0, p, (uint)buffer.Length, out var returned, IntPtr.Zero)
                    || returned < 8)
                {
                    return false;
                }

                // REPARSE_DATA_BUFFER: ULONG Tag; ...
                tag = BitConverter.ToUInt32(buffer, 0);
                // 目录 symlink 是目录形态的 reparse point；tag 判类型，形态仅作兜底
                isDirectory = (attrs & FileAttributes.Directory) != 0;
                return true;
            }
        }
        finally
        {
            _ = NativeMethods.CloseHandle(hFile);
        }
    }

    internal static class NativeMethods
    {
        public const uint FsctlGetReparsePoint = 0x000900A8u;
        public const uint FileFlagOpenReparsePoint = 0x00200000u;
        public const uint FileFlagBackupSemantics = 0x02000000u;
        public const uint FileShareReadWriteDelete = 0x7u; // FileShare.ReadWrite | Delete
        public static readonly IntPtr InvalidHandleValue = new(-1);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateFileW(
            string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
            FileMode creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern unsafe bool DeviceIoControl(
            IntPtr hDevice, uint ioControlCode, void* inBuffer, uint inBufferSize,
            byte* outBuffer, uint outBufferSize, out uint bytesReturned, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr handle);
    }
}
