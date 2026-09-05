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
    public async Task RestartRuntime_FromRunning_TransitionsToStartingWithEffect()
    {
        var result = _reducer.Reduce(RunningState(), new RuntimeIntent.RestartRuntime());

        await Assert.That(result.State.Lifecycle).IsEqualTo(RuntimeLifecycle.Starting);
        await Assert.That(result.State.StartupStage).IsEqualTo(RuntimeStartupStage.Validating);
        await Assert.That(result.State.LastError).IsNull();
        await Assert.That(result.Effects.Count).IsEqualTo(1);
        await Assert.That(result.Effects[0] is RuntimeEffect.RestartRuntime).IsTrue();
    }

    [Test]
    public async Task RestartRuntime_FromFailed_AllowsRestart()
    {
        RuntimeState failed = RuntimeState.Initial with { Lifecycle = RuntimeLifecycle.Failed };

        var result = _reducer.Reduce(failed, new RuntimeIntent.RestartRuntime());

        await Assert.That(result.State.Lifecycle).IsEqualTo(RuntimeLifecycle.Starting);
        await Assert.That(result.Effects.Count).IsEqualTo(1);
        await Assert.That(result.Effects[0] is RuntimeEffect.RestartRuntime).IsTrue();
    }

    [Test]
    [Arguments(RuntimeLifecycle.Stopped)]
    [Arguments(RuntimeLifecycle.Starting)]
    [Arguments(RuntimeLifecycle.Stopping)]
    [Arguments(RuntimeLifecycle.Recovering)]
    public async Task RestartRuntime_FromOtherLifecycle_IsIgnored(RuntimeLifecycle lifecycle)
    {
        RuntimeState state = RuntimeState.Initial with { Lifecycle = lifecycle };

        var result = _reducer.Reduce(state, new RuntimeIntent.RestartRuntime());

        await Assert.That(result.State.Lifecycle).IsEqualTo(lifecycle);
        await Assert.That(result.Effects.Count).IsEqualTo(0);
    }

    [Test]
    public async Task RecoverRuntime_FromFailed_TransitionsToRecoveringWithEffect()
    {
        RuntimeState failed = RuntimeState.Initial with
        {
            Lifecycle = RuntimeLifecycle.Failed,
            LastError = "插件导致启动失败",
        };

        var result = _reducer.Reduce(failed, new RuntimeIntent.RecoverRuntime());

        await Assert.That(result.State.Lifecycle).IsEqualTo(RuntimeLifecycle.Recovering);
        await Assert.That(result.State.LastError).IsEqualTo("插件导致启动失败");
        await Assert.That(result.Effects.Count).IsEqualTo(1);
        await Assert.That(result.Effects[0] is RuntimeEffect.RecoverRuntime).IsTrue();
    }

    [Test]
    [Arguments(RuntimeLifecycle.Stopped)]
    [Arguments(RuntimeLifecycle.Starting)]
    [Arguments(RuntimeLifecycle.Running)]
    [Arguments(RuntimeLifecycle.Stopping)]
    [Arguments(RuntimeLifecycle.Recovering)]
    public async Task RecoverRuntime_FromNonFailed_IsIgnored(RuntimeLifecycle lifecycle)
    {
        RuntimeState state = RuntimeState.Initial with { Lifecycle = lifecycle };

        var result = _reducer.Reduce(state, new RuntimeIntent.RecoverRuntime());

        await Assert.That(result.State.Lifecycle).IsEqualTo(lifecycle);
        await Assert.That(result.Effects.Count).IsEqualTo(0);
    }

    [Test]
    public async Task RecoverRuntime_SuccessChain_FailedToRecoveringToStartingToRunning()
    {
        // ADR-0004 两段状态机：Failed → Recovering（禁用插件 Effect）
        // →（禁用成功回流 RecoverPluginsDisabled）→ Starting（复用启动链路）→ Running。
        RuntimeState failed = RuntimeState.Initial with
        {
            Lifecycle = RuntimeLifecycle.Failed,
            LastError = "插件导致启动失败",
        };
        var recovering = _reducer.Reduce(failed, new RuntimeIntent.RecoverRuntime());

        var starting = _reducer.Reduce(recovering.State, new RuntimeIntent.RecoverPluginsDisabled());

        await Assert.That(starting.State.Lifecycle).IsEqualTo(RuntimeLifecycle.Starting);
        await Assert.That(starting.State.StartupStage).IsEqualTo(RuntimeStartupStage.Validating);
        await Assert.That(starting.Effects.Count).IsEqualTo(1);
        await Assert.That(starting.Effects[0] is RuntimeEffect.StartRuntime).IsTrue();

        var result = _reducer.Reduce(
            starting.State,
            new RuntimeIntent.RuntimeStarted(1234, 5678, "http://127.0.0.1:5678/?token=x"));

        await Assert.That(result.State.Lifecycle).IsEqualTo(RuntimeLifecycle.Running);
        await Assert.That(result.State.StartupStage).IsEqualTo(RuntimeStartupStage.Ready);
    }

    [Test]
    [Arguments(RuntimeLifecycle.Stopped)]
    [Arguments(RuntimeLifecycle.Starting)]
    [Arguments(RuntimeLifecycle.Running)]
    [Arguments(RuntimeLifecycle.Stopping)]
    [Arguments(RuntimeLifecycle.Failed)]
    public async Task RecoverPluginsDisabled_FromNonRecovering_IsIgnored(RuntimeLifecycle lifecycle)
    {
        // 恢复第二段回流仅 Recovering 合法，其余状态忽略（防乱序回流污染状态机）。
        RuntimeState state = RuntimeState.Initial with { Lifecycle = lifecycle };

        var result = _reducer.Reduce(state, new RuntimeIntent.RecoverPluginsDisabled());

        await Assert.That(result.State.Lifecycle).IsEqualTo(lifecycle);
        await Assert.That(result.Effects.Count).IsEqualTo(0);
    }

    [Test]
    public async Task RecoverRuntime_FailureReflow_BackToFailedKeepingLastError()
    {
        // ADR-0004：Recover 再失败回 Failed 且保留 LastError（用户必须能看到恢复失败原因）。
        RuntimeState failed = RuntimeState.Initial with { Lifecycle = RuntimeLifecycle.Failed };
        var recovering = _reducer.Reduce(failed, new RuntimeIntent.RecoverRuntime());

        var result = _reducer.Reduce(
            recovering.State,
            new RuntimeIntent.RuntimeFailed("禁用插件后启动仍失败"));

        await Assert.That(result.State.Lifecycle).IsEqualTo(RuntimeLifecycle.Failed);
        await Assert.That(result.State.LastError).IsEqualTo("禁用插件后启动仍失败");
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
    public async Task RuntimeStarted_NullProcessIdAndPort_PreservesNullInsteadOfSentinel()
    {
        // Phase 7：RuntimeStarted 与 RuntimeSnapshot 类型对齐（int?），空值透传，不得落成 0 哨兵。
        RuntimeState starting = RuntimeState.Initial with { Lifecycle = RuntimeLifecycle.Starting };

        var result = _reducer.Reduce(starting, new RuntimeIntent.RuntimeStarted(null, null, "http://127.0.0.1:5678/?token=x"));

        await Assert.That(result.State.Lifecycle).IsEqualTo(RuntimeLifecycle.Running);
        await Assert.That(result.State.Port).IsNull();
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
    public async Task RuntimeExited_WhileRecovering_TreatedAsCrashUsingExitInfo()
    {
        // ADR-0004：Recovering 中进程退出 = 恢复再失败 → Failed；无既有错误时用退出信息。
        RuntimeState recovering = RuntimeState.Initial with { Lifecycle = RuntimeLifecycle.Recovering };

        var result = _reducer.Reduce(recovering, new RuntimeIntent.RuntimeExited(1));

        await Assert.That(result.State.Lifecycle).IsEqualTo(RuntimeLifecycle.Failed);
        await Assert.That(result.State.LastError).IsEqualTo("Runtime 意外退出（退出码 1）。");
    }

    [Test]
    public async Task RuntimeExited_WhileRecovering_KeepsExistingLastError()
    {
        // ADR-0004：Recover 再失败回 Failed 且保留 LastError——既有错误不被退出信息覆盖。
        RuntimeState recovering = RuntimeState.Initial with
        {
            Lifecycle = RuntimeLifecycle.Recovering,
            LastError = "插件导致启动失败",
        };

        var result = _reducer.Reduce(recovering, new RuntimeIntent.RuntimeExited(1));

        await Assert.That(result.State.Lifecycle).IsEqualTo(RuntimeLifecycle.Failed);
        await Assert.That(result.State.LastError).IsEqualTo("插件导致启动失败");
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
