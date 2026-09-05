using DshDesktop.Domain.Runtime;
using DshDesktop.Presentation.Avalonia.Features.Runtime;

namespace DshDesktop.Tests;

/// <summary>
/// Runtime 规约器测试（§43.1：Given State / When Intent / Then NewState + Effect）。
/// 重点覆盖 CONTEXT.md 崩溃语义：Running/Starting 中收到退出即崩溃，Stopping 中才是用户请求的停止。
/// </summary>
public sealed class RuntimeReducerTests
{
    private readonly RuntimeReducer _reducer = new();

    [Test]
    public async Task StartRuntime_FromStopped_TransitionsToStartingWithEffect()
    {
        var result = _reducer.Reduce(RuntimeState.Initial, new RuntimeIntent.StartRuntime());

        await Assert.That(result.State.Lifecycle).IsEqualTo(RuntimeLifecycle.Starting);
        await Assert.That(result.State.StartupStage).IsEqualTo(RuntimeStartupStage.Validating);
        await Assert.That(result.Effects.Count).IsEqualTo(1);
        await Assert.That(result.Effects[0] is RuntimeEffect.StartRuntime).IsTrue();
    }

    [Test]
    public async Task StartRuntime_FromFailed_AllowsRestart()
    {
        RuntimeState failed = RuntimeState.Initial with { Lifecycle = RuntimeLifecycle.Failed };

        var result = _reducer.Reduce(failed, new RuntimeIntent.StartRuntime());

        await Assert.That(result.State.Lifecycle).IsEqualTo(RuntimeLifecycle.Starting);
        await Assert.That(result.Effects.Count).IsEqualTo(1);
    }

    [Test]
    public async Task StartRuntime_FromRunning_IsIgnored()
    {
        RuntimeState running = RunningState();

        var result = _reducer.Reduce(running, new RuntimeIntent.StartRuntime());

        await Assert.That(result.State.Lifecycle).IsEqualTo(RuntimeLifecycle.Running);
        await Assert.That(result.Effects.Count).IsEqualTo(0);
    }

    [Test]
    public async Task StopRuntime_FromRunning_TransitionsToStoppingWithEffect()
    {
        var result = _reducer.Reduce(RunningState(), new RuntimeIntent.StopRuntime());

        await Assert.That(result.State.Lifecycle).IsEqualTo(RuntimeLifecycle.Stopping);
        await Assert.That(result.Effects.Count).IsEqualTo(1);
        await Assert.That(result.Effects[0] is RuntimeEffect.StopRuntime).IsTrue();
    }

    [Test]
    public async Task StopRuntime_FromStopped_IsIgnored()
    {
        var result = _reducer.Reduce(RuntimeState.Initial, new RuntimeIntent.StopRuntime());

        await Assert.That(result.State.Lifecycle).IsEqualTo(RuntimeLifecycle.Stopped);
        await Assert.That(result.Effects.Count).IsEqualTo(0);
    }

    [Test]
    public async Task RuntimeStarted_TransitionsToRunningWithPortAndUrl()
    {
        RuntimeState starting = RuntimeState.Initial with { Lifecycle = RuntimeLifecycle.Starting };

        var result = _reducer.Reduce(starting, new RuntimeIntent.RuntimeStarted(1234, 5678, "http://127.0.0.1:5678/?token=x"));

        await Assert.That(result.State.Lifecycle).IsEqualTo(RuntimeLifecycle.Running);
        await Assert.That(result.State.StartupStage).IsEqualTo(RuntimeStartupStage.Ready);
        await Assert.That(result.State.Port).IsEqualTo(5678);
        await Assert.That(result.State.Url).IsEqualTo("http://127.0.0.1:5678/?token=x");
    }

    [Test]
    public async Task RuntimeFailed_TransitionsToFailedWithError()
    {
        RuntimeState starting = RuntimeState.Initial with { Lifecycle = RuntimeLifecycle.Starting };

        var result = _reducer.Reduce(starting, new RuntimeIntent.RuntimeFailed("启动超时"));

        await Assert.That(result.State.Lifecycle).IsEqualTo(RuntimeLifecycle.Failed);
        await Assert.That(result.State.LastError).IsEqualTo("启动超时");
    }

    [Test]
    public async Task RuntimeExited_WhileRunning_TreatedAsCrash()
    {
        var result = _reducer.Reduce(RunningState(), new RuntimeIntent.RuntimeExited(1));

        await Assert.That(result.State.Lifecycle).IsEqualTo(RuntimeLifecycle.Failed);
        await Assert.That(result.State.Health).IsEqualTo(RuntimeHealth.Unknown);
        await Assert.That(result.State.Port).IsNull();
        await Assert.That(result.State.Url).IsNull();
        await Assert.That(result.State.LastError).IsNotNull();
    }

