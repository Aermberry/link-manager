using LinkManager.Cli;

namespace LinkManager.Cli.Tests;

/// <summary>CliArgParser 契约测试：--json 任意位置、--registry 两种形式、坏选项/缺参报错、位置参数收集。</summary>
public class CliArgParserTests
{
    [Fact]
    public void No_options_yields_defaults()
    {
        var o = CliArgParser.Parse(new[] { "ls" });

        Assert.False(o.Json);
        Assert.Null(o.RegistryRoot);
        Assert.Null(o.ParseError);
        Assert.Empty(o.Positional);
    }

    [Fact]
    public void Json_flag_is_detected_anywhere_including_after_a_bad_option()
    {
        Assert.True(CliArgParser.Parse(new[] { "ls", "--json" }).Json);
        Assert.True(CliArgParser.Parse(new[] { "ls", "--frobnicate", "--json" }).Json);
        Assert.True(CliArgParser.Parse(new[] { "ls", "--JSON" }).Json);
        Assert.False(CliArgParser.Parse(new[] { "ls", "--jso" }).Json);
    }

    [Fact]
    public void Registry_takes_space_form_value()
    {
        var o = CliArgParser.Parse(new[] { "ls", "--registry", @"E:\sandbox" });

        Assert.Equal(@"E:\sandbox", o.RegistryRoot);
        Assert.Null(o.ParseError);
    }

    [Fact]
    public void Registry_takes_equals_form_value()
    {
        var o = CliArgParser.Parse(new[] { "ls", "--registry=E:\\sandbox" });

        Assert.Equal(@"E:\sandbox", o.RegistryRoot);
        Assert.Null(o.ParseError);
    }

    [Fact]
    public void Registry_with_missing_trailing_value_is_a_parse_error()
    {
        var o = CliArgParser.Parse(new[] { "ls", "--registry" });

        Assert.Null(o.RegistryRoot);
        Assert.NotNull(o.ParseError);
        Assert.Contains("--registry", o.ParseError);
    }

    [Fact]
    public void Registry_does_not_swallow_a_following_option()
    {
        // 回归：曾把 --json 当成路径吞掉，静默创建 "--json\link-manager\registry.json" 目录
        var o = CliArgParser.Parse(new[] { "ls", "--registry", "--json" });

        Assert.Null(o.RegistryRoot);
        Assert.NotNull(o.ParseError);
    }

    [Fact]
    public void Registry_empty_equals_form_fails_immediately()
    {
        var o = CliArgParser.Parse(new[] { "ls", "--registry=", "--registry", @"E:\valid" });

        Assert.NotNull(o.ParseError);
        Assert.Null(o.RegistryRoot);
    }

    [Fact]
    public void Unknown_option_stops_parsing_with_error()
    {
        var o = CliArgParser.Parse(new[] { "ls", "--bogus" });

        Assert.Contains("--bogus", o.ParseError);
    }

    [Fact]
    public void Positional_arguments_are_collected_in_order()
    {
        var o = CliArgParser.Parse(new[] { "create", "src", "dst", "备注", "--json" });

        Assert.Equal(new[] { "src", "dst", "备注" }, o.Positional);
        Assert.True(o.Json);
        Assert.Null(o.ParseError);
    }

    [Fact]
    public void First_error_wins_and_stops()
    {
        var o = CliArgParser.Parse(new[] { "ls", "--bogus", "--registry" });

        Assert.Contains("--bogus", o.ParseError);
    }
}

/// <summary>退出码约定钉死测试（docs/cli-conventions.md 是唯一权威；改动须双向同步）。</summary>
public class ExitCodeContractTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(64)]
    public void Exit_code_values_match_the_documented_convention(int code)
    {
        var expected = code switch
        {
            0 => ExitCodes.Success,
            1 => ExitCodes.UserError,
            2 => ExitCodes.ExecutionFailed,
            3 => ExitCodes.RegistryError,
            4 => ExitCodes.AbnormalLinks,
            64 => ExitCodes.UnknownCommand,
            _ => -1,
        };

        Assert.Equal(code, expected);
    }

    [Theory]
    [InlineData(1, "usage")]
    [InlineData(2, "execution_failed")]
    [InlineData(3, "registry_error")]
    [InlineData(64, "unknown_command")]
    public void Error_codes_map_from_exit_codes(int exitCode, string expectedCode)
    {
        Assert.Equal(expectedCode, CliErrorCodes.For(exitCode));
    }

    [Fact]
    public void Unmapped_exit_codes_fall_back_to_execution_failed()
    {
        // 兜底策略：未知退出码归入执行失败，而不是给出错误但貌似合法的码
        Assert.Equal("execution_failed", CliErrorCodes.For(42));
    }
}

