using DshDesktop.Application.Runtime;
using DshDesktop.Domain.Runtime;
using R3;

namespace DshDesktop.Tests;

/// <summary>
/// RuntimeRecoveryController 测试（批 2b）：恢复环 + 失败计数 + 重接管判定 + 指标判定迁入 Application。
/// 全部副作用经假端口记录调用，恢复延迟用可控 FakeDelay（参考 BoundedRecoveryPlanner 既有直测范式）。
/// </summary>
public sealed class RuntimeRecoveryControllerTests
{
    [Test]
    public async Task RecoveryEvents_ReachDiagnosticsThroughProductionSink()
    {
        var hub = new DshDesktop.Application.Diagnostics.DiagnosticsHub();
        var events = new List<DshDesktop.Domain.Diagnostics.DiagnosticEvent>();
        using var subscription = hub.Events.Subscribe(events.Add);
        using var logger = new Serilog.LoggerConfiguration()
            .WriteTo.Sink(new DshDesktop.App.Logging.DiagnosticsSink(hub)).CreateLogger();
        var controller = new RuntimeRecoveryController(
            new FakeHost(), new FakeConfig(), new FakeSupervisor(),
            new RuntimeReattacher(new FakeProbe(), logger), CancellationToken.None,
            delay: (_, _) => Task.CompletedTask, logger: logger);

        controller.OnState(RuntimeLifecycle.Failed);
        await controller.RecordFailureAsync(CancellationToken.None);
        await controller.RecordFailureAsync(CancellationToken.None);

        await Assert.That(events.Count).IsEqualTo(2);
        await Assert.That(events[0].Message.StartsWith(
            DshDesktop.Application.Diagnostics.DiagnosticEventNames.RuntimeAutoRecoveryAttempted,
            StringComparison.Ordinal)).IsTrue();
        await Assert.That(events[1].Message.StartsWith(
            DshDesktop.Application.Diagnostics.DiagnosticEventNames.RuntimeAutoSafeModeEntered,
            StringComparison.Ordinal)).IsTrue();
    }

    // ===== ① 恢复环 =====

    [Test]
    public async Task OnState_NewFailedEdge_DelaysThenRedispatchesStartRuntime()
    {
        var delay = new FakeDelay();
        var host = new FakeHost();
        var controller = NewController(host, delay: delay.DelayAsync);

        controller.OnState(RuntimeLifecycle.Stopped);
        controller.OnState(RuntimeLifecycle.Failed);

        await Task.WhenAny(delay.Invoked, Task.Delay(TimeSpan.FromSeconds(5)));
        delay.Complete();
        await Task.WhenAny(host.RedispatchReceived.Task, Task.Delay(TimeSpan.FromSeconds(5)));

        await Assert.That(delay.Requested).IsEqualTo(RuntimeRecoveryController.RecoveryRetryDelay);
        await Assert.That(host.RedispatchCount).IsEqualTo(1);
    }

    [Test]
    public async Task OnState_FailedToFailed_NotANewEdge_NoRedispatch()
    {
        var delay = new FakeDelay { AutoComplete = true };
        var host = new FakeHost();
        var controller = NewController(host, delay: delay.DelayAsync);

        controller.OnState(RuntimeLifecycle.Stopped);
        controller.OnState(RuntimeLifecycle.Failed);   // 新沿：排程
        controller.OnState(RuntimeLifecycle.Failed);   // Failed→Failed：非新沿

        await Task.Delay(100);
        await Assert.That(host.RedispatchCount).IsEqualTo(1);
    }

    [Test]
    public async Task OnState_SecondFailureCycleWithoutRunning_NoRetry()
    {
        // 上限恒 1 次（MaxAttemptsPerFailure=1）且仅有 Running 沿才 Reset：
        // Stopped→Failed 消耗唯一一次尝试，随后 Stopped→Failed 再次新沿因上限拒绝。
        var delay = new FakeDelay { AutoComplete = true };
        var host = new FakeHost();
        var controller = NewController(host, delay: delay.DelayAsync);

        controller.OnState(RuntimeLifecycle.Stopped);
        controller.OnState(RuntimeLifecycle.Failed);    // 新沿 + 尝试，排程
        controller.OnState(RuntimeLifecycle.Stopped);   // 不 Reset
        controller.OnState(RuntimeLifecycle.Failed);    // 新沿但上限已耗尽

        await Task.Delay(100);
        await Assert.That(host.RedispatchCount).IsEqualTo(1);
    }