    [Test]
    public async Task RuntimeExited_WhileStarting_TreatedAsCrash()
    {
        RuntimeState starting = RuntimeState.Initial with { Lifecycle = RuntimeLifecycle.Starting };

        var result = _reducer.Reduce(starting, new RuntimeIntent.RuntimeExited(null));

        await Assert.That(result.State.Lifecycle).IsEqualTo(RuntimeLifecycle.Failed);
    }

    [Test]
    public async Task RuntimeExited_WhileStopping_TreatedAsUserRequestedStop()
    {
        RuntimeState stopping = RunningState() with { Lifecycle = RuntimeLifecycle.Stopping };

        var result = _reducer.Reduce(stopping, new RuntimeIntent.RuntimeExited(0));

        await Assert.That(result.State.Lifecycle).IsEqualTo(RuntimeLifecycle.Stopped);
        await Assert.That(result.State.StartupStage).IsEqualTo(RuntimeStartupStage.None);
    }

    [Test]
    public async Task RuntimeExited_WhileStopped_StaysStopped()
    {
        var result = _reducer.Reduce(RuntimeState.Initial, new RuntimeIntent.RuntimeExited(0));

        await Assert.That(result.State.Lifecycle).IsEqualTo(RuntimeLifecycle.Stopped);
    }

    [Test]
    public async Task RuntimeSnapshotReceived_DoesNotOverrideLifecycle()
    {
        // 防回归：退出快照（Supervisor 事实层 Stopped）不得覆盖崩溃语义（Failed）。
        RuntimeState failed = RuntimeState.Initial with { Lifecycle = RuntimeLifecycle.Failed };
        var snapshot = new RuntimeSnapshot(
            RuntimeLifecycle.Stopped, RuntimeHealth.Healthy, RuntimeStartupStage.Ready,
            TimeSpan.FromSeconds(2), null, null, null);

        var result = _reducer.Reduce(failed, new RuntimeIntent.RuntimeSnapshotReceived(snapshot));

        await Assert.That(result.State.Lifecycle).IsEqualTo(RuntimeLifecycle.Failed);
        await Assert.That(result.State.Health).IsEqualTo(RuntimeHealth.Healthy);
        await Assert.That(result.State.StartupElapsed?.TotalSeconds).IsEqualTo(2);
    }

    [Test]
    public async Task EnterSafeMode_DeclaresPersistEffectOnly()
    {
        var result = _reducer.Reduce(RuntimeState.Initial, new RuntimeIntent.EnterSafeMode());

        await Assert.That(result.State.SafeMode).IsFalse();
        await Assert.That(result.Effects.Count).IsEqualTo(1);
        await Assert.That(result.Effects[0] is RuntimeEffect.SetSafeMode { Enabled: true }).IsTrue();
    }

    [Test]
    public async Task ExitSafeMode_DeclaresPersistEffectOnly()
    {
        RuntimeState safe = RuntimeState.Initial with { SafeMode = true };

        var result = _reducer.Reduce(safe, new RuntimeIntent.ExitSafeMode());

        await Assert.That(result.State.SafeMode).IsTrue();
        await Assert.That(result.Effects[0] is RuntimeEffect.SetSafeMode { Enabled: false }).IsTrue();
    }

    [Test]
    public async Task SafeModeChanged_UpdatesSafeMode()
    {
        var result = _reducer.Reduce(RuntimeState.Initial, new RuntimeIntent.SafeModeChanged(true));

        await Assert.That(result.State.SafeMode).IsTrue();
        await Assert.That(result.Effects.Count).IsEqualTo(0);
    }

    [Test]
    public async Task RuntimeOperationFailed_OnlyUpdatesLastError()
    {
        RuntimeState running = RunningState();

        var result = _reducer.Reduce(running, new RuntimeIntent.RuntimeOperationFailed("安全模式写入失败"));

        await Assert.That(result.State.Lifecycle).IsEqualTo(RuntimeLifecycle.Running);
        await Assert.That(result.State.LastError).IsEqualTo("安全模式写入失败");
    }

    private static RuntimeState RunningState()
    {
        return RuntimeState.Initial with
        {
            Lifecycle = RuntimeLifecycle.Running,
            Health = RuntimeHealth.Healthy,
            StartupStage = RuntimeStartupStage.Ready,
            Port = 5678,
            Url = "http://127.0.0.1:5678/?token=x",
        };
    }
}
