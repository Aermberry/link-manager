using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace LinkManager.Cli.Tests;

/// <summary>CLI 端到端测试辅助：以子进程运行真实 exe。stdin 重定向（管道，不写不关）=
/// 非交互环境；登记表一律用 --registry 沙箱，绝不触真实 %APPDATA%（见 .agents gotchas）。</summary>
internal static class CliE2e
{
    public static string ExePath => Path.Combine(AppContext.BaseDirectory, "LinkManager.Cli.exe");

    public sealed record CliRun(int ExitCode, string Stdout, string Stderr)
    {
        /// <summary>stdout 最后一个非空行按 --json 单行包络解析。</summary>
        public JsonDocument Json()
        {
            var line = Stdout
                .Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .LastOrDefault()
                ?? throw new InvalidOperationException($"stdout 无 JSON 行：«{Stdout}»");
            return JsonDocument.Parse(line);
        }
    }

    /// <summary>本机 SDK 经 dnvm 管理（见 .agents/project-overview）：测试子进程需要
    /// DOTNET_ROOT 指向 .NET 11 运行时，否则 apphost 找到的是系统 dotnet（8/10，无 11）。</summary>
    public static readonly string? DotnetRoot = ResolveDotnetRoot();

    private static string? ResolveDotnetRoot()
    {
        var hostDir = Path.GetDirectoryName(Environment.ProcessPath);
        while (hostDir is not null)
        {
            var candidate = Path.Combine(hostDir, "dotnet.exe");
            if (File.Exists(candidate))
            {
                return hostDir;
            }

            hostDir = Path.GetDirectoryName(hostDir);
        }

        return null;
    }

    /// <summary>运行 CLI 子进程并等待退出（20 秒超时 = 非交互挂起保证的守卫）。
    /// stdin 是一条永远无数据的管道：正确实现绝不读它；若回退成读 stdin 会阻塞到超时失败。</summary>
    public static CliRun Run(params string[] args)
    {
        var psi = new ProcessStartInfo(ExePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (DotnetRoot is not null)
        {
            psi.EnvironmentVariables["DOTNET_ROOT"] = DotnetRoot;
        }
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();

        if (!p.WaitForExit(20_000))
        {
            p.Kill(entireProcessTree: true);
            Assert.Fail($"CLI 20 秒内未退出（非交互挂起保证被破坏）：{string.Join(' ', args)}");
        }

        p.WaitForExit(); // 无参重载：冲刷异步输出读取
        return new CliRun(p.ExitCode, stdout.Result, stderr.Result);
    }

    /// <summary>在 parent 下建真实 junction（mklink /J，无需特权）——模拟「非本工具创建」的存量链接。</summary>
    public static string NewJunction(string parent, string name, string realDir)
    {
        Directory.CreateDirectory(realDir);
        var link = Path.Combine(parent, name);
        var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{realDir}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0 && Directory.Exists(link),
            $"mklink /J 失败：{link} → {realDir}。原因：{stderr}");
        return link;
    }

    /// <summary>建一个临时沙箱目录（登记表根或扫描树），测试结束由 Dispose 清理。</summary>
    public static string NewSandbox(string prefix)
    {
        var root = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }
}
