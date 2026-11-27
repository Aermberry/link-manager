using System.Reflection;
using LinkManager.Core;

namespace LinkManager.Cli.Tests;

/// <summary>
/// version 命令回归（issue：v1.1.0 发布后 version / version --json 仍报 1.0.0）：
/// csproj 曾无 Version 属性，程序集版本从未随发版 bump。钉死两条不变式：
/// ① version --json 的 version 字段 == CLI 程序集真实版本（动态读磁盘 exe，不硬编码数字，
///    未来 bump / CI -p:Version 注入后测试依旧有效）；② 文本模式首行同样带该版本。
/// </summary>
public class VersionE2eTests
{
    private static Version CliAssemblyVersion()
    {
        // 从测试运行目录读 CLI 托管程序集（exe 是 apphost 原生启动器，无托管元数据；
        // 托管程序集是旁边的 LinkManager.Cli.dll）反射取其 AssemblyVersion ——
        // 独立于测试程序集自身的版本，动态跟随 csproj 默认值与 CI 注入值，不硬编码。
        var dllPath = Path.Combine(AppContext.BaseDirectory, "LinkManager.Cli.dll");
        Assert.True(File.Exists(dllPath), $"CLI 托管程序集不存在：{dllPath}");
        return AssemblyName.GetAssemblyName(dllPath).Version!;
    }

    [Fact]
    public void Version_json_reports_the_cli_assembly_version()
    {
        var run = CliE2e.Run("version", "--json");

        Assert.Equal(0, run.ExitCode);
        var doc = run.Json();
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("version", doc.RootElement.GetProperty("command").GetString());
        var reported = doc.RootElement.GetProperty("data").GetProperty("version").GetString();
        Assert.Equal(CliAssemblyVersion().ToString(3), reported);
    }

    [Fact]
    public void Plain_text_mode_reports_the_cli_assembly_version()
    {
        var run = CliE2e.Run("version");

        Assert.Equal(0, run.ExitCode);
        var expected = $"{AppInfo.Name} {CliAssemblyVersion().ToString(3)}";
        Assert.Equal(expected, run.Stdout.Trim());
    }
}
