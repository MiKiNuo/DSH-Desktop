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
    public async Task RestartAsync_ExecutesStopThenStart()
    {
        // ADR-0004：Restart = Stop+Start 原子编排，先停后启。
        var orchestrator = new FakeRuntimeOrchestrator();
        var supervisor = new RuntimeSupervisor(orchestrator, Serilog.Core.Logger.None);
        _ = await supervisor.StartAsync(Options, CancellationToken.None);

        RuntimeSnapshot snapshot = await supervisor.RestartAsync(Options, CancellationToken.None);

        await Assert.That(snapshot.Lifecycle).IsEqualTo(RuntimeLifecycle.Running);
        await Assert.That(orchestrator.Calls.Count).IsEqualTo(3);
        await Assert.That(orchestrator.Calls[0]).IsEqualTo("start");
        await Assert.That(orchestrator.Calls[1]).IsEqualTo("stop");
        await Assert.That(orchestrator.Calls[2]).IsEqualTo("start");

        await supervisor.StopAsync(CancellationToken.None); // 停健康循环，防后台泄漏
    }

    [Test]
    public async Task Exited_WhileRunning_PublishesStoppedSnapshotAndForwards()
    {
        var orchestrator = new FakeRuntimeOrchestrator();
        var supervisor = new RuntimeSupervisor(orchestrator, Serilog.Core.Logger.None);
        _ = await supervisor.StartAsync(Options, CancellationToken.None);

        // StartAsync 的阶段 Progress 回调经线程池异步落地（既有竞态）：可能晚于订阅、
        // 甚至晚于退出发布。收集改用信号量确定性等待 Stopped 快照到达，不再假设回调时序。
        var stoppedSignal = new TaskCompletionSource<RuntimeSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        supervisor.SnapshotChanged += (_, snapshot) =>
        {
            if (snapshot.Lifecycle is RuntimeLifecycle.Stopped)
            {
                stoppedSignal.TrySetResult(snapshot);
            }
        };
        int? forwardedExitCode = null;
        supervisor.Exited += (_, args) => forwardedExitCode = args.ExitCode;

        orchestrator.RaiseExited(1);

        // 事实层：进程已死 → 快照 Stopped；崩溃语义（Failed）由 Reducer 裁决（Q7），不在 Supervisor。
        RuntimeSnapshot stopped = await stoppedSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(stopped.Lifecycle).IsEqualTo(RuntimeLifecycle.Stopped);
        await Assert.That(supervisor.Current.Lifecycle).IsEqualTo(RuntimeLifecycle.Stopped);
        await Assert.That(forwardedExitCode).IsEqualTo(1);
    }

    [Test]
    public async Task Exited_WhileRunning_LogsCrashDetectedEvent()
    {
        // Phase 7 Issue 03：Running 中收到退出 = 崩溃，补发 Runtime.Crash.Detected 结构化事件。
        var orchestrator = new FakeRuntimeOrchestrator();
        var sink = new CollectingSink();
        var supervisor = new RuntimeSupervisor(orchestrator, CreateLogger(sink));
        _ = await supervisor.StartAsync(Options, CancellationToken.None);

        orchestrator.RaiseExited(1);

        await Assert.That(sink.Messages.Any(m => m.StartsWith("Runtime.Crash.Detected", StringComparison.Ordinal)))
            .IsTrue();

        await supervisor.StopAsync(CancellationToken.None); // 停健康循环，防后台泄漏
    }

    [Test]
    public async Task Exited_OnOrchestratedStop_DoesNotLogCrashDetectedEvent()
    {
        // 主动停止已先把生命周期切到 Stopping，退出事件不得误报崩溃。
        var orchestrator = new FakeRuntimeOrchestrator { RaiseExitedOnStop = true };
        var sink = new CollectingSink();
        var supervisor = new RuntimeSupervisor(orchestrator, CreateLogger(sink));
        _ = await supervisor.StartAsync(Options, CancellationToken.None);

        await supervisor.StopAsync(CancellationToken.None);

        await Assert.That(sink.Messages.Any(m => m.StartsWith("Runtime.Crash.Detected", StringComparison.Ordinal)))
            .IsFalse();
    }

    [Test]
    public async Task StopAsync_DuringStarting_ThenExited_DoesNotLogCrashDetectedEvent()
    {
        // F1：Starting 中被停（如应用退出时 Shutdown 无条件 StopAsync）同样是编排停止，
        // 编排器杀进程触发的退出事件不得误报崩溃；快照落 Stopped 而非 Failed。
        var startGate = new TaskCompletionSource<RuntimeStartResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var orchestrator = new FakeRuntimeOrchestrator
        {
            RaiseExitedOnStop = true,
            StartAsyncHandler = () => startGate.Task,
        };
        var sink = new CollectingSink();
        var supervisor = new RuntimeSupervisor(orchestrator, CreateLogger(sink));

        _ = supervisor.StartAsync(Options, CancellationToken.None); // 启动挂起在编排器上
        await Assert.That(supervisor.Current.Lifecycle).IsEqualTo(RuntimeLifecycle.Starting);

        await supervisor.StopAsync(CancellationToken.None);

        await Assert.That(supervisor.Current.Lifecycle).IsEqualTo(RuntimeLifecycle.Stopped);
        await Assert.That(sink.Messages.Any(m => m.StartsWith("Runtime.Crash.Detected", StringComparison.Ordinal)))
            .IsFalse();
    }

    private static Serilog.ILogger CreateLogger(CollectingSink sink)
    {
        return new Serilog.LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
    }

    private sealed class CollectingSink : Serilog.Core.ILogEventSink
    {
        public List<string> Messages { get; } = [];

        public void Emit(Serilog.Events.LogEvent logEvent)
        {
            Messages.Add(logEvent.RenderMessage());
        }
    }

    private sealed class FakeRuntimeOrchestrator : IRuntimeOrchestrator
    {
        private static readonly RuntimeStartResult DefaultResult = new(42, 5000, "http://127.0.0.1:5000/?token=x");

        public Func<RuntimeLaunchOptions, RuntimeStartResult>? StartHandler { get; init; }

        /// <summary>异步启动宿主（如挂起的启动任务）；设置时优先于 <see cref="StartHandler"/>。</summary>
        public Func<Task<RuntimeStartResult>>? StartAsyncHandler { get; init; }

        /// <summary>模拟真实进程宿主：主动停止（Kill）同样触发退出事件。</summary>
        public bool RaiseExitedOnStop { get; init; }

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public List<string> Calls { get; } = [];

        public event EventHandler<RuntimeExitedEventArgs>? Exited;

        public Task<RuntimeStartResult> StartAsync(RuntimeLaunchOptions options, CancellationToken cancellationToken)
        {
            StartCount++;
            Calls.Add("start");
            options.Progress?.Report(RuntimeStartupStage.Spawning);
            return StartAsyncHandler?.Invoke()
                ?? Task.FromResult(StartHandler?.Invoke(options) ?? DefaultResult);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCount++;
            Calls.Add("stop");
            if (RaiseExitedOnStop)
            {
                Exited?.Invoke(this, new RuntimeExitedEventArgs(0));
            }

            return Task.CompletedTask;
        }

        public void RaiseExited(int? exitCode)
        {
            Exited?.Invoke(this, new RuntimeExitedEventArgs(exitCode));
        }
    }
}
