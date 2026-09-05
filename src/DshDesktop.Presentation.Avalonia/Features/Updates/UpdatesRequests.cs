using DshDesktop.Domain.Updates;
using MiKiNuo.Mvi.Domain.MVI.Mediator;

namespace DshDesktop.Presentation.Avalonia.Features.Updates;

/// <summary>
/// 表示一次更新检查的结果载荷。
/// </summary>
/// <param name="LatestDshVersion">通道最新 DSH 版本。</param>
/// <param name="CurrentDshVersion">当前激活的 DSH 版本。</param>
/// <param name="Runtimes">可用 Runtime 列表。</param>
/// <param name="PluginUpdates">可更新的插件列表。</param>
/// <param name="LatestDesktopVersion">最新 Desktop 版本；无更新或未安装形态为 null。</param>
public sealed record CheckUpdatesResponse(
    string? LatestDshVersion,
    string? CurrentDshVersion,
    IReadOnlyList<DshRuntimeInfo> Runtimes,
    IReadOnlyList<PluginUpdateInfo> PluginUpdates,
    string? LatestDesktopVersion);

/// <summary>
/// 表示检查更新的跨层请求（§28 Mediator）。
/// </summary>
public sealed record CheckUpdatesRequest : IMviRequest<CheckUpdatesResponse>;

/// <summary>
/// 表示安装指定版本 DSH Runtime 的跨层请求；响应为最新 Runtime 列表。
/// </summary>
/// <param name="Version">目标版本。</param>
public sealed record InstallDshRuntimeRequest(string Version)
    : IMviRequest<IReadOnlyList<DshRuntimeInfo>>;

/// <summary>
/// 表示激活指定 Runtime 的跨层请求（空字符串 = 借用外部安装）；响应为最新 Runtime 列表。
/// </summary>
/// <param name="Version">版本目录名，空字符串表示借用。</param>
public sealed record ActivateDshRuntimeRequest(string Version)
    : IMviRequest<IReadOnlyList<DshRuntimeInfo>>;

/// <summary>
/// 表示更新插件的跨层请求（内部走 §19 安装事务）。
/// </summary>
/// <param name="Name">插件包名。</param>
public sealed record UpdatePluginRequest(string Name) : IMviRequest<bool>;

/// <summary>
/// 表示下载并应用最近检查到的 Desktop 更新（应用后进程重启，不返回）。
/// </summary>
public sealed record DownloadAndApplyDesktopUpdateRequest : IMviRequest<bool>;
