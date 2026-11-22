namespace LinkMgr.Core.Tests;

public class AppInfoTests
{
    [Fact]
    public void Version_returns_a_semver_style_string()
    {
        var version = AppInfo.Version;

        Assert.Matches(@"^\d+\.\d+\.\d+", version);
    }

    [Fact]
    public void Version_reflects_the_assembly_version()
    {
        var expected = typeof(AppInfo).Assembly.GetName().Version!.ToString(3);

        Assert.Equal(expected, AppInfo.Version);
    }
}
