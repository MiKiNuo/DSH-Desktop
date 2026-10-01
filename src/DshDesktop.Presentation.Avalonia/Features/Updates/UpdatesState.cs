using DshDesktop.Domain.Plugins;
using DshDesktop.Domain.Updates;
using MiKiNuo.Mvi.Domain.MVI.State;

namespace DshDesktop.Presentation.Avalonia.Features.Updates;

/// <summary>
/// 表示 Updates 状态（§22/§23：Desktop / DSH Runtime / Plugin 三者独立更新）。
/// </summary>
/// <param name="Status">更新检查状态机。</param>
/// <param name="Channel">DSH 更新通道。</param>
/// <param name="CurrentDshVersion">当前激活的 DSH 版本。</param>
/// <param name="LatestDshVersion">通道最新 DSH 版本；未知为 null。</param>
/// <param name="Runtimes">可用 Runtime 列表（借用 + 自建）。</param>
/// <param name="PluginUpdates">可更新的插件列表。</param>
/// <param name="LatestDesktopVersion">最新 Desktop 版本；无更新或未安装形态为 null（当前版本是编译期常量，见 ViewModel.DesktopVersion）。</param>
/// <param name="Operation">Desktop 下载、Runtime 安装/激活的操作事实；插件事务由 PluginsState.Operation 承载。</param>
/// <param name="LastError">最近一次错误信息。</param>
public sealed record UpdatesState(
    UpdateStatus Status,
    string Channel,
    string? CurrentDshVersion,
    string? LatestDshVersion,
    IReadOnlyList<DshRuntimeInfo> Runtimes,
    IReadOnlyList<PluginUpdateInfo> PluginUpdates,
    string? LatestDesktopVersion,
    UpdatesOperation? Operation,
    string? LastError) : IMviState
{
    /// <summary>
    /// 获取初始状态。
    /// </summary>
    public static UpdatesState Initial { get; } = new(
        UpdateStatus.Idle,
        "latest",
        null, null,
        System.Array.Empty<DshRuntimeInfo>(),
        System.Array.Empty<PluginUpdateInfo>(),
        null, null, null);

    /// <summary>进行中的自有操作文案；终态与空闲为 null。</summary>
    public string? PendingOperation => Operation?.PendingOperation;

    /// <summary>Desktop 下载进度；其他操作、终态与空闲为 null。</summary>
    public int? DesktopDownloadProgress => Operation?.DesktopDownloadProgress;

    /// <summary>
    /// 获取可用更新总数（AppShell UpdateBadge 投影口径，§22 三来源独立计数）：
    /// PluginUpdates 每条计 1；Desktop 侧 LatestDesktopVersion 仅在更新器确认有更新时非空
    /// （当前版本是编译期常量），非空计 1；DSH 侧 LatestDshVersion 是通道最新版本（始终上报），
    /// 须与当前版本不等才计 1。
    /// </summary>
    public int AvailableCount =>
        PluginUpdates.Count
        + (LatestDesktopVersion is not null ? 1 : 0)
        + (LatestDshVersion is not null
            && CurrentDshVersion is not null
            && LatestDshVersion != CurrentDshVersion ? 1 : 0);

    /// <summary>
    /// 获取 DSH Runtime 更新卡的展示阶段（视图 badge 与主按钮的唯一判定口径）。
    /// 此前视图裸绑 <c>LatestDshVersion 非空</c> 判定「有可用更新」，导致当前版已等于最新版时
    /// 仍显示可更新且安装按钮常显（2026-09-18 实机投诉）；三态推导集中于此，视图不再各自判等。
    /// 借用副本是外部只读安装，不算「已下载到本机」；版本判等用 Ordinal（npm 与 package.json 同为裸 semver）。
    /// </summary>
    public DshRuntimeStage DshStage =>
        LatestDshVersion is null
        || string.Equals(LatestDshVersion, CurrentDshVersion, StringComparison.Ordinal)
            ? DshRuntimeStage.UpToDate
            : Runtimes.Any(r => !r.IsBorrowed
                && !r.IsActive
                && string.Equals(r.Version, LatestDshVersion, StringComparison.Ordinal))
                ? DshRuntimeStage.ReadyToActivate
                : DshRuntimeStage.Available;
}
