using DshDesktop.Application.Plugins;
using DshDesktop.Application.Runtime;
using DshDesktop.Domain.Plugins;
using DshDesktop.Domain.Runtime;

namespace DshDesktop.Tests;

/// <summary>
/// 插件编排器测试（§43.3：Fake 应用服务覆盖事务路径）。
/// 覆盖 §19 安装事务的失败路径：任何失败 → 回滚快照 → 尽力重启原 Runtime → 抛原异常。
/// </summary>
public sealed class PluginOrchestratorTests
{
    private static readonly RuntimeLaunchOptions TestOptions = new(
        "node", "entry.js", null, ".", ".", "127.0.0.1", 0, TimeSpan.FromSeconds(5));

    private static RuntimeLaunchOptions OptionsFactory()
    {
        return TestOptions;
    }

    [Test]
    public async Task InstallAsync_WhenInstallFails_RollsBackRestartsAndRethrows()
    {
        var pluginManager = new FailingPluginManager();
        var snapshotter = new FakeProfileSnapshotter();
        var supervisor = new FakeRuntimeSupervisor();
        var orchestrator = new PluginOrchestrator(
            pluginManager,
            snapshotter,
            supervisor,
            OptionsFactory,
            Serilog.Core.Logger.None);
        var stages = new List<PluginOperationStage>();
        orchestrator.OperationChanged += (_, operation) => stages.Add(operation.Stage);

        await Assert.That(async () => await orchestrator.InstallAsync("bad-plugin", CancellationToken.None))
            .Throws<InvalidOperationException>();

        await Assert.That(snapshotter.RestoredSnapshotId).IsEqualTo("snap-1");
        await Assert.That(supervisor.StartCount).IsEqualTo(1); // 尽力重启原 Runtime（§19）
        await Assert.That(supervisor.StopCount).IsEqualTo(1);
        await Assert.That(stages.Count).IsEqualTo(6);
        await Assert.That(stages[0]).IsEqualTo(PluginOperationStage.Preparing);
        await Assert.That(stages[1]).IsEqualTo(PluginOperationStage.CreatingSnapshot);
        await Assert.That(stages[2]).IsEqualTo(PluginOperationStage.StoppingRuntime);
        await Assert.That(stages[3]).IsEqualTo(PluginOperationStage.Installing);
        await Assert.That(stages[4]).IsEqualTo(PluginOperationStage.RollingBack);
        await Assert.That(stages[5]).IsEqualTo(PluginOperationStage.Failed);
    }

    private sealed class FailingPluginManager : IPluginManager
    {
        public Task<IReadOnlyList<PluginInfo>> ListPluginsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<PluginInfo>>(Array.Empty<PluginInfo>());
        }

