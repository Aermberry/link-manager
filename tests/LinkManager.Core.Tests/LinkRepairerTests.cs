using LinkManager.Core.Health;
using LinkManager.Core.Links;
using LinkManager.Core.Registry;

namespace LinkManager.Core.Tests;

public class LinkRepairerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"link-manager-repair-{Guid.NewGuid():N}");

    public LinkRepairerTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Repair_rejects_missing_registry_entry()
    {
        var ex = Assert.Throws<LinkRepairException>(() => LinkRepairer.Repair(Path.Combine(_root, "missing"), _root, FakeProcessProbe.NothingRunning));
        Assert.Contains("未找到", ex.Message);
    }

    [Fact]
    public void Repair_rejects_missing_data_target()
    {
        var entry = Register("junction", targetExists: false);
        var ex = Assert.Throws<LinkRepairException>(() => LinkRepairer.Repair(entry.Source, _root, FakeProcessProbe.NothingRunning));
        Assert.Contains("数据真实位置不存在", ex.Message);
    }

    [Fact]
    public void Repair_rejects_occupied_link_location()
    {
        var entry = Register("junction", targetExists: true);
        Directory.CreateDirectory(entry.Source);
        File.WriteAllText(Path.Combine(entry.Source, "occupied.txt"), "data");

        var ex = Assert.Throws<LinkRepairException>(() => LinkRepairer.Repair(entry.Source, _root, FakeProcessProbe.NothingRunning));
        Assert.Contains("目录非空", ex.Message);
    }

    [Fact]
    public void Repair_rejects_running_processes()
    {
        var entry = Register("junction", targetExists: true);
        var probe = new FakeProcessProbe(["magpie"]);

        var ex = Assert.Throws<LinkRepairException>(() => LinkRepairer.Repair(entry.Source, _root, probe));
        Assert.Contains("相关进程仍在运行", ex.Message);
    }

    [Fact]
    public void Repair_recreates_junction_and_health_is_ok()
    {
        var entry = Register("junction", targetExists: true);
        var result = LinkRepairer.Repair(entry.Source, _root, FakeProcessProbe.NothingRunning);

        Assert.Equal(LinkStatus.Ok, result.Status);
        Assert.True(new DirectoryInfo(entry.Source).Attributes.HasFlag(FileAttributes.ReparsePoint));
        Assert.Equal(LinkStatus.Ok, LinkHealth.Check(entry.Source, entry.Type));
    }

    [Fact]
    public void Repair_recreates_symlink_and_health_is_ok()
    {
        var entry = Register("symlink", targetExists: true);
        var result = LinkRepairer.Repair(entry.Source, _root, FakeProcessProbe.NothingRunning,
            new EnabledDeveloperModeDetector());

        Assert.Equal(LinkStatus.Ok, result.Status);
        Assert.True(new DirectoryInfo(entry.Source).Attributes.HasFlag(FileAttributes.ReparsePoint));
    }

    [Fact]
    public void Repair_rejects_unknown_link_type()
    {
        var entry = Register("unknown", targetExists: true);
        var ex = Assert.Throws<LinkRepairException>(() => LinkRepairer.Repair(entry.Source, _root, FakeProcessProbe.NothingRunning));
        Assert.Contains("类型不支持", ex.Message);
    }

    private sealed class EnabledDeveloperModeDetector : IDeveloperModeDetector
    {
        public bool IsDeveloperModeEnabled => true;
    }

    private LinkEntry Register(string type, bool targetExists)
    {
        var entry = new LinkEntry
        {
            Source = Path.Combine(_root, $"link-{Guid.NewGuid():N}"),
            Target = Path.Combine(_root, $"target-{Guid.NewGuid():N}"),
            Type = type,
        };
        if (targetExists) Directory.CreateDirectory(entry.Target);
        var registry = LinkRegistry.Load(_root);
        registry.Add(entry);
        registry.Save();
        return entry;
    }
}
