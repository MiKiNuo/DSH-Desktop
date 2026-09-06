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
    /// 处理重启 Runtime 意图（ADR-0004）：仅 Running / Failed 可重启，其余状态忽略。
    /// </summary>
    [MviReduce(typeof(RuntimeIntent.RestartRuntime))]
    private MviReduceResult<RuntimeState, RuntimeEffect> HandleRestartRuntime(
        RuntimeState state,
        RuntimeIntent.RestartRuntime intent)
    {
        if (state.Lifecycle is not (RuntimeLifecycle.Running or RuntimeLifecycle.Failed))
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
            new RuntimeEffect.RestartRuntime());
    }

    /// <summary>
    /// 处理编排恢复 Runtime 意图（ADR-0004）：仅 Failed 可恢复，其余状态忽略。
    /// 迁移路径 Failed → Recovering →（禁用成功回流 RecoverPluginsDisabled）→ Starting → Running。
    /// </summary>
    [MviReduce(typeof(RuntimeIntent.RecoverRuntime))]
    private MviReduceResult<RuntimeState, RuntimeEffect> HandleRecoverRuntime(
        RuntimeState state,
        RuntimeIntent.RecoverRuntime intent)
    {
        if (state.Lifecycle is not RuntimeLifecycle.Failed)
        {
            return Unchanged(state);
        }

        return WithEffect(
            state with { Lifecycle = RuntimeLifecycle.Recovering },
            new RuntimeEffect.RecoverRuntime());
    }

    /// <summary>
    /// 处理恢复第一段（禁用全部第三方插件）完成回流意图（ADR-0004）：仅 Recovering 合法，
    /// 迁移到 Starting 并复用启动链路；LastError 保留，待 RuntimeStarted / RuntimeFailed 结算。
    /// </summary>
    [MviReduce(typeof(RuntimeIntent.RecoverPluginsDisabled))]
    private MviReduceResult<RuntimeState, RuntimeEffect> HandleRecoverPluginsDisabled(
        RuntimeState state,
        RuntimeIntent.RecoverPluginsDisabled intent)
    {
        if (state.Lifecycle is not RuntimeLifecycle.Recovering)
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
            },
            new RuntimeEffect.StartRuntime());
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
            ProcessId = intent.ProcessId,
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
    /// Recovering 中收到 = 恢复再失败（ADR-0004）→ Failed 且保留 LastError；
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
                ProcessId = null,
                Port = null,
                Url = null,
                LastError = $"Runtime 意外退出（退出码 {exitCodeText}）。",
            });
        }

        if (state.Lifecycle is RuntimeLifecycle.Recovering)
        {
            string exitCodeText = intent.ExitCode?.ToString() ?? "未知";
            return Unchanged(state with
            {
                Lifecycle = RuntimeLifecycle.Failed,
                Health = RuntimeHealth.Unknown,
                ProcessId = null,
                Port = null,
                Url = null,
                LastError = state.LastError ?? $"Runtime 意外退出（退出码 {exitCodeText}）。",
            });
        }

        return Unchanged(state with
        {
            Lifecycle = RuntimeLifecycle.Stopped,
            Health = RuntimeHealth.Unknown,
            StartupStage = RuntimeStartupStage.None,
            ProcessId = null,
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

    // ===== Phase 8 Issue 04：启动与恢复策略三开关（照 Settings ToggleSafeMode 先例：无载荷翻转 + 乐观更新 + 持久化副作用） =====

    /// <summary>
    /// 处理切换"关闭窗口后保持 DSH Runtime"意图（ADR-0005）。
    /// </summary>
    [MviReduce(typeof(RuntimeIntent.ToggleKeepRuntimeOnClose))]
    private MviReduceResult<RuntimeState, RuntimeEffect> HandleToggleKeepRuntimeOnClose(
        RuntimeState state,
        RuntimeIntent.ToggleKeepRuntimeOnClose intent)
    {
        bool target = !state.KeepRuntimeOnClose;
        return WithEffect(
            state with { KeepRuntimeOnClose = target },
            new RuntimeEffect.SaveKeepRuntimeOnClose(target));
    }

    /// <summary>
    /// 处理切换"异常启动自动进入安全模式"意图（ADR-0004 修订注）。
    /// </summary>
    [MviReduce(typeof(RuntimeIntent.ToggleAutoSafeModeOnFailure))]
    private MviReduceResult<RuntimeState, RuntimeEffect> HandleToggleAutoSafeModeOnFailure(
        RuntimeState state,
        RuntimeIntent.ToggleAutoSafeModeOnFailure intent)
    {
        bool target = !state.AutoSafeModeOnFailure;
        return WithEffect(
            state with { AutoSafeModeOnFailure = target },
            new RuntimeEffect.SaveAutoSafeModeOnFailure(target));
    }

    /// <summary>
    /// 处理切换"启动时检查网络更新"意图（§34 修订注）。
    /// </summary>
    [MviReduce(typeof(RuntimeIntent.ToggleCheckUpdatesOnStartup))]
    private MviReduceResult<RuntimeState, RuntimeEffect> HandleToggleCheckUpdatesOnStartup(
        RuntimeState state,
        RuntimeIntent.ToggleCheckUpdatesOnStartup intent)
    {
        bool target = !state.CheckUpdatesOnStartup;
        return WithEffect(
            state with { CheckUpdatesOnStartup = target },
            new RuntimeEffect.SaveCheckUpdatesOnStartup(target));
    }

    /// <summary>
    /// 处理策略开关持久化值加载回流意图（config 为权威源，覆盖 Initial 默认值）。
    /// </summary>
    [MviReduce(typeof(RuntimeIntent.PoliciesLoaded))]
    private MviReduceResult<RuntimeState, RuntimeEffect> HandlePoliciesLoaded(
        RuntimeState state,
        RuntimeIntent.PoliciesLoaded intent)
    {
        return Unchanged(state with
        {
            KeepRuntimeOnClose = intent.KeepRuntimeOnClose,
            AutoSafeModeOnFailure = intent.AutoSafeModeOnFailure,
            CheckUpdatesOnStartup = intent.CheckUpdatesOnStartup,
        });
    }

    /// <summary>
    /// 处理运行环境信息已探测回流意图。
    /// </summary>
    [MviReduce(typeof(RuntimeIntent.EnvironmentLoaded))]
    private MviReduceResult<RuntimeState, RuntimeEffect> HandleEnvironmentLoaded(
        RuntimeState state,
        RuntimeIntent.EnvironmentLoaded intent)
    {
        return Unchanged(state with { Environment = intent.Environment });
    }

    /// <summary>
    /// 处理 DSH 版本投影变化回流意图（§11.2 兄弟 Store 只读投影）。
    /// </summary>
    [MviReduce(typeof(RuntimeIntent.DshVersionChanged))]
    private MviReduceResult<RuntimeState, RuntimeEffect> HandleDshVersionChanged(
        RuntimeState state,
        RuntimeIntent.DshVersionChanged intent)
    {
        return Unchanged(state with { DshVersion = intent.Version });
    }
}
