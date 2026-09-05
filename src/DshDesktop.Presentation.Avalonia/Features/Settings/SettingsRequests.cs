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
public sealed record SettingsInfo(
    bool SafeMode,
    bool NotificationsEnabled,
    string Channel,
    string NodePath,
    string DshHome,
    string DataDirectory);

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
