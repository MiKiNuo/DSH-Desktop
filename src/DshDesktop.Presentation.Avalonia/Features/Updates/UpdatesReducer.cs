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
    /// 与进行中的 Desktop 下载 / Runtime 安装 / 插件更新的区间必然重叠。它过去兼任「插件更新终态」，
    /// 使在飞检查的完成回流与终态清空互相踩踏（2026-09-21 v0.1.8 实机：遮罩永久卡死 / 遮罩被提前释放）；
    /// 终态清空已收口到 <see cref="HandlePluginOperationFinished"/>。
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
    /// 与进行中的 Desktop 下载 / Runtime 安装 / 插件更新的区间必然重叠；它过去兼任「插件更新终态」，
    /// 使在飞检查的完成回流与终态清空互相踩踏（2026-09-21 v0.1.8 实机：遮罩永久卡死 / 遮罩被提前释放）；
    /// 终态清空已收口到 <see cref="HandlePluginOperationFinished"/>（候选 3 后该 handler 也不再触碰 PendingOperation）。
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
            PluginUpdates = result.PluginUpdates,
            LatestDesktopVersion = result.LatestDesktopVersion,
            LastError = null,
        };
        return Unchanged(refreshed with
        {
            Status = refreshed.AvailableCount > 0 ? UpdateStatus.Available : UpdateStatus.Idle,
        });
    }

    /// <summary>
    /// 处理插件事务终态意图（组合根订阅 <c>IPluginOrchestrator.OperationChanged</c> 后在 Completed 阶段发布）：
    /// 按名摘除该插件的可更新行（乐观刷新，不等慢网络检查回来，§23），并回流一次检查以对账。
    /// </summary>
    /// <remarks>
    /// 候选 3（消除"在飞操作"双轨）：插件更新的在飞与文案改由 <c>PluginsState.Operation</c> 单一承载，
    /// 本 handler <b>完全不触碰 <see cref="UpdatesState.PendingOperation"/></b>——它只覆盖 UpdatesStore 自有操作
    /// （Desktop 下载 / Runtime 安装 / 激活）。任何插件事务（更新 / 卸载 / 启停）的 Completed 终态都走这里，
    /// 按名摘行 + 回流检查对卸载/启停终态同样安全：Desktop 下载进行中抵达的插件终态仅保留其自有待办，
    /// 不会被误清（§22）。
    /// </remarks>
    [MviReduce(typeof(UpdatesIntent.PluginOperationFinished))]
    private MviReduceResult<UpdatesState, UpdatesEffect> HandlePluginOperationFinished(
        UpdatesState state,
        UpdatesIntent.PluginOperationFinished intent)
    {
        return WithEffect(
            state with
            {
                Status = UpdateStatus.Checking,
                PluginUpdates = RemoveByName(state.PluginUpdates, intent.PluginName),
                LastError = null,
            },
            new UpdatesEffect.CheckUpdates());
    }

    /// <summary>
    /// 按包名摘除可更新行；无匹配（或包名未知）时原样返回同一引用，避免无谓的状态重发。
    /// </summary>
    private static IReadOnlyList<PluginUpdateInfo> RemoveByName(
        IReadOnlyList<PluginUpdateInfo> updates,
        string? name)
    {
        if (name is null || !updates.Any(u => string.Equals(u.Name, name, StringComparison.Ordinal)))
        {
            return updates;
        }

        return updates.Where(u => !string.Equals(u.Name, name, StringComparison.Ordinal)).ToArray();
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
    /// 处理更新插件意图（走 §19 安装事务）。
    /// </summary>
    [MviReduce(typeof(UpdatesIntent.UpdatePlugin))]
    private MviReduceResult<UpdatesState, UpdatesEffect> HandleUpdatePlugin(
        UpdatesState state,
        UpdatesIntent.UpdatePlugin intent)
    {
        // 候选 3：消除"在飞操作"双轨。插件更新的在飞与文案改由 PluginsState.Operation 单一承载，
        // 故此处<b>不写</b> UpdatesState.PendingOperation（只发 UpdatePlugin effect 启动事务）；其余逻辑不变。
        return WithEffect(
            state with
            {
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
