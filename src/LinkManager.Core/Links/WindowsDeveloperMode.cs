using Microsoft.Win32;
using Win32Registry = Microsoft.Win32.Registry;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("LinkManager.Core.Tests")]

namespace LinkManager.Core.Links;

/// <summary>
/// Windows 开发者模式检测：注册表
/// HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock 的
/// AllowDevelopmentWithoutDevLicense（DWORD，非 0 即开启）。
/// 读取逻辑经由 <see cref="IDeveloperModeDetector"/> 抽象，测试可注入模拟。
/// </summary>
public interface IDeveloperModeDetector
{
    /// <summary>当前系统是否已开启开发者模式。</summary>
    bool IsDeveloperModeEnabled { get; }
}

/// <summary>开发者模式的注册表真值检测（Windows）。</summary>
public sealed class WindowsDeveloperModeDetector : IDeveloperModeDetector
{
    internal const string RegistryKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock";
    internal const string RegistryValueName = "AllowDevelopmentWithoutDevLicense";

    /// <summary>读取注册表判定开发者模式；键/值缺失或读取失败一律视为未开启。</summary>
    public bool IsDeveloperModeEnabled
    {
        get
        {
            try
            {
                using var key = Win32Registry.LocalMachine.OpenSubKey(RegistryKeyPath);
                var value = key?.GetValue(RegistryValueName);
                return value is int dword && dword != 0;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                        or System.Security.SecurityException
                                        or PlatformNotSupportedException)
            {
                return false;
            }
        }
    }

    /// <summary>未开启开发者模式时给用户/调用方的中文指引与查询接口。</summary>
    public static class Options
    {
        /// <summary>Windows 设置中开启开发者模式的路径（中文指引）。</summary>
        public const string Hint = "设置 > 系统 > 开发者选项";

        /// <summary>开发者选项页的 ms-settings URI。</summary>
        public const string SettingsUri = "ms-settings:developers";

        /// <summary>未开启开发者模式时的完整中文指引消息。</summary>
        public static string Message =>
            $"创建 symlink 需要开启 Windows 开发者模式。请前往 {Hint} 打开开关" +
            $"（或运行 start {SettingsUri}），开启后重试。";

        /// <summary>未开启时抛出带中文指引的异常；开启时直接通过。</summary>
        /// <exception cref="InvalidOperationException">开发者模式未开启。</exception>
        public static void Require(IDeveloperModeDetector detector)
        {
            if (!detector.IsDeveloperModeEnabled)
            {
                throw new InvalidOperationException(Message);
            }
        }
    }
}
