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
    public async Task RecoveryController_UsesProductionLogger()
    {
        string source = await CompositionRootSourceAsync();
        int start = source.IndexOf("_recoveryController = new RuntimeRecoveryController(", StringComparison.Ordinal);
        await Assert.That(start >= 0).IsTrue();
        int end = source.IndexOf(");", start, StringComparison.Ordinal);
        await Assert.That(source[start..end].Contains("logger: Log.Logger", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task CompositionRoot_HasNoDirectConfigStoreSave()
    {
        string source = await CompositionRootSourceAsync();

        await Assert.That(source.Contains("DshDesktopConfigStore.SaveAsync", StringComparison.Ordinal)).IsFalse();
        string root = XamlScan.FindRepositoryRoot()!;
        foreach (string file in Directory.EnumerateFiles(Path.Combine(root, "src", "DshDesktop.App", "Composition"), "*.cs"))
        {
            string module = await File.ReadAllTextAsync(file);
            await Assert.That(module.Contains("DshDesktopConfigStore.SaveAsync", StringComparison.Ordinal)).IsFalse();
        }
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
        await Assert.That(body.Contains("RuntimeActivation.ActivateAsync", StringComparison.Ordinal)).IsTrue();
        await Assert.That(body.Contains("cancellationToken, _lifetimeSource.Token", StringComparison.Ordinal)).IsTrue();
        await Assert.That(body.Contains("DispatchRuntimeFailed, activationSource.Token", StringComparison.Ordinal)).IsTrue();
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

    [Test]
    public async Task CheckUpdates_IncludesResolvableCorePlugins()
    {
        string source = await CompositionRootSourceAsync();

        await Assert.That(
            source.Contains("plugins.Where(p => p is { Enabled: true, IsResolvable: true })", StringComparison.Ordinal))
            .IsTrue();
    }

    /// <summary>
    /// 插件事务终态广播守卫（2026-09-21 v0.1.8 实机回归）：
    /// 终态必须发显式终态意图 <c>UpdatesIntent.PluginOperationFinished</c>（清待办 + 摘除该插件的可更新行 +
    /// 回流一次检查），不得再借 <c>CheckUpdates</c> 兼职终态——后者兼任「发起检查」，
    /// 与在飞检查的完成回流交错时会把来源标记清掉，导致待办永不清空、全屏遮罩永久卡死。
    /// </summary>
    [Test]
    public async Task PluginOperationCompleted_BroadcastsTerminalIntentNotCheckUpdates()
    {
        string source = await CompositionRootSourceAsync();

        const string anchor = "private void OnPluginOperationChanged(";
        int start = source.IndexOf(anchor, StringComparison.Ordinal);
        await Assert.That(start >= 0).IsTrue();

        int end = source.IndexOf("\n    private ", start + anchor.Length, StringComparison.Ordinal);
        string body = end < 0 ? source[start..] : source[start..end];

        await Assert.That(body.Contains("PluginsIntent.LoadPlugins()", StringComparison.Ordinal)).IsTrue();
        await Assert.That(body.Contains("UpdatesIntent.PluginOperationFinished(", StringComparison.Ordinal)).IsTrue();
        await Assert.That(body.Contains("UpdatesIntent.CheckUpdates()", StringComparison.Ordinal)).IsFalse();
    }

    /// <summary>
    /// 崩溃漂移自愈胶水守卫（组合根拆分批 1）：<c>TrackStartupAsync</c> 的 catch 必须先把启动失败
    /// 转交给 <c>RuntimeBootstrapper.TryHealIncompatiblePluginCrashAsync</c> 尝试自愈（肇事插件事务化升级），
    /// 自愈成功才 <c>RecordSuccess</c> 并复用 supervisor.Current 返回；自愈未命中/失败则回退
    /// <c>OnStartupFailureAsync</c> 走原失败计数链（连续失败进自动安全模式）。这条「组合根 catch → 自愈 →
    /// 成功 RecordSuccess / 失败 OnStartupFailureAsync」的胶水语义曾由已删的守卫
    /// TrackStartupAsync_HealsIncompatiblePluginCrash 锁定；迁移到 Bootstrapper 后须由本守卫继续锁住，
    /// 否则有人把自愈调用整段删掉，或把 RecordSuccess 错放到 heal 调用之前，都不会被发现。
    /// </summary>
    [Test]
    public async Task TrackStartupAsync_DelegatesCrashHealThenRecordsOrFails()
    {
        string source = await CompositionRootSourceAsync();

        // 锚定方法**定义**（裸名字会先命中 HandleActivateDshRuntimeAsync 等调用点）。
        const string anchor = "ValueTask<RuntimeSnapshot> TrackStartupAsync(";
        int start = source.IndexOf(anchor, StringComparison.Ordinal);
        await Assert.That(start >= 0).IsTrue();

        // 只取方法体：截止下一个 private 方法（OnStartupFailureAsync）定义之前。
        int end = source.IndexOf("\n    private ", start + anchor.Length, StringComparison.Ordinal);
        string body = end < 0 ? source[start..] : source[start..end];

        int healCall = body.IndexOf("TryHealIncompatiblePluginCrashAsync", StringComparison.Ordinal);
        await Assert.That(healCall >= 0).IsTrue();

        // RecordSuccess 必须出现在自愈调用之后（自愈成功的回流，而非 try 内启动成功的回流）。
        // 从 healCall 起搜，跳过 try 内那处更早的 RecordSuccess。
        // 失败计数迁入 Application 的 RuntimeRecoveryController（批 2b）：锚点改为 controller 调用。
        int recordSuccessAfterHeal = body.IndexOf("RecordSuccess()", healCall);
        await Assert.That(recordSuccessAfterHeal > healCall).IsTrue();

        // 自愈未命中/失败必须回退原失败计数链（经控制器进入安全模式）。
        await Assert.That(body.Contains("RecordFailureAsync", StringComparison.Ordinal)).IsTrue();
    }

    private static async Task<string> CompositionRootSourceAsync()
    {
        string? root = XamlScan.FindRepositoryRoot();
        await Assert.That(root).IsNotNull();

        string path = Path.Combine(root!, "src", "DshDesktop.App", "Composition", "DshCompositionRoot.cs");
        await Assert.That(File.Exists(path)).IsTrue();
        return (await File.ReadAllTextAsync(path)).Replace("\r\n", "\n");
    }

    /// <summary>
    /// 批 2b 迁移守卫：恢复环 / 失败计数 / 重接管判定 / 指标判定已迁入 Application 的 RuntimeRecoveryController，
    /// 组合根只留一行订阅转发 + 端口适配器（mediator / ConfigPersistence 闭包）。本守卫锁死"内联恢复逻辑已迁出"
    /// —— 下列内联符号不得再出现在组合根源码中，否则即回到迁移前形态。
    /// </summary>
    [Test]
    public async Task CompositionRoot_HasNoInlineRecoveryLogic()
    {
        string source = await CompositionRootSourceAsync();

        string[] migratedSymbols =
        [
            "_recoveryPlanner",
            "RecoveryRetryDelay",
            "RetryStartAfterDelayAsync",
            "_failureTracker",
            "StartupFailureTracker",
            "OnStartupFailureAsync",
            "TryReattachRuntimeAsync",
            "RecordStartupMetrics",
            "UpdateMetricsMonitor",
            "PersistReattachTarget(RuntimeSnapshot",
            "OnRuntimeStateChanged",
        ];

        foreach (string symbol in migratedSymbols)
        {
            await Assert.That(source.Contains(symbol, StringComparison.Ordinal)).IsFalse();
        }
    }

    /// <summary>
    /// 候选 4「配置写管道收敛」语义守卫：8 个纯同构开关处理器必须全部经泛型辅助
    /// <c>SetConfigFlagAsync</c> lambda 注册（不再各自独立方法体），且这些独立方法名不得残留。
    /// 收敛后路径仍只经 SaveConfigAsync 落盘，HasNoDirectConfigStoreSave 守卫继续有效。
    /// </summary>
    [Test]
    public async Task SetConfigFlags_RouteThroughSetConfigFlagHelper()
    {
        string source = await CompositionRootSourceAsync();

        // 8 个注册行均为 SetConfigFlagAsync(request, ...) lambda。
        int routed = 0;
        int index = source.IndexOf("SetConfigFlagAsync(request", StringComparison.Ordinal);
        while (index >= 0)
        {
            routed++;
            index = source.IndexOf("SetConfigFlagAsync(request", index + 1, StringComparison.Ordinal);
        }

        await Assert.That(routed).IsEqualTo(8);

        // 8 个旧显式方法体必须已删除（不得残留方法定义）。
        string[] removedHandlers =
        [
            "HandleSetMinimizeToTrayOnCloseAsync(",
            "HandleSetBackgroundUpdateCheckAsync(",
            "HandleSetAutoDownloadUpdatesAsync(",
            "HandleSetNotificationsEnabledAsync(",
            "HandleSetDshChannelAsync(",
            "HandleSetKeepRuntimeOnCloseAsync(",
            "HandleSetAutoSafeModeOnFailureAsync(",
            "HandleSetCheckUpdatesOnStartupAsync(",
        ];

        foreach (string handler in removedHandlers)
        {
            await Assert.That(source.Contains(handler, StringComparison.Ordinal)).IsFalse();
        }
    }
}
