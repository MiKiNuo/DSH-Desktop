namespace DshDesktop.Tests;

/// <summary>
/// 组合根架构守卫（App 项目不被测试项目引用，只能文本断言；锚点 CRLF 免疫）：
/// ① config 落盘唯一入口 = ConfigPersistence，组合根不得直调 DshDesktopConfigStore.SaveAsync
///    （历史缺陷：4 处绕过 _configSaveLock 直写，与快照驱动的后台保存并发时丢写）；
/// ② 激活 Runtime 版本链路必须经 TrackStartupAsync（失败计数进入自动安全模式）且停止前
///    先回流 RuntimeStopOrchestrated（进程退出不被误判为崩溃），不得直 await _supervisor 绕过 MVI。
/// </summary>
public sealed class CompositionRootGuardTests
{
    [Test]
    public async Task CompositionRoot_HasNoDirectConfigStoreSave()
    {
        string source = await CompositionRootSourceAsync();

        await Assert.That(source.Contains("DshDesktopConfigStore.SaveAsync", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task ActivateRuntime_GoesThroughTrackedStartup()
    {
        string source = await CompositionRootSourceAsync();

        const string anchor = "HandleActivateDshRuntimeAsync(";
        int start = source.IndexOf(anchor, StringComparison.Ordinal);
        await Assert.That(start >= 0).IsTrue();

        int end = source.IndexOf("\n    private ", start + anchor.Length, StringComparison.Ordinal);
        string body = end < 0 ? source[start..] : source[start..end];

        await Assert.That(body.Contains("await _supervisor", StringComparison.Ordinal)).IsFalse();
        await Assert.That(body.Contains("TrackStartupAsync", StringComparison.Ordinal)).IsTrue();
        await Assert.That(body.Contains("RuntimeStopOrchestrated", StringComparison.Ordinal)).IsTrue();
    }

    private static async Task<string> CompositionRootSourceAsync()
    {
        string? root = XamlScan.FindRepositoryRoot();
        await Assert.That(root).IsNotNull();

        string path = Path.Combine(root!, "src", "DshDesktop.App", "Composition", "DshCompositionRoot.cs");
        await Assert.That(File.Exists(path)).IsTrue();
        return (await File.ReadAllTextAsync(path)).Replace("\r\n", "\n");
    }
}