    [Test]
    public async Task OnState_RunningEdge_ResetsPlanner_AllowsNextCycle()
    {
        var delay = new FakeDelay { AutoComplete = true };
        var host = new FakeHost();
        var controller = NewController(host, delay: delay.DelayAsync);

        controller.OnState(RuntimeLifecycle.Stopped);
        controller.OnState(RuntimeLifecycle.Failed);    // 第 1 周期：排程
        controller.OnState(RuntimeLifecycle.Running);   // Reset
        controller.OnState(RuntimeLifecycle.Failed);    // 第 2 周期：再次排程

        await Task.Delay(100);
        await Assert.That(host.RedispatchCount).IsEqualTo(2);
    }

    [Test]
    public async Task OnState_CancelledLifetime_OceSwallowed_NoRedispatch()
    {
        // Shutdown 先 Cancel 取消源：延迟任务 OCE 静默吞，不再派发 StartRuntime。
        var delay = new FakeDelay();
        var host = new FakeHost();
        using var cts = new CancellationTokenSource();
        var controller = NewController(host, lifetimeToken: cts.Token, delay: delay.DelayAsync);

        controller.OnState(RuntimeLifecycle.Stopped);
        controller.OnState(RuntimeLifecycle.Failed);   // 进入延迟等待
        await Task.WhenAny(delay.Invoked, Task.Delay(TimeSpan.FromSeconds(5)));
        cts.Cancel();                                  // 模拟 Shutdown：Cancel 取消源

        await Task.Delay(100);
        await Assert.That(host.RedispatchCount).IsEqualTo(0);
        await Assert.That(delay.Cancelled).IsTrue();
    }

    // ===== ② 失败计数（由 TrackStartupAsync 委托）=====

    [Test]
    public async Task RecordFailure_TwoConsecutive_WithSwitchOn_EnterSafeMode()
    {
        var host = new FakeHost();
        var config = new FakeConfig { AutoSafeModeOnFailure = true };
        var controller = NewController(host, config);

        controller.RecordSuccess();                    // 清零起点
        await controller.RecordFailureAsync(CancellationToken.None);   // 第 1 次：不触发
        await controller.RecordFailureAsync(CancellationToken.None);   // 第 2 次：触发

        await Assert.That(host.EnterSafeModeCount).IsEqualTo(1);
    }

    [Test]
    public async Task RecordFailure_SwitchOff_NeverEntersSafeMode()
    {
        var host = new FakeHost();
        var config = new FakeConfig { AutoSafeModeOnFailure = false };
        var controller = NewController(host, config);

        await controller.RecordFailureAsync(CancellationToken.None);
        await controller.RecordFailureAsync(CancellationToken.None);
        await controller.RecordFailureAsync(CancellationToken.None);

        await Assert.That(host.EnterSafeModeCount).IsEqualTo(0);
    }

    // ===== ③ 重接管判定（ADR-0005）=====

    [Test]
    public async Task TryReattach_SwitchOff_ReturnsFalseWithoutProbing()
    {
        var host = new FakeHost();
        var config = new FakeConfig { KeepRuntimeOnClose = false };
        var probe = new FakeProbe();
        var reattacher = new RuntimeReattacher(probe, Serilog.Core.Logger.None);
        var controller = NewController(host, config, reattacher: reattacher);

        bool adopted = await controller.TryReattachAsync(CancellationToken.None);

        await Assert.That(adopted).IsFalse();
        await Assert.That(probe.ProcessAliveChecked).IsEqualTo(0);
        await Assert.That(host.ClearReattachCount).IsEqualTo(0);
    }

    [Test]
    public async Task TryReattach_SwitchOnButNoRecord_ReturnsFalseWithoutProbing()
    {
        var host = new FakeHost();
        var config = new FakeConfig { KeepRuntimeOnClose = true, LastRuntimePid = null, LastRuntimePort = null };
        var probe = new FakeProbe();
        var reattacher = new RuntimeReattacher(probe, Serilog.Core.Logger.None);
        var controller = NewController(host, config, reattacher: reattacher);

        bool adopted = await controller.TryReattachAsync(CancellationToken.None);

        await Assert.That(adopted).IsFalse();
        await Assert.That(probe.ProcessAliveChecked).IsEqualTo(0);
    }

