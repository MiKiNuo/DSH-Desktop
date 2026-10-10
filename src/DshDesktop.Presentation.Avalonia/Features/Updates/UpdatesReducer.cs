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
    /// <remarks>
    /// 只发起检查与刷新，<b>不碰 PendingOperation</b>：本意图有两个来源（用户按钮 / 启动期后台静默检查，
    /// App.axaml.cs → BackgroundCheckUpdatesAsync），而检查是 N 次 npm 查询 + GitHub 的慢网络调用，
    /// 与进行中的 Desktop 下载 / Runtime 安装的区间必然重叠。
    /// </remarks>
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
    /// <remarks>
    /// 只发起检查与刷新，<b>不碰 PendingOperation</b>：本意图有两个来源（用户按钮 / 启动期后台静默检查），
    /// 与进行中的 Desktop 下载 / Runtime 安装的区间必然重叠。
    /// </remarks>
    [MviReduce(typeof(UpdatesIntent.CheckUpdatesCompleted))]
    private MviReduceResult<UpdatesState, UpdatesEffect> HandleCheckUpdatesCompleted(
        UpdatesState state,
        UpdatesIntent.CheckUpdatesCompleted intent)
    {
        CheckUpdatesResponse result = intent.Result;
        UpdatesState refreshed = state with
        {
            LatestDshVersion = result.LatestDshVersion,
            CurrentDshVersion = result.CurrentDshVersion,
            Runtimes = result.Runtimes,
            LatestDesktopVersion = result.LatestDesktopVersion,
            LastError = null,
        };
        return Unchanged(refreshed with
        {
            Status = refreshed.AvailableCount > 0 ? UpdateStatus.Available : UpdateStatus.Idle,
        });
    }

    /// <summary>
    /// 处理下载并应用 Desktop 更新意图（ADR-0003：无更新时忽略）。
    /// </summary>
    [MviReduce(typeof(UpdatesIntent.DownloadAndApplyDesktopUpdate))]
    private MviReduceResult<UpdatesState, UpdatesEffect> HandleDownloadAndApplyDesktopUpdate(
        UpdatesState state,
        UpdatesIntent.DownloadAndApplyDesktopUpdate intent)
    {
        if (state.LatestDesktopVersion is null)
        {
            return Unchanged(state);
        }

        return WithEffect(
            state with
            {
                Operation = new UpdatesOperation(UpdatesOperationKind.DesktopDownload, state.LatestDesktopVersion),
                LastError = null,
            },
            new UpdatesEffect.DownloadAndApplyDesktopUpdate(state.LatestDesktopVersion));
    }

    /// <summary>
    /// 处理 Desktop 更新下载进度回流意图。
    /// </summary>
    [MviReduce(typeof(UpdatesIntent.DesktopDownloadProgress))]
    private MviReduceResult<UpdatesState, UpdatesEffect> HandleDesktopDownloadProgress(
        UpdatesState state,
        UpdatesIntent.DesktopDownloadProgress intent)
    {
        // 迟到的进度只能更新仍在运行的 Desktop 下载，不能复活终态或污染 Runtime 操作。
        if (state.Operation is not
            { Kind: UpdatesOperationKind.DesktopDownload, Phase: UpdatesOperationPhase.Running } operation)
        {
            return Unchanged(state);
        }

        return Unchanged(state with
        {
            Operation = operation with { Percent = intent.Percent },
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
                Operation = new UpdatesOperation(UpdatesOperationKind.RuntimeInstall, intent.Version),
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
        return WithEffect(
            state with
            {
                Operation = new UpdatesOperation(UpdatesOperationKind.RuntimeActivation, intent.Version),
                LastError = null,
            },
            new UpdatesEffect.ActivateDshRuntime(intent.Version));
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
        UpdatesOperation? operation = state.Operation;
        UpdateStatus status = state.Status;
        if (operation is
            { Phase: UpdatesOperationPhase.Running, Kind: UpdatesOperationKind.RuntimeInstall or UpdatesOperationKind.RuntimeActivation }
            && operation.Kind == intent.CompletedKind
            && string.Equals(operation.Version, intent.CompletedVersion, StringComparison.Ordinal))
        {
            operation = operation with { Phase = UpdatesOperationPhase.Completed };
            status = UpdateStatus.Idle;
        }
        return Unchanged(state with
        {
            Runtimes = intent.Runtimes,
            CurrentDshVersion = active?.Version ?? state.CurrentDshVersion,
            Status = status,
            Operation = operation,
        });
    }

    /// <summary>后台检查失败不清除下载或安装的在飞事实。</summary>
    [MviReduce(typeof(UpdatesIntent.CheckUpdatesFailed))]
    private MviReduceResult<UpdatesState, UpdatesEffect> HandleCheckUpdatesFailed(
        UpdatesState state, UpdatesIntent.CheckUpdatesFailed intent)
    {
        return Unchanged(state with { Status = UpdateStatus.Failed, LastError = intent.Error });
    }

    /// <summary>
    /// 处理更新操作失败回流意图。
    /// </summary>
    [MviReduce(typeof(UpdatesIntent.UpdatesOperationFailed))]
    private MviReduceResult<UpdatesState, UpdatesEffect> HandleUpdatesOperationFailed(
        UpdatesState state,
        UpdatesIntent.UpdatesOperationFailed intent)
    {
        if (state.Operation is { Phase: UpdatesOperationPhase.Running } operation
            && operation.Kind == intent.OperationKind
            && string.Equals(operation.Version, intent.OperationVersion, StringComparison.Ordinal))
        {
            return Unchanged(state with
            {
                Status = UpdateStatus.Failed,
                Operation = operation with { Phase = UpdatesOperationPhase.Failed },
                LastError = intent.Error,
            });
        }

        return Unchanged(state with { LastError = intent.Error });
    }
}
