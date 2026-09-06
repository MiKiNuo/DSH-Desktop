using MiKiNuo.Mvi.Domain.MVI.State;

namespace DshDesktop.Presentation.Avalonia.Features.Settings;

/// <summary>
/// 表示 Settings 状态（安全模式 + 通知开关 + DSH 通道 + 桌面行为/更新策略开关 + 只读路径信息）。
/// </summary>
/// <param name="SafeMode">是否处于安全模式（权威来源为 config，此处为投影）。</param>
/// <param name="NotificationsEnabled">是否启用 Windows 通知（权威来源为 config，此处为投影）。</param>
/// <param name="Channel">DSH 更新通道（npm dist-tag：latest / alpha）。</param>
/// <param name="NodePath">node.exe 路径（只读）。</param>
/// <param name="DshHome">DSH_HOME 数据根目录（只读）。</param>
/// <param name="DataDirectory">Desktop 数据根目录（只读）。</param>
/// <param name="PluginsDirectory">插件目录（profiles\web\node_modules，只读）。</param>
/// <param name="DshRuntimeDirectory">DSH Runtime 目录（只读）。</param>
/// <param name="MinimizeToTrayOnClose">关闭窗口最小化到托盘（默认开）。</param>
/// <param name="LaunchOnStartup">开机自动启动（默认关）。</param>
/// <param name="BackgroundUpdateCheck">后台检查更新（默认开，UI Ready 后异步）。</param>
/// <param name="AutoDownloadUpdates">自动下载安装（默认关，仅提示不自动覆盖）。</param>
/// <param name="PendingOperation">进行中的操作描述；null 表示空闲。</param>
/// <param name="LastError">最近一次错误信息。</param>
public sealed record SettingsState(
    bool SafeMode,
    bool NotificationsEnabled,
    string Channel,
    string? NodePath,
    string? DshHome,
    string? DataDirectory,
    string? PluginsDirectory,
    string? DshRuntimeDirectory,
    bool MinimizeToTrayOnClose,
    bool LaunchOnStartup,
    bool BackgroundUpdateCheck,
    bool AutoDownloadUpdates,
    string? PendingOperation,
    string? LastError) : IMviState
{
    /// <summary>
    /// 获取初始状态（开关默认值与 DshDesktopConfig 默认值一致：托盘开 / 自启关 / 后台检查开 / 自动下载关）。
    /// </summary>
    public static SettingsState Initial { get; } =
        new(false, true, "latest", null, null, null, null, null, true, false, true, false, null, null);
}
