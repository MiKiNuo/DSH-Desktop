using DshDesktop.Domain.Updates;
using MiKiNuo.Mvi.Application.MVI.Reducer;
using MiKiNuo.Mvi.Domain.DI;
using MiKiNuo.Mvi.Domain.MVI.Reducer;

namespace DshDesktop.Presentation.Avalonia.Features.Updates;

/// <summary>
/// 表示 Updates 规约器。纯函数，禁止 IO（§9）。
/// </summary>
[MviFeature]
public sealed partial class UpdatesReducer
    : MviReducerBase<UpdatesState, UpdatesIntent, UpdatesEffect>
{
    /// <summary>
    /// 处理检查更新意图。
    /// </summary>
    [MviReduce(typeof(UpdatesIntent.CheckUpdates))]
    private MviReduceResult<UpdatesState, UpdatesEffect> HandleCheckUpdates(
        UpdatesState state,
        UpdatesIntent.CheckUpdates intent)
    {
        return WithEffect(
            state with { Status = UpdateStatus.Checking, LastError = null },
            new UpdatesEffect.CheckUpdates());
    }

    /// <summary>
    /// 处理检查更新完成回流意图。
    /// </summary>
    [MviReduce(typeof(UpdatesIntent.CheckUpdatesCompleted))]
    private MviReduceResult<UpdatesState, UpdatesEffect> HandleCheckUpdatesCompleted(
        UpdatesState state,
        UpdatesIntent.CheckUpdatesCompleted intent)
    {
        CheckUpdatesResponse result = intent.Result;
        bool available = result.PluginUpdates.Count > 0
            || (result.LatestDshVersion is not null
                && result.CurrentDshVersion is not null
                && result.LatestDshVersion != result.CurrentDshVersion);

        return Unchanged(state with
        {
            Status = available ? UpdateStatus.Available : UpdateStatus.Idle,
            LatestDshVersion = result.LatestDshVersion,
            CurrentDshVersion = result.CurrentDshVersion,
            Runtimes = result.Runtimes,
            PluginUpdates = result.PluginUpdates,
            LastError = null,
        });
    }

    /// <summary>
    /// 处理安装 DSH Runtime 意图。
    /// </summary>
    [MviReduce(typeof(UpdatesIntent.InstallDshRuntime))]
    private MviReduceResult<UpdatesState, UpdatesEffect> HandleInstallDshRuntime(
        UpdatesState state,
        UpdatesIntent.InstallDshRuntime intent)
    {
        return WithEffect(
            state with
            {
                Status = UpdateStatus.Installing,
                PendingOperation = $"安装 DSH Runtime {intent.Version}…",
                LastError = null,
            },
            new UpdatesEffect.InstallDshRuntime(intent.Version));
    }

    /// <summary>
    /// 处理激活 Runtime 意图。
    /// </summary>
    [MviReduce(typeof(UpdatesIntent.ActivateDshRuntime))]
    private MviReduceResult<UpdatesState, UpdatesEffect> HandleActivateDshRuntime(
        UpdatesState state,
        UpdatesIntent.ActivateDshRuntime intent)
    {
        string label = intent.Version.Length == 0 ? "借用安装" : intent.Version;
        return WithEffect(
            state with
            {
                PendingOperation = $"切换到 {label} 并重启 Runtime…",
                LastError = null,
            },
            new UpdatesEffect.ActivateDshRuntime(intent.Version));
    }

    /// <summary>
    /// 处理更新插件意图（走 §19 安装事务）。
    /// </summary>
    [MviReduce(typeof(UpdatesIntent.UpdatePlugin))]
    private MviReduceResult<UpdatesState, UpdatesEffect> HandleUpdatePlugin(
        UpdatesState state,
        UpdatesIntent.UpdatePlugin intent)
    {
        return WithEffect(
            state with
            {
                PendingOperation = $"更新 {intent.Name}…",
                LastError = null,
            },
            new UpdatesEffect.UpdatePlugin(intent.Name));
    }

    /// <summary>
    /// 处理 Runtime 列表变化回流意图。
    /// </summary>
    [MviReduce(typeof(UpdatesIntent.RuntimeListChanged))]
    private MviReduceResult<UpdatesState, UpdatesEffect> HandleRuntimeListChanged(
        UpdatesState state,
        UpdatesIntent.RuntimeListChanged intent)
    {
        DshRuntimeInfo? active = intent.Runtimes.FirstOrDefault(r => r.IsActive);
        return Unchanged(state with
        {
            Runtimes = intent.Runtimes,
            CurrentDshVersion = active?.Version ?? state.CurrentDshVersion,
            Status = UpdateStatus.Idle,
            PendingOperation = null,
        });
    }

    /// <summary>
    /// 处理更新操作失败回流意图。
    /// </summary>
    [MviReduce(typeof(UpdatesIntent.UpdatesOperationFailed))]
    private MviReduceResult<UpdatesState, UpdatesEffect> HandleUpdatesOperationFailed(
        UpdatesState state,
        UpdatesIntent.UpdatesOperationFailed intent)
    {
        return Unchanged(state with
        {
            Status = UpdateStatus.Failed,
            PendingOperation = null,
            LastError = intent.Error,
        });
    }
}
