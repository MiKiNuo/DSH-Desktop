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
    /// 刻意不动 <c>IsPluginUpdatePending</c>：该标记表示「待办属于插件操作」，只有插件操作自己的终态
    /// （<see cref="HandlePluginOperationFinished"/>）才能清。检查完成只说明本次检查结束，
    /// 与插件更新是否结束无关；在此清标记会让进行中的插件更新此后无法被终态清空（遮罩永久卡死）。
    /// </remarks>
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
            LatestDesktopVersion = result.LatestDesktopVersion,
            LastError = null,
        });
    }

    /// <summary>
    /// 处理插件事务终态意图（组合根订阅 <c>IPluginOrchestrator.OperationChanged</c> 后在 Completed 阶段发布）：
    /// 释放本次插件操作自己的待办（壳遮罩 + 导航锁的唯一释放点）、按名摘除该插件的可更新行
    /// （乐观刷新，不等慢网络检查回来，§23），并回流一次检查以对账。
    /// </summary>
    /// <remarks>
    /// 终态清空用显式来源标记判定，而不是借用「发起检查」意图：否则检查与终态互相踩踏
    /// （2026-09-21 v0.1.8 实机：遮罩永久卡死 / 被提前释放）。
    /// </remarks>
    [MviReduce(typeof(UpdatesIntent.PluginOperationFinished))]
    private MviReduceResult<UpdatesState, UpdatesEffect> HandlePluginOperationFinished(
        UpdatesState state,
        UpdatesIntent.PluginOperationFinished intent)
    {
        // 仅当待办确属插件操作时才清空：Desktop 下载 / Runtime 安装 / 激活进行中抵达的插件事务终态
        // 不得释放遮罩与导航锁（§22）。标记非真时它本就是 false，故可无条件写 false。
        bool ownedByPluginOperation = state.IsPluginUpdatePending;
        return WithEffect(
            state with
            {
                Status = UpdateStatus.Checking,
                PendingOperation = ownedByPluginOperation ? null : state.PendingOperation,
                IsPluginUpdatePending = false,
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
                PendingOperation = $"下载 Desktop 更新 {state.LatestDesktopVersion}…",
                DesktopDownloadProgress = 0,
                LastError = null,
                IsPluginUpdatePending = false,
            },
            new UpdatesEffect.DownloadAndApplyDesktopUpdate());
    }

    /// <summary>
    /// 处理 Desktop 更新下载进度回流意图。
    /// </summary>
    [MviReduce(typeof(UpdatesIntent.DesktopDownloadProgress))]
    private MviReduceResult<UpdatesState, UpdatesEffect> HandleDesktopDownloadProgress(
        UpdatesState state,
        UpdatesIntent.DesktopDownloadProgress intent)
    {
        // 防"终态事件之后的进度回流"复活 PendingOperation（§22：进度回调 fire-and-forget，操作失败/成功后
        // 仍可能有一个进度回调在队列中），也防"插件更新期间抵达的迟到进度回调"劫持待办：
        // 后者会把待办改写成下载文案并清掉来源标记，使插件终态再也不能释放遮罩（2026-09-21 复审 P1）。
        // 正确判据是「确在 Desktop 下载期」——DesktopDownloadProgress 仅下载期间非空（其余操作与终态一律 null）。
        if (state.PendingOperation is null || state.DesktopDownloadProgress is null)
        {
            return Unchanged(state);
        }

        return Unchanged(state with
        {
            DesktopDownloadProgress = intent.Percent,
            PendingOperation = $"下载 Desktop 更新 {state.LatestDesktopVersion}（{intent.Percent}%）…",
            IsPluginUpdatePending = false,
        });
    }

    /// <summary>
    /// 处理安装 DSH Runtime 意图。
    /// </summary>
    /// <remarks>
    /// 非下载操作一律把 <c>DesktopDownloadProgress</c> 复位为 null：遮罩的「旋转图标 ↔ 确定进度条」
    /// 互斥切换依赖「进度字段仅在 Desktop 下载期间有值」，残留的旧百分比会让遮罩显示一个卡住的进度条。
    /// </remarks>
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
                DesktopDownloadProgress = null,
                LastError = null,
                IsPluginUpdatePending = false,
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
                DesktopDownloadProgress = null,
                LastError = null,
                IsPluginUpdatePending = false,
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
                DesktopDownloadProgress = null,
                LastError = null,
                IsPluginUpdatePending = true,
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
        // 终态一并清 DownloadProgress：否则中途失败/中止的下载会把百分比留在状态里，
        // 遮罩与页面进度条下次操作时会停在旧值上不动（假死观感）。
        return Unchanged(state with
        {
            Runtimes = intent.Runtimes,
            CurrentDshVersion = active?.Version ?? state.CurrentDshVersion,
            Status = UpdateStatus.Idle,
            PendingOperation = null,
            DesktopDownloadProgress = null,
            IsPluginUpdatePending = false,
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
            DesktopDownloadProgress = null,
            LastError = intent.Error,
            IsPluginUpdatePending = false,
        });
    }
}
