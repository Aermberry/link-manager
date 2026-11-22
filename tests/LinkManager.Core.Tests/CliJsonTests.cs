using System.Text.Json;
using LinkManager.Core.CliOutput;
using LinkManager.Core.Registry;

namespace LinkManager.Core.Tests;

/// <summary>CliJson --json 输出契约测试：包络结构、条目字段、状态值、jq 可解析性。</summary>
public class CliJsonTests : IDisposable
{
    private readonly string _root;

    public CliJsonTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"link-manager-clijson-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Ls_of_an_empty_registry_produces_a_parsable_ok_envelope()
    {
        var registry = LinkRegistry.Load(_root);

        var json = CliJson.Serialize(CliJson.Ls(registry));

        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal("ls", root.GetProperty("command").GetString());
        Assert.Equal(0, root.GetProperty("data").GetProperty("registry").GetProperty("entryCount").GetInt32());
        Assert.Equal(0, root.GetProperty("data").GetProperty("registry").GetProperty("abnormalCount").GetInt32());
        Assert.Empty(root.GetProperty("data").GetProperty("entries").EnumerateArray());
    }

    [Fact]
    public void Ls_lists_entries_with_source_target_type_note_and_status()
    {
        var registry = LinkRegistry.Load(_root);
        registry.Add(new LinkEntry
        {
            Source = @"C:\No\Such\Link",
            Target = @"F:\No\Such\Real",
            Type = "junction",
            Note = "测试条目",
        });

        var json = CliJson.Serialize(CliJson.Ls(registry));

        var doc = JsonDocument.Parse(json);
        var entry = doc.RootElement.GetProperty("data").GetProperty("entries").EnumerateArray().Single();
        Assert.Equal(@"C:\No\Such\Link", entry.GetProperty("source").GetString());
        Assert.Equal(@"F:\No\Such\Real", entry.GetProperty("target").GetString());
        Assert.Equal("junction", entry.GetProperty("type").GetString());
        Assert.Equal("测试条目", entry.GetProperty("note").GetString());
        // 路径不存在：状态应为 missing（英文小写枚举值，供 agent 判读）
        Assert.Equal("missing", entry.GetProperty("status").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("data").GetProperty("registry").GetProperty("abnormalCount").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("data").GetProperty("registry").GetProperty("entryCount").GetInt32());
    }

    [Fact]
    public void Serialize_emits_compact_single_line_unescaped_chinese()
    {
        var registry = LinkRegistry.Load(_root);
        registry.Add(new LinkEntry
        {
            Source = @"C:\No\Such\Link",
            Target = @"F:\No\Such\Real",
            Type = "junction",
            Note = "中文备注",
        });

        var json = CliJson.Serialize(CliJson.Ls(registry));

        Assert.DoesNotContain("\n", json);
        Assert.DoesNotContain("\\u", json);
        Assert.Contains("中文备注", json);
    }

    [Fact]
    public void Error_envelope_carries_code_and_message_without_data()
    {
        var json = CliJson.Serialize(CliJson.Error("create", "create_failed", "源路径不存在"));

        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("create", root.GetProperty("command").GetString());
        Assert.Equal("create_failed", root.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("源路径不存在", root.GetProperty("error").GetProperty("message").GetString());
        Assert.False(root.TryGetProperty("data", out _));
    }

    [Fact]
    public void Ok_envelope_has_no_error_property()
    {
        var json = CliJson.Serialize(CliJson.Version());

        var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.TryGetProperty("error", out _));
    }

    [Fact]
    public void Version_envelope_carries_name_and_version()
    {
        var json = CliJson.Serialize(CliJson.Version());

        var doc = JsonDocument.Parse(json);
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal("link-manager", data.GetProperty("name").GetString());
        Assert.Equal(AppInfo.Version, data.GetProperty("version").GetString());
    }

    [Fact]
    public void Create_envelope_reports_the_registered_entry_with_a_live_status()
    {
        // 用不存在的路径造登记条目：status 是即时健康检查结果，不是常量 ok
        var entry = new LinkEntry
        {
            Source = @"C:\No\Such\Link",
            Target = @"F:\No\Such\Real",
            Type = "junction",
            Note = "",
        };

        var json = CliJson.Serialize(CliJson.Create(entry));

        var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("create", doc.RootElement.GetProperty("command").GetString());
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal(@"C:\No\Such\Link", data.GetProperty("source").GetString());
        Assert.Equal(@"F:\No\Such\Real", data.GetProperty("target").GetString());
        Assert.Equal("junction", data.GetProperty("type").GetString());
        Assert.Equal("missing", data.GetProperty("status").GetString());
    }
}