    [Test]
    public async Task TryReattach_SwitchOnWithRecord_Alive_DegradesToRestart_ClearsRecordAndDoesNotAdopt()
    {
        // 生产态 canRestoreSessionUrl 恒 false（ADR-0005：Session URL 一次性且禁止落盘）→
        // 存活 Runtime 走退化重启分支（杀旧进程 + 清记录），不接管、不派发 RuntimeStarted。
        // Adopted 分支（canRestoreSessionUrl=true）由 RuntimeReattacherTests 在重接管层覆盖。
        var host = new FakeHost();
        var config = new FakeConfig { KeepRuntimeOnClose = true, LastRuntimePid = 4321, LastRuntimePort = 5678, Host = "127.0.0.1" };
        var probe = new FakeProbe { ProcessAlive = true, HttpAlive = true };
        var reattacher = new RuntimeReattacher(probe, Serilog.Core.Logger.None);
        var supervisor = new FakeSupervisor();
        var controller = NewController(host, config, supervisor, reattacher);

        bool adopted = await controller.TryReattachAsync(CancellationToken.None);

        await Assert.That(adopted).IsFalse();
        await Assert.That(probe.KilledPid).IsEqualTo(4321);
        await Assert.That(supervisor.AdoptRunningCalled).IsFalse();
        await Assert.That(host.RuntimeStartedCount).IsEqualTo(0);
        await Assert.That(host.ClearReattachCount).IsEqualTo(1);
    }

    [Test]
    public async Task TryReattach_SwitchOnWithRecord_ProcessDead_NotFound_ClearsRecord()
    {
        var host = new FakeHost();
        var config = new FakeConfig { KeepRuntimeOnClose = true, LastRuntimePid = 4321, LastRuntimePort = 5678 };
        var probe = new FakeProbe { ProcessAlive = false };
        var reattacher = new RuntimeReattacher(probe, Serilog.Core.Logger.None);
        var controller = NewController(host, config, reattacher: reattacher);

        bool adopted = await controller.TryReattachAsync(CancellationToken.None);

        await Assert.That(adopted).IsFalse();
        await Assert.That(probe.HttpProbeCount).IsEqualTo(0);
        await Assert.That(host.ClearReattachCount).IsEqualTo(1);
        await Assert.That(host.RuntimeStartedCount).IsEqualTo(0);
    }

    // ===== ④ 指标判定（Phase 8 Issue 03）=====

    [Test]
    public async Task OnSnapshot_ReadyElapsedUnchanged_DoesNotRedispatchesTimeline()
    {
        var host = new FakeHost();
        var config = new FakeConfig { KeepRuntimeOnClose = true, LastRuntimePid = 99, LastRuntimePort = 99 };
        var supervisor = new FakeSupervisor();
        var controller = NewController(host, config, supervisor);

        var ready = new RuntimeSnapshot(
            RuntimeLifecycle.Running, RuntimeHealth.Healthy, RuntimeStartupStage.Ready,
            TimeSpan.FromMilliseconds(100), 11, 22, "http://h:22/");

        // 首达：派发一次。
        controller.OnSnapshot(ready, RuntimeLifecycle.Stopped);
        await Assert.That(host.TimelineCount).IsEqualTo(1);
        await Assert.That(host.StartupElapsedCount).IsEqualTo(1);
        await Assert.That(host.PersistStartupElapsedCount).IsEqualTo(1);

        // elapsed 未变：不再派发（去重）。
        controller.OnSnapshot(ready, RuntimeLifecycle.Running);
        await Assert.That(host.TimelineCount).IsEqualTo(1);
        await Assert.That(host.StartupElapsedCount).IsEqualTo(1);
    }

    [Test]
    public async Task OnSnapshot_ReadyElapsedChanged_RedispatchesTimeline()
    {
        var host = new FakeHost();
        var config = new FakeConfig { KeepRuntimeOnClose = true, LastRuntimePid = 99, LastRuntimePort = 99 };
        var supervisor = new FakeSupervisor();
        var controller = NewController(host, config, supervisor);

        var first = new RuntimeSnapshot(
            RuntimeLifecycle.Running, RuntimeHealth.Healthy, RuntimeStartupStage.Ready,
            TimeSpan.FromMilliseconds(100), 11, 22, "http://h:22/");
        var second = first with { StartupElapsed = TimeSpan.FromMilliseconds(250) };

        controller.OnSnapshot(first, RuntimeLifecycle.Stopped);
        controller.OnSnapshot(second, RuntimeLifecycle.Running);

        await Assert.That(host.TimelineCount).IsEqualTo(2);
        await Assert.That(host.StartupElapsedCount).IsEqualTo(2);
        await Assert.That(host.PersistStartupElapsedCount).IsEqualTo(2);
        await Assert.That(host.LastPersistedElapsedMs).IsEqualTo(250);
    }

