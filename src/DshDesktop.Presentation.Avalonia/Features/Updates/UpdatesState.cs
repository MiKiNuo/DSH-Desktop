using DshDesktop.Application.Updates;
using DshDesktop.Domain.Updates;
using MiKiNuo.Mvi.Domain.MVI.State;

namespace DshDesktop.Presentation.Avalonia.Features.Updates;

/// <summary>
/// 表示 Updates 状态（§22/§23：Desktop / DSH Runtime 两者独立更新）。
/// </summary>
/// <param name="Status">更新检查状态机。</param>
/// <param name="Channel">DSH 更新通道。</param>
/// <param name="CurrentDshVersion">当前激活的 DSH 版本。</param>
/// <param name="LatestDshVersion">通道最新 DSH 版本；未知为 null。</param>
/// <param name="Runtimes">可用 Runtime 列表（借用 + 自建）。</param>
/// <param name="LatestDesktopVersion">最新 Desktop 版本；无更新或未安装形态为 null（当前版本是编译期常量，见 ViewModel.DesktopVersion）。</param>
/// <param name="Operation">Desktop 下载、Runtime 安装/激活的操作事实。</param>
/// <param name="LastError">最近一次错误信息。</param>
public sealed record UpdatesState(
    UpdateStatus Status,
    string Channel,
    string? CurrentDshVersion,
    string? LatestDshVersion,
    IReadOnlyList<DshRuntimeInfo> Runtimes,
    string? LatestDesktopVersion,
    UpdatesOperation? Operation,
    string? LastError) : IMviState
{
    /// <summary>
    /// 获取初始状态。
    /// </summary>
    public static UpdatesState Initial { get; } = new(
        UpdateStatus.Idle,
        RuntimeChannel.PrereleaseLiteral,
        null, null,
        System.Array.Empty<DshRuntimeInfo>(),
        null, null, null);

    /// <summary>进行中的自有操作文案；终态与空闲为 null。</summary>
    public string? PendingOperation => Operation?.PendingOperation;

    /// <summary>Desktop 下载进度；其他操作、终态与空闲为 null。</summary>
    public int? DesktopDownloadProgress => Operation?.DesktopDownloadProgress;

    /// <summary>
    /// 获取可用更新总数（AppShell UpdateBadge 投影口径，§22 来源独立计数）：
    /// Desktop 侧 LatestDesktopVersion 仅在更新器确认有更新时非空
    /// （当前版本是编译期常量），非空计 1；DSH 侧 LatestDshVersion 是通道最新版本（始终上报），
    /// 须严格新于当前版本才计 1（ADR-0011 起与 <see cref="DshStage"/> 同口径）。
    /// </summary>
    public int AvailableCount =>
        (LatestDesktopVersion is not null ? 1 : 0)
        + (LatestDshVersion is not null
            && CurrentDshVersion is not null
            && RuntimeVersion.IsNewer(LatestDshVersion, CurrentDshVersion) ? 1 : 0);

    /// <summary>
    /// 获取 DSH Runtime 更新卡的展示阶段（视图 badge 与主按钮的唯一判定口径）。
    /// 此前视图裸绑 <c>LatestDshVersion 非空</c> 判定「有可用更新」，导致当前版已等于最新版时
    /// 仍显示可更新且安装按钮常显（2026-09-18 实机投诉）；三态推导集中于此，视图不再各自判等。
    /// 借用副本是外部只读安装，不算「已下载到本机」。
    /// </summary>
    /// <remarks>
    /// ADR-0011：版本比较从 Ordinal 字符串判等换成 <see cref="RuntimeVersion"/> semver 比较。
    /// 字符串判等有两处错判——<c>rc.10</c> 与 <c>rc.2</c> 被判为不同版本，以及「通道最新版比当前版旧」
    /// （换源后 GitHub 可能落后于本机已装版本）被误报成有更新。当前版本未知（借用入口缺失）时
    /// 仍按可安装处理，维持既有行为。
    /// </remarks>
    public DshRuntimeStage DshStage =>
        LatestDshVersion is null
        || (CurrentDshVersion is { } current && !RuntimeVersion.IsNewer(LatestDshVersion, current))
            ? DshRuntimeStage.UpToDate
            : Runtimes.Any(r => !r.IsBorrowed
                && !r.IsActive
                && string.Equals(r.Version, LatestDshVersion, StringComparison.Ordinal))
                ? DshRuntimeStage.ReadyToActivate
                : DshRuntimeStage.Available;
}
