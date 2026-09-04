using DshDesktop.Domain.Runtime;
using MiKiNuo.Mvi.Application.MVI.Reducer;
using MiKiNuo.Mvi.Domain.DI;
using MiKiNuo.Mvi.Domain.MVI.Reducer;

namespace DshDesktop.Presentation.Avalonia.Features.Runtime;

/// <summary>
/// 表示 Runtime 规约器。纯函数：只允许修改 State 或声明 Effect，禁止 IO（§9）。
/// </summary>
[MviFeature]
public sealed partial class RuntimeReducer
    : MviReducerBase<RuntimeState, RuntimeIntent, RuntimeEffect>
{
    /// <summary>
    /// 处理启动 Runtime 意图：仅 Stopped / Failed 可启动。
    /// </summary>
    [MviReduce(typeof(RuntimeIntent.StartRuntime))]
    private MviReduceResult<RuntimeState, RuntimeEffect> HandleStartRuntime(
        RuntimeState state,
        RuntimeIntent.StartRuntime intent)
    {
        if (state.Lifecycle is not (RuntimeLifecycle.Stopped or RuntimeLifecycle.Failed))
        {
            return Unchanged(state);
        }

        return WithEffect(
            state with
            {
                Lifecycle = RuntimeLifecycle.Starting,
                Health = RuntimeHealth.Unknown,
                StartupStage = RuntimeStartupStage.Validating,
                StartupElapsed = null,
                LastError = null,
            },
            new RuntimeEffect.StartRuntime());
    }

    /// <summary>
    /// 处理停止 Runtime 意图：仅 Running 可停止。
    /// </summary>
    [MviReduce(typeof(RuntimeIntent.StopRuntime))]
    private MviReduceResult<RuntimeState, RuntimeEffect> HandleStopRuntime(
        RuntimeState state,
        RuntimeIntent.StopRuntime intent)
    {
        if (state.Lifecycle is not RuntimeLifecycle.Running)
        {
            return Unchanged(state);
        }

        return WithEffect(
            state with { Lifecycle = RuntimeLifecycle.Stopping },
            new RuntimeEffect.StopRuntime());
    }

    /// <summary>
    /// 处理 Runtime 已就绪回流意图。
    /// </summary>
    [MviReduce(typeof(RuntimeIntent.RuntimeStarted))]
    private MviReduceResult<RuntimeState, RuntimeEffect> HandleRuntimeStarted(
        RuntimeState state,
        RuntimeIntent.RuntimeStarted intent)
    {
        return Unchanged(state with
        {
            Lifecycle = RuntimeLifecycle.Running,
            StartupStage = RuntimeStartupStage.Ready,
            Port = intent.Port,
            Url = intent.Url,
            LastError = null,
        });
    }

    /// <summary>
    /// 处理 Runtime 失败回流意图。
    /// </summary>
    [MviReduce(typeof(RuntimeIntent.RuntimeFailed))]
    private MviReduceResult<RuntimeState, RuntimeEffect> HandleRuntimeFailed(
        RuntimeState state,
        RuntimeIntent.RuntimeFailed intent)
    {
        return Unchanged(state with
        {
            Lifecycle = RuntimeLifecycle.Failed,
            LastError = intent.Error,
        });
    }

    /// <summary>
    /// 处理 Runtime 退出回流意图（Q7 崩溃语义）：
    /// Stopping 中收到 = 用户请求的停止 → Stopped；
    /// Running / Starting 中收到 = 崩溃 → Failed；
    /// 其余 = Stopped。
    /// </summary>
    [MviReduce(typeof(RuntimeIntent.RuntimeExited))]
    private MviReduceResult<RuntimeState, RuntimeEffect> HandleRuntimeExited(
        RuntimeState state,
        RuntimeIntent.RuntimeExited intent)
    {
        if (state.Lifecycle is RuntimeLifecycle.Running or RuntimeLifecycle.Starting)
        {
            string exitCodeText = intent.ExitCode?.ToString() ?? "未知";
            return Unchanged(state with
            {
                Lifecycle = RuntimeLifecycle.Failed,
                Health = RuntimeHealth.Unknown,
                Port = null,
                Url = null,
                LastError = $"Runtime 意外退出（退出码 {exitCodeText}）。",
            });
        }

        return Unchanged(state with
        {
            Lifecycle = RuntimeLifecycle.Stopped,
            Health = RuntimeHealth.Unknown,
            StartupStage = RuntimeStartupStage.None,
            Port = null,
            Url = null,
        });
    }

    /// <summary>
    /// 处理 Supervisor 快照推送：只应用阶段 / 健康 / 耗时。
    /// 生命周期迁移由专用 Intent（Started / Failed / Exited）表达，
    /// 防止退出快照的 Stopped 覆盖崩溃语义（Q7）。
    /// </summary>
    [MviReduce(typeof(RuntimeIntent.RuntimeSnapshotReceived))]
    private MviReduceResult<RuntimeState, RuntimeEffect> HandleRuntimeSnapshotReceived(
        RuntimeState state,
        RuntimeIntent.RuntimeSnapshotReceived intent)
    {
        return Unchanged(state with
        {
            StartupStage = intent.Snapshot.StartupStage,
            Health = intent.Snapshot.Health,
            StartupElapsed = intent.Snapshot.StartupElapsed ?? state.StartupElapsed,
        });
    }

    /// <summary>
    /// 处理进入安全模式意图：只声明持久化副作用，状态更新等 SafeModeChanged 回流。
    /// </summary>
    [MviReduce(typeof(RuntimeIntent.EnterSafeMode))]
    private MviReduceResult<RuntimeState, RuntimeEffect> HandleEnterSafeMode(
        RuntimeState state,
        RuntimeIntent.EnterSafeMode intent)
    {
        return WithEffect(state, new RuntimeEffect.SetSafeMode(true));
    }

    /// <summary>
    /// 处理退出安全模式意图。
    /// </summary>
    [MviReduce(typeof(RuntimeIntent.ExitSafeMode))]
    private MviReduceResult<RuntimeState, RuntimeEffect> HandleExitSafeMode(
        RuntimeState state,
        RuntimeIntent.ExitSafeMode intent)
    {
        return WithEffect(state, new RuntimeEffect.SetSafeMode(false));
    }

    /// <summary>
    /// 处理安全模式状态已持久化回流意图。
    /// </summary>
    [MviReduce(typeof(RuntimeIntent.SafeModeChanged))]
    private MviReduceResult<RuntimeState, RuntimeEffect> HandleSafeModeChanged(
        RuntimeState state,
        RuntimeIntent.SafeModeChanged intent)
    {
        return Unchanged(state with { SafeMode = intent.Enabled });
    }

    /// <summary>
    /// 处理不改变生命周期的操作失败回流意图。
    /// </summary>
    [MviReduce(typeof(RuntimeIntent.RuntimeOperationFailed))]
    private MviReduceResult<RuntimeState, RuntimeEffect> HandleRuntimeOperationFailed(
        RuntimeState state,
        RuntimeIntent.RuntimeOperationFailed intent)
    {
        return Unchanged(state with { LastError = intent.Error });
    }
}
