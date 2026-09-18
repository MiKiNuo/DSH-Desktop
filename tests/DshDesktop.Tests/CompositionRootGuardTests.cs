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

    /// <summary>
    /// 首启崩溃守卫（2026-09-19 v0.1.2 便携版回归）：无可用 node 时（干净机器 NodePath 为空）
    /// 必须跳过 PnpmProvisioner——它对空 nodePath 硬抛参数校验，不拦则整个编排初始化被
    /// 吞成 Desktop.Bootstrap.Failed，用户连「下载 Runtime」的自救入口都没有。
    /// </summary>
    [Test]
    public async Task InitializeRuntimeAsync_SkipsPnpmProvisionWithoutNode()
    {
        string source = await CompositionRootSourceAsync();

        const string anchor = "PnpmProvisioner";
        int call = source.IndexOf(anchor, StringComparison.Ordinal);
        await Assert.That(call >= 0).IsTrue();

        await Assert.That(source.Contains("NodeProvisioner.IsNodeAvailable(_config.NodePath)", StringComparison.Ordinal))
            .IsTrue();
    }

    /// <summary>
    /// 首启自检守卫：App 引导必须在自动启动前做 Runtime 存在性自检（IsRuntimeSetupRequiredAsync），
    /// 缺 Runtime 时弹「下载并安装」提示；漏掉自检则干净机器首启永远静默卡 loading。
    /// </summary>
    [Test]
    public async Task Bootstrap_SelfChecksRuntimeBeforeAutoStart()
    {
        string? root = XamlScan.FindRepositoryRoot();
        await Assert.That(root).IsNotNull();

        string path = Path.Combine(root!, "src", "DshDesktop.App", "App.axaml.cs");
        string source = (await File.ReadAllTextAsync(path)).Replace("\r\n", "\n");

        // 只取 BootstrapRuntimeAsync 方法体：锚定方法**定义**（裸名字会先命中
        // OnFrameworkInitializationCompleted 里的调用点），且文件级 IndexOf 会被
        // 后定义的 EnsureRuntimePresentAsync 方法体干扰。
        const string anchor = "private async Task BootstrapRuntimeAsync(";
        int start = source.IndexOf(anchor, StringComparison.Ordinal);
        await Assert.That(start >= 0).IsTrue();
        int end = source.IndexOf("\n    private ", start + anchor.Length, StringComparison.Ordinal);
        string body = end < 0 ? source[start..] : source[start..end];

        int selfCheck = body.IndexOf("EnsureRuntimePresentAsync(", StringComparison.Ordinal);
        int autoStart = body.IndexOf("AutoStartRuntimeAsync()", StringComparison.Ordinal);
        await Assert.That(selfCheck >= 0).IsTrue();
        await Assert.That(autoStart >= 0).IsTrue();
        await Assert.That(selfCheck < autoStart).IsTrue();
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
