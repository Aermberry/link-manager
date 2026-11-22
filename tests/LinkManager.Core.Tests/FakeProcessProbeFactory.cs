using LinkManager.Core.Migration;

namespace LinkManager.Core.Tests;

/// <summary>测试工厂：把进程名数组包装成可注入的探测器。</summary>
internal static class FakeProcessProbeFactory
{
    public static Func<string[], IProcessProbe> Of(string[] running) =>
        _ => new FakeProcessProbe(running);
}
