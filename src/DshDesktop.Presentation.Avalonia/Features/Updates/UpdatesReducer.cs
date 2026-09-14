using System;
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
        // 仅当"当前进行中操作即本次检查自身"时才清空 PendingOperation：空闲检查本就为空；
        // 插件更新成功经此处终态回流（§23）需清空其自身标记。后台静默检查
        // （App.axaml.cs 启动路径 BackgroundCheckUpdatesAsync，绕过按钮）可能在 Desktop 下载 / Runtime 安装 /
        // 激活进行中抵达，若无条件清空会释放遮罩与导航锁（§22）。插件标记前缀须与 HandleUpdatePlugin 的
        // PendingOperation 文案保持一致。
        const string PluginPendingPrefix = "更新";
        bool canClearPending = state.PendingOperation is null
            || state.PendingOperation.StartsWith(PluginPendingPrefix, StringComparison.Ordinal);
        if (!canClearPending)
        {
            return WithEffect(
                state with { Status = UpdateStatus.Checking, LastError = null },
                new UpdatesEffect.CheckUpdates());
        }

        return WithEffect(
            state with { Status = UpdateStatus.Checking, LastError = null, PendingOperation = null },
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
            LatestDesktopVersion = result.LatestDesktopVersion,
            LastError = null,
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
                PendingOperation = $"下载 Desktop 更新 {state.LatestDesktopVersion}…",
                DesktopDownloadProgress = 0,
                LastError = null,
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
        // 仍可能有一个进度回调在队列中）。仅当确有进行中操作时才更新进度文本/百分比；否则原样返回——
        // 否则壳遮罩会被凭空拉起并永久卡死（用户无法关闭）。
        if (state.PendingOperation is null)
        {
            return Unchanged(state);
        }

        return Unchanged(state with
        {
            DesktopDownloadProgress = intent.Percent,
            PendingOperation = $"下载 Desktop 更新 {state.LatestDesktopVersion}（{intent.Percent}%）…",
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
        });
    }
}
