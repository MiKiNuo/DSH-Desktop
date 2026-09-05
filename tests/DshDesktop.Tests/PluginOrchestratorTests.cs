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
            return Task.FromResult<IReadOnlyList<PluginInfo>>([new PluginInfo("dsh-foo", "1.0.0", false, true)]);
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
                new PluginInfo("@deepseek-ai/dsh", "0.1.2", true, true),  // 核心启用：不可动
                new PluginInfo("dsh-foo", "1.0.0", false, true),          // 第三方启用：应禁用
                new PluginInfo("dsh-bar", "1.0.0", false, false),         // 第三方已禁用：跳过
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

    private sealed class FakeProfileSnapshotter : IProfileSnapshotter
    {
        public string? RestoredSnapshotId { get; private set; }

        public Task<string> CreateSnapshotAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult("snap-1");
        }

        public Task RestoreAsync(string snapshotId, CancellationToken cancellationToken)
        {
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
    }
}