    [Test]
    public async Task OnSnapshot_StartingResetsDedup_AllowsNextReadyDispatch()
    {
        var host = new FakeHost();
        var config = new FakeConfig { KeepRuntimeOnClose = true, LastRuntimePid = 99, LastRuntimePort = 99 };
        var supervisor = new FakeSupervisor();
        var controller = NewController(host, config, supervisor);

        var ready = new RuntimeSnapshot(
            RuntimeLifecycle.Running, RuntimeHealth.Healthy, RuntimeStartupStage.Ready,
            TimeSpan.FromMilliseconds(100), 11, 22, "http://h:22/");

        controller.OnSnapshot(ready, RuntimeLifecycle.Stopped);
        controller.OnSnapshot(ready with { Lifecycle = RuntimeLifecycle.Starting, StartupStage = RuntimeStartupStage.Validating }, RuntimeLifecycle.Running);
        controller.OnSnapshot(ready, RuntimeLifecycle.Stopped);   // Starting 重置后再次 Ready

        await Assert.That(host.TimelineCount).IsEqualTo(2);
    }

    [Test]
    public async Task OnSnapshot_RunningReconciles_WhenStoreNotRunning()
    {
        var host = new FakeHost();
        var config = new FakeConfig { KeepRuntimeOnClose = true, LastRuntimePid = 99, LastRuntimePort = 99 };
        var supervisor = new FakeSupervisor();
        var controller = NewController(host, config, supervisor);

        var running = new RuntimeSnapshot(
            RuntimeLifecycle.Running, RuntimeHealth.Healthy, RuntimeStartupStage.Ready,
            TimeSpan.FromMilliseconds(100), 11, 22, "http://h:22/");

        controller.OnSnapshot(running, RuntimeLifecycle.Stopped);   // 对账派发
        await Assert.That(host.RuntimeStartedCount).IsEqualTo(1);

        controller.OnSnapshot(running, RuntimeLifecycle.Running);    // 已 Running：不重复
        await Assert.That(host.RuntimeStartedCount).IsEqualTo(1);
    }

    [Test]
    public async Task OnSnapshot_Running_StartsMetricsMonitor_Stopped_Stops()
    {
        var host = new FakeHost();
        var config = new FakeConfig { KeepRuntimeOnClose = true, LastRuntimePid = 99, LastRuntimePort = 99 };
        var supervisor = new FakeSupervisor();
        var controller = NewController(host, config, supervisor);

        controller.OnSnapshot(
            new RuntimeSnapshot(RuntimeLifecycle.Running, RuntimeHealth.Healthy, RuntimeStartupStage.Ready,
                TimeSpan.FromMilliseconds(1), 777, 22, null), RuntimeLifecycle.Stopped);
        await Assert.That(host.StartMetricsCount).IsEqualTo(1);
        await Assert.That(host.LastStartedMetricsPid).IsEqualTo(777);

        controller.OnSnapshot(
            new RuntimeSnapshot(RuntimeLifecycle.Stopped, RuntimeHealth.Unknown, RuntimeStartupStage.None,
                null, null, null, null), RuntimeLifecycle.Running);
        await Assert.That(host.StopMetricsCount).IsEqualTo(1);
    }

    // ===== 工厂 =====