        public Task SetEnabledAsync(string name, bool enabled, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task UninstallAsync(string name, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<string> InstallAsync(string source, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("npm 安装失败（Fake）");
        }
    }

    [Test]
    public async Task InstallAsync_WhenRollbackRestoreFails_StillRestartsRuntime()
    {
        // 回归：快照恢复也失败时，RollbackAsync 不能提前 return 跳过「尽力重启 Runtime」，
        // 否则被事务杀掉的 Runtime 无人拉起（用户必须人工点「重试启动」）。
        // §19：Restore 失败也必须重启原 Runtime，且 Failed 只发布一次，错误含真实原因。
        var pluginManager = new FailingPluginManager();
        var snapshotter = new FakeProfileSnapshotter { ThrowOnRestore = true };
        var supervisor = new FakeRuntimeSupervisor();
        var orchestrator = new PluginOrchestrator(
            pluginManager,
            snapshotter,
            supervisor,
            OptionsFactory,
            Serilog.Core.Logger.None);
        var operations = new List<PluginOperation>();
        orchestrator.OperationChanged += (_, operation) => operations.Add(operation);

        await Assert.That(async () => await orchestrator.InstallAsync("bad-plugin", CancellationToken.None))
            .Throws<InvalidOperationException>();

        // Runtime 仍被尽力重启（修复前快照恢复失败会跳过此处）。
        await Assert.That(supervisor.StartCount).IsEqualTo(1);
        // Failed 阶段只发布一次。
        await Assert.That(operations.Count(o => o.Stage == PluginOperationStage.Failed)).IsEqualTo(1);
        PluginOperation failed = operations.Single(o => o.Stage == PluginOperationStage.Failed);
        // 错误文案同时包含原始安装错误与「回滚恢复也失败」的真实原因。
        await Assert.That(failed.Error).Contains("npm 安装失败（Fake）");
        await Assert.That(failed.Error).Contains("回滚恢复也失败");
    }

    [Test]
    public async Task InstallAsync_Success_CompletesFullStageSequence()
    {
        var pluginManager = new SucceedingPluginManager();
        var snapshotter = new FakeProfileSnapshotter();
        var supervisor = new FakeRuntimeSupervisor();
        var orchestrator = new PluginOrchestrator(
            pluginManager,
            snapshotter,
            supervisor,
            OptionsFactory,
            Serilog.Core.Logger.None);
        var stages = new List<PluginOperationStage>();
        orchestrator.OperationChanged += (_, operation) => stages.Add(operation.Stage);

        string installed = await orchestrator.InstallAsync("dsh-foo", CancellationToken.None);

        await Assert.That(installed).IsEqualTo("dsh-foo");
        await Assert.That(snapshotter.RestoredSnapshotId).IsNull(); // 成功不回滚
        await Assert.That(stages.Count).IsEqualTo(8);
        await Assert.That(stages[0]).IsEqualTo(PluginOperationStage.Preparing);
        await Assert.That(stages[1]).IsEqualTo(PluginOperationStage.CreatingSnapshot);
        await Assert.That(stages[2]).IsEqualTo(PluginOperationStage.StoppingRuntime);
        await Assert.That(stages[3]).IsEqualTo(PluginOperationStage.Installing);
        await Assert.That(stages[4]).IsEqualTo(PluginOperationStage.Validating);
        await Assert.That(stages[5]).IsEqualTo(PluginOperationStage.StartingRuntime);
        await Assert.That(stages[6]).IsEqualTo(PluginOperationStage.HealthChecking);
        await Assert.That(stages[7]).IsEqualTo(PluginOperationStage.Completed);
    }

    [Test]
    public async Task InstallAsync_WhenPluginDeclaredButUnresolvable_ValidationFails()
    {
        // 回归（2026-09-14）：仅断言 manifest 的 Enabled 不够——声明-but-未物化
        // （悬空 junction）的插件会让 DSH 启动期 resolveBundleDir 抛错。
        // 校验必须要求磁盘可解析（IsResolvable），否则失败并回滚。
        var pluginManager = new DeclaredButUnresolvableManager();
        var snapshotter = new FakeProfileSnapshotter();
        var supervisor = new FakeRuntimeSupervisor();
        var orchestrator = new PluginOrchestrator(
            pluginManager,
            snapshotter,
            supervisor,
            OptionsFactory,
            Serilog.Core.Logger.None);

        await Assert.That(async () => await orchestrator.InstallAsync("dsh-foo", CancellationToken.None))
            .Throws<InvalidOperationException>();

        await Assert.That(snapshotter.RestoredSnapshotId).IsEqualTo("snap-1"); // 校验失败也回滚
    }

    [Test]
    public async Task DisableAllThirdPartyAsync_OnlyDisablesEnabledThirdParty()
    {
        var pluginManager = new MixedPluginManager();
        var snapshotter = new FakeProfileSnapshotter();
        var supervisor = new FakeRuntimeSupervisor();
        var orchestrator = new PluginOrchestrator(
            pluginManager,
            snapshotter,
            supervisor,
            OptionsFactory,
            Serilog.Core.Logger.None);

        await orchestrator.DisableAllThirdPartyAsync(CancellationToken.None);

        // 核心插件不可动、已禁用的跳过：只有 dsh-foo 被禁用。
        await Assert.That(pluginManager.SetCalls.Count).IsEqualTo(1);
        await Assert.That(pluginManager.SetCalls[0].Name).IsEqualTo("dsh-foo");
        await Assert.That(pluginManager.SetCalls[0].Enabled).IsFalse();
    }

    private sealed class SucceedingPluginManager : IPluginManager
    {
        public Task<IReadOnlyList<PluginInfo>> ListPluginsAsync(CancellationToken cancellationToken)
        {
            // 校验要求：已安装且启用（PluginOrchestrator 的 Validating 阶段）。
            return Task.FromResult<IReadOnlyList<PluginInfo>>([new PluginInfo("dsh-foo", "1.0.0", false, true, "")]);
        }

        public Task SetEnabledAsync(string name, bool enabled, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task UninstallAsync(string name, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<string> InstallAsync(string source, CancellationToken cancellationToken)
        {
            return Task.FromResult("dsh-foo");
        }
    }

    private sealed class MixedPluginManager : IPluginManager
    {
        public List<(string Name, bool Enabled)> SetCalls { get; } = [];
        public Task<IReadOnlyList<PluginInfo>> ListPluginsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<PluginInfo>>(
            [
                new PluginInfo("@deepseek-ai/dsh", "0.1.2", true, true, ""),  // 核心启用：不可动
                new PluginInfo("dsh-foo", "1.0.0", false, true, ""),          // 第三方启用：应禁用
                new PluginInfo("dsh-bar", "1.0.0", false, false, ""),         // 第三方已禁用：跳过
            ]);
        }

        public Task SetEnabledAsync(string name, bool enabled, CancellationToken cancellationToken)
        {
            SetCalls.Add((name, enabled));
            return Task.CompletedTask;
        }

        public Task UninstallAsync(string name, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<string> InstallAsync(string source, CancellationToken cancellationToken)
        {
            return Task.FromResult(source);
        }
    }

    private sealed class DeclaredButUnresolvableManager : IPluginManager
    {
        public Task<IReadOnlyList<PluginInfo>> ListPluginsAsync(CancellationToken cancellationToken)
        {
            // 已声明、已启用，但磁盘不可解析（悬空 junction / 中断安装残留）。
            return Task.FromResult<IReadOnlyList<PluginInfo>>(
                [new PluginInfo("dsh-foo", "1.0.0", false, true, "", IsResolvable: false)]);
        }

        public Task SetEnabledAsync(string name, bool enabled, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task UninstallAsync(string name, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<string> InstallAsync(string source, CancellationToken cancellationToken)
        {
            return Task.FromResult("dsh-foo");
        }
    }

    private sealed class FakeProfileSnapshotter : IProfileSnapshotter
    {
        public string? RestoredSnapshotId { get; private set; }

        public int CreateCount { get; private set; }

        public bool ThrowOnRestore { get; set; }

        public Task<string> CreateSnapshotAsync(CancellationToken cancellationToken)
        {
            CreateCount++;
            return Task.FromResult("snap-1");
        }

        public Task RestoreAsync(string snapshotId, CancellationToken cancellationToken)
        {
            if (ThrowOnRestore)
            {
                throw new InvalidOperationException("快照恢复失败（Fake）");
            }

            RestoredSnapshotId = snapshotId;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRuntimeSupervisor : IRuntimeSupervisor
    {
        private static readonly RuntimeSnapshot RunningHealthy = new(
            RuntimeLifecycle.Running, RuntimeHealth.Healthy, RuntimeStartupStage.Ready,
            TimeSpan.FromSeconds(1), 1234, 5678, "http://127.0.0.1:5678/?token=x");

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public RuntimeSnapshot Current => RunningHealthy;

        public event EventHandler<RuntimeSnapshot>? SnapshotChanged
        {
            add { }
            remove { }
        }

        public event EventHandler<RuntimeExitedEventArgs>? Exited
        {
            add { }
            remove { }
        }

        public Task<RuntimeSnapshot> StartAsync(RuntimeLaunchOptions options, CancellationToken cancellationToken)
        {
            StartCount++;
            return Task.FromResult(RunningHealthy);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCount++;
            return Task.CompletedTask;
        }

        public Task<RuntimeSnapshot> RestartAsync(RuntimeLaunchOptions options, CancellationToken cancellationToken)
        {
            throw new NotSupportedException("Fake 不支持 RestartAsync。");
        }
    }
}
