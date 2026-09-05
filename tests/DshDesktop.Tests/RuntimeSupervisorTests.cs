using DshDesktop.Application.Runtime;
using DshDesktop.Domain.Runtime;

namespace DshDesktop.Tests;

/// <summary>
/// Runtime 监管器测试（§16：包装编排器、崩溃检测、快照推送）。
/// 健康轮询（5s × 3 次）属集成行为，已由 Phase 2 实测覆盖，不在此做慢速单测。
/// </summary>
public sealed class RuntimeSupervisorTests
{
    private static readonly RuntimeLaunchOptions Options = new(
        "node", "entry.js", null, ".", ".", "127.0.0.1", 0, TimeSpan.FromSeconds(5));

    [Test]
    public async Task StartAsync_Success_ReturnsRunningHealthySnapshot()
    {
        var orchestrator = new FakeRuntimeOrchestrator();
        var supervisor = new RuntimeSupervisor(orchestrator, Serilog.Core.Logger.None);

        RuntimeSnapshot snapshot = await supervisor.StartAsync(Options, CancellationToken.None);

        await Assert.That(snapshot.Lifecycle).IsEqualTo(RuntimeLifecycle.Running);
        await Assert.That(snapshot.Health).IsEqualTo(RuntimeHealth.Healthy);
        await Assert.That(snapshot.StartupStage).IsEqualTo(RuntimeStartupStage.Ready);
        await Assert.That(snapshot.Port).IsEqualTo(5000);
        await Assert.That(snapshot.ProcessId).IsEqualTo(42);
        await Assert.That(snapshot.StartupElapsed).IsNotNull();
        await Assert.That(orchestrator.StartCount).IsEqualTo(1);

        await supervisor.StopAsync(CancellationToken.None); // 停健康循环，防后台泄漏
    }

    [Test]
    public async Task StartAsync_OrchestratorThrows_Propagates()
    {
        var orchestrator = new FakeRuntimeOrchestrator
        {
            StartHandler = _ => throw new InvalidOperationException("进程拉起失败"),
        };
        var supervisor = new RuntimeSupervisor(orchestrator, Serilog.Core.Logger.None);

        await Assert.That(async () => await supervisor.StartAsync(Options, CancellationToken.None))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task StopAsync_AfterRunning_ReturnsToStopped()
    {
        var orchestrator = new FakeRuntimeOrchestrator();
        var supervisor = new RuntimeSupervisor(orchestrator, Serilog.Core.Logger.None);
        _ = await supervisor.StartAsync(Options, CancellationToken.None);

        await supervisor.StopAsync(CancellationToken.None);

        await Assert.That(supervisor.Current.Lifecycle).IsEqualTo(RuntimeLifecycle.Stopped);
        await Assert.That(supervisor.Current.Port).IsNull();
        await Assert.That(orchestrator.StopCount).IsEqualTo(1);
    }

    [Test]
    public async Task Exited_WhileRunning_PublishesStoppedSnapshotAndForwards()
    {
        var orchestrator = new FakeRuntimeOrchestrator();
        var supervisor = new RuntimeSupervisor(orchestrator, Serilog.Core.Logger.None);
        _ = await supervisor.StartAsync(Options, CancellationToken.None);

        List<RuntimeSnapshot> snapshots = [];
        supervisor.SnapshotChanged += (_, snapshot) => snapshots.Add(snapshot);
        int? forwardedExitCode = null;
        supervisor.Exited += (_, args) => forwardedExitCode = args.ExitCode;

        orchestrator.RaiseExited(1);

        // 事实层：进程已死 → 快照 Stopped；崩溃语义（Failed）由 Reducer 裁决（Q7），不在 Supervisor。
        await Assert.That(supervisor.Current.Lifecycle).IsEqualTo(RuntimeLifecycle.Stopped);
        await Assert.That(forwardedExitCode).IsEqualTo(1);
        await Assert.That(snapshots.Count).IsEqualTo(1);
        await Assert.That(snapshots[0].Lifecycle).IsEqualTo(RuntimeLifecycle.Stopped);
    }

    private sealed class FakeRuntimeOrchestrator : IRuntimeOrchestrator
    {
        private static readonly RuntimeStartResult DefaultResult = new(42, 5000, "http://127.0.0.1:5000/?token=x");

        public Func<RuntimeLaunchOptions, RuntimeStartResult>? StartHandler { get; init; }

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public event EventHandler<RuntimeExitedEventArgs>? Exited;

        public Task<RuntimeStartResult> StartAsync(RuntimeLaunchOptions options, CancellationToken cancellationToken)
        {
            StartCount++;
            options.Progress?.Report(RuntimeStartupStage.Spawning);
            return Task.FromResult(StartHandler?.Invoke(options) ?? DefaultResult);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCount++;
            return Task.CompletedTask;
        }

        public void RaiseExited(int? exitCode)
        {
            Exited?.Invoke(this, new RuntimeExitedEventArgs(exitCode));
        }
    }
}
