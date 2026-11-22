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
