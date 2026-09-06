using MiKiNuo.Mvi.Domain.MVI.Mediator;

namespace DshDesktop.Presentation.Avalonia.Features.Settings;

/// <summary>
/// 表示设置信息快照（跨层契约，组合根从 config 装配）。
/// </summary>
/// <param name="SafeMode">是否处于安全模式。</param>
/// <param name="NotificationsEnabled">是否启用 Windows 通知。</param>
/// <param name="Channel">DSH 更新通道。</param>
/// <param name="NodePath">node.exe 路径。</param>
/// <param name="DshHome">DSH_HOME 数据根目录。</param>
/// <param name="DataDirectory">Desktop 数据根目录。</param>
/// <param name="PluginsDirectory">插件目录（profiles\web\node_modules）。</param>
/// <param name="DshRuntimeDirectory">DSH Runtime 目录（含当前激活版本子目录）。</param>
/// <param name="MinimizeToTrayOnClose">关闭窗口最小化到托盘。</param>
/// <param name="LaunchOnStartup">开机自动启动。</param>
/// <param name="BackgroundUpdateCheck">后台检查更新。</param>
/// <param name="AutoDownloadUpdates">自动下载安装。</param>
public sealed record SettingsInfo(
    bool SafeMode,
    bool NotificationsEnabled,
    string Channel,
    string NodePath,
    string DshHome,
    string DataDirectory,
    string PluginsDirectory,
    string DshRuntimeDirectory,
    bool MinimizeToTrayOnClose,
    bool LaunchOnStartup,
    bool BackgroundUpdateCheck,
    bool AutoDownloadUpdates);

/// <summary>
/// 表示获取设置信息的跨层请求（§28 Mediator）。
/// </summary>
public sealed record GetSettingsInfoRequest : IMviRequest<SettingsInfo>;

/// <summary>
/// 表示修改 DSH 更新通道的跨层请求。
/// </summary>
/// <param name="Channel">目标通道（latest / alpha）。</param>
public sealed record SetDshChannelRequest(string Channel) : IMviRequest<bool>;

/// <summary>
/// 表示修改 Windows 通知开关的跨层请求。
/// </summary>
/// <param name="Enabled">目标通知开关状态。</param>
public sealed record SetNotificationsEnabledRequest(bool Enabled) : IMviRequest<bool>;

/// <summary>
/// 表示修改"关闭窗口最小化到托盘"开关的跨层请求（Phase 8 Issue 05）。
/// </summary>
/// <param name="Enabled">目标开关状态。</param>
public sealed record SetMinimizeToTrayOnCloseRequest(bool Enabled) : IMviRequest<bool>;

/// <summary>
/// 表示修改"开机自动启动"开关的跨层请求（含注册表 Run 键写/删，Phase 8 Issue 05）。
/// </summary>
/// <param name="Enabled">目标开关状态。</param>
public sealed record SetLaunchOnStartupRequest(bool Enabled) : IMviRequest<bool>;

/// <summary>
/// 表示修改"后台检查更新"开关的跨层请求（Phase 8 Issue 05）。
/// </summary>
/// <param name="Enabled">目标开关状态。</param>
public sealed record SetBackgroundUpdateCheckRequest(bool Enabled) : IMviRequest<bool>;

/// <summary>
/// 表示修改"自动下载安装"开关的跨层请求（Phase 8 Issue 05）。
/// </summary>
/// <param name="Enabled">目标开关状态。</param>
public sealed record SetAutoDownloadUpdatesRequest(bool Enabled) : IMviRequest<bool>;

/// <summary>
/// 表示打开目录的跨层请求（路由到组合根的 IPathOpener 端口，Phase 8 Issue 05）。
/// </summary>
/// <param name="Path">目标目录绝对路径。</param>
public sealed record OpenPathRequest(string Path) : IMviRequest<bool>;