    private static RuntimeRecoveryController NewController(
        FakeHost host,
        FakeConfig? config = null,
        FakeSupervisor? supervisor = null,
        RuntimeReattacher? reattacher = null,
        CancellationToken lifetimeToken = default,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
        => new(
            host,
            config ?? new FakeConfig(),
            supervisor ?? new FakeSupervisor(),
            reattacher ?? new RuntimeReattacher(new FakeProbe { ProcessAlive = false }, Serilog.Core.Logger.None),
            lifetimeToken,
            delay);

    // ===== 假端口与替身 =====

    private sealed class FakeHost : IRuntimeRecoveryHost
    {
        public int RedispatchCount { get; private set; }
        public int EnterSafeModeCount { get; private set; }
        public int RuntimeStartedCount { get; private set; }
        public int TimelineCount { get; private set; }
        public int StartupElapsedCount { get; private set; }
        public int StartMetricsCount { get; private set; }
        public int StopMetricsCount { get; private set; }
        public int PersistReattachCount { get; private set; }
        public int ClearReattachCount { get; private set; }
        public int PersistStartupElapsedCount { get; private set; }
        public long LastPersistedElapsedMs { get; private set; }

        public TaskCompletionSource RedispatchReceived { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task RedispatchStartRuntimeAsync(CancellationToken cancellationToken)
        {
            RedispatchCount++;
            RedispatchReceived.TrySetResult();
            return Task.CompletedTask;
        }

        public Task EnterSafeModeAsync(CancellationToken cancellationToken)
        {
            EnterSafeModeCount++;
            return Task.CompletedTask;
        }

        public void DispatchRuntimeStarted(int? processId, int? port, string url) => RuntimeStartedCount++;

        public void DispatchTimeline(IReadOnlyList<StartupStageTiming> timings) => TimelineCount++;

        public void DispatchStartupElapsed(long? previousMs) => StartupElapsedCount++;

        public void StartMetricsMonitor(int processId)
        {
            StartMetricsCount++;
            LastStartedMetricsPid = processId;
        }

        public void StopMetricsMonitor() => StopMetricsCount++;

        public Task PersistReattachTargetAsync(int processId, int port, CancellationToken cancellationToken)
        {
            PersistReattachCount++;
            return Task.CompletedTask;
        }

        public Task ClearReattachRecordAsync(CancellationToken cancellationToken)
        {
            ClearReattachCount++;
            return Task.CompletedTask;
        }

        public Task PersistStartupElapsedAsync(long elapsedMs, CancellationToken cancellationToken)
        {
            PersistStartupElapsedCount++;
            LastPersistedElapsedMs = elapsedMs;
            return Task.CompletedTask;
        }

        public int LastStartedMetricsPid { get; private set; }
    }

    private sealed class FakeConfig : IRuntimeRecoveryConfig
    {
        public bool KeepRuntimeOnClose { get; init; }
        public string Host { get; init; } = "127.0.0.1";
        public int? LastRuntimePid { get; init; }
        public int? LastRuntimePort { get; init; }
        public bool AutoSafeModeOnFailure { get; init; } = true;
        public long? LastStartupElapsedMs { get; init; }
    }

    private sealed class FakeSupervisor : IRuntimeSupervisor
    {
        public bool AdoptRunningCalled { get; private set; }

        public RuntimeSnapshot Current { get; } = new(
            RuntimeLifecycle.Stopped, RuntimeHealth.Unknown, RuntimeStartupStage.None, null, null, null, null);

        public IReadOnlyList<StartupStageTiming> LastStartupStageTimings { get; } = [];

        public event EventHandler<RuntimeSnapshot>? SnapshotChanged;
        public event EventHandler<RuntimeExitedEventArgs>? Exited;

        public Task<RuntimeSnapshot> StartAsync(RuntimeLaunchOptions options, CancellationToken cancellationToken)
            => Task.FromResult(Current);

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<RuntimeSnapshot> RestartAsync(RuntimeLaunchOptions options, CancellationToken cancellationToken)
            => Task.FromResult(Current);

        public RuntimeSnapshot AdoptRunning(int processId, int port, string host)
        {
            AdoptRunningCalled = true;
            return new RuntimeSnapshot(
                RuntimeLifecycle.Running, RuntimeHealth.Healthy, RuntimeStartupStage.Ready, null,
                processId, port, $"http://{host}:{port}/");
        }
    }

    private sealed class FakeProbe : IRuntimeProbe
    {
        public bool ProcessAlive { get; init; }

        public bool HttpAlive { get; init; }

        public Exception? KillException { get; init; }

        public int ProcessAliveChecked { get; private set; }

        public int HttpProbeCount { get; private set; }

        public int? KilledPid { get; private set; }

        public bool IsProcessAlive(int processId)
        {
            ProcessAliveChecked++;
            return ProcessAlive;
        }

        public void Dispose()
        {
        }

        public Task<bool> IsHttpAliveAsync(string host, int port, CancellationToken cancellationToken)
        {
            HttpProbeCount++;
            return Task.FromResult(HttpAlive);
        }

        public void KillProcessTree(int processId)
        {
            if (KillException is not null)
            {
                throw KillException;
            }

            KilledPid = processId;
        }
    }

    private sealed class FakeDelay
    {
        private readonly TaskCompletionSource<bool> _entered = new();
        private readonly TaskCompletionSource<bool> _gate = new();

        /// <summary>true = 进入延迟后立即放行（用于边沿/上限/Reset 测试，不等手动 Complete）。</summary>
        public bool AutoComplete { get; init; }

        public TimeSpan? Requested { get; private set; }

        public bool Cancelled { get; private set; }

        /// <summary>延迟已进入（尚未完成）的信号。</summary>
        public Task Invoked => _entered.Task;

        public void Complete() => _gate.TrySetResult(true);

        public async Task DelayAsync(TimeSpan delaySpan, CancellationToken cancellationToken)
        {
            Requested = delaySpan;
            _entered.TrySetResult(true);
            if (AutoComplete)
            {
                return;
            }

            using var registration = cancellationToken.Register(
                () => _gate.TrySetException(new OperationCanceledException()));
            try
            {
                await _gate.Task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Cancelled = true;
                throw;
            }
        }
    }
}