/// <summary>子命令自有选项回归（T10 解析器收紧时曾拒绝 T6/T8/T9/T5 的自有标志）：
/// 全局解析器必须放行已知子命令的自有选项，交由命令自解析。</summary>
public class CommandFlagParserTests
{
    [Theory]
    [InlineData("create", "--symlink")]
    [InlineData("migrate", "--execute")]
    [InlineData("remove", "--keep-link")]
    [InlineData("remove", "--delete-link")]
    [InlineData("remove", "--yes")]
    [InlineData("scan", "--yes")]
    public void Known_command_flags_pass_the_global_gate(string command, string flag)
    {
        var o = CliArgParser.Parse(new[] { command, flag, "--json" });

        Assert.Null(o.ParseError);
        Assert.True(o.Json);
    }

    [Fact]
    public void Valued_command_flag_takes_space_form_value()
    {
        var o = CliArgParser.Parse(new[] { "scan", "--max-depth", "3", @"C:\dir" });

        Assert.Null(o.ParseError);
        Assert.Equal("3", o.CommandFlagValues["--max-depth"]);
        Assert.Equal(new[] { @"C:\dir" }, o.Positional);
    }

    [Fact]
    public void Valued_command_flag_takes_equals_form_value()
    {
        var o = CliArgParser.Parse(new[] { "scan", "--max-depth=2" });

        Assert.Null(o.ParseError);
        Assert.Equal("2", o.CommandFlagValues["--max-depth"]);
        Assert.Empty(o.Positional);
    }

    [Fact]
    public void Valued_command_flag_with_missing_value_is_a_parse_error()
    {
        var o = CliArgParser.Parse(new[] { "scan", "--max-depth" });

        Assert.NotNull(o.ParseError);
        Assert.Contains("--max-depth", o.ParseError);
    }

    [Fact]
    public void Valued_command_flag_does_not_swallow_a_following_option()
    {
        var o = CliArgParser.Parse(new[] { "scan", "--max-depth", "--yes" });

        Assert.NotNull(o.ParseError);
        Assert.False(o.CommandFlagValues.ContainsKey("--max-depth"));
    }

    [Fact]
    public void Unknown_option_on_unknown_command_still_errors()
    {
        // 命令不在已知列表时自有选项表不生效——未知命令优先报错（exit 64 由调用方判定）
        var o = CliArgParser.Parse(new[] { "frobnicate", "--yes" });

        Assert.NotNull(o.ParseError);
        Assert.Contains("--yes", o.ParseError);
    }

    [Fact]
    public void Unknown_flag_on_known_command_still_errors()
    {
        var o = CliArgParser.Parse(new[] { "scan", "--bogus" });

        Assert.NotNull(o.ParseError);
        Assert.Contains("--bogus", o.ParseError);
    }

    [Fact]
    public void Registry_still_parses_when_interleaved_with_command_flags()
    {
        var o = CliArgParser.Parse(new[] { "scan", "--registry", @"E:\sb", @"C:\dir", "--yes" });

        Assert.Equal(@"E:\sb", o.RegistryRoot);
        Assert.Equal(new[] { @"C:\dir" }, o.Positional);
        Assert.Null(o.ParseError);
    }

    [Fact]
    public void Option_error_precedes_unknown_command_as_documented()
    {
        // 文档约定：实际先解析选项，foo --badopt 退出 1（usage）而非 64
        var o = CliArgParser.Parse(new[] { "foo", "--badopt" });

        Assert.Contains("--badopt", o.ParseError);
    }
}
