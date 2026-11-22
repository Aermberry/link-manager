using LinkManager.Core.Links;

namespace LinkManager.Core.Tests;

public class WindowsDeveloperModeTests
{
    [Fact]
    public void Require_throws_when_developer_mode_is_disabled()
    {
        var detector = new DisabledDetector();

        var ex = Assert.Throws<InvalidOperationException>(
            () => WindowsDeveloperModeDetector.Options.Require(detector));

        Assert.Contains("开发者模式", ex.Message);
        Assert.Contains("设置 > 系统 > 开发者选项", ex.Message);
    }

    [Fact]
    public void Options_message_guides_user_to_windows_settings()
    {
        var message = WindowsDeveloperModeDetector.Options.Message;

        Assert.Contains("开发者模式", message);
        Assert.Contains("设置 > 系统 > 开发者选项", message);
    }

    [Fact]
    public void Settings_uri_points_at_the_developers_settings_page()
    {
        // Windows 文档约定：ms-settings:developers 打开"开发者选项"页
        Assert.Equal("ms-settings:developers", WindowsDeveloperModeDetector.Options.SettingsUri);
    }

    [Fact]
    public void Registry_semantics_match_windows_doc()
    {
        // 注册表语义（Windows 文档）：DWORD ≠ 0 即开启；本机真值经真检测器验证，
        // 键/值缺失与读取失败语义由注入探测器覆盖，这里验证真值与本机注册表一致。
        var registryValue = Microsoft.Win32.Registry.GetValue(
            @"HKEY_LOCAL_MACHINE\" + WindowsDeveloperModeDetector.RegistryKeyPath,
            WindowsDeveloperModeDetector.RegistryValueName, null);

        Assert.Equal(new WindowsDeveloperModeDetector().IsDeveloperModeEnabled,
                     registryValue is int dword && dword != 0);
    }
}
