using MiKiNuo.Mvi.Domain.MVI.Effect;

namespace DshDesktop.Presentation.Avalonia.Features.Settings;

/// <summary>
/// 表示 Settings 副作用。
/// </summary>
public abstract partial record SettingsEffect : IMviEffect
{
    /// <summary>
    /// 表示加载设置信息副作用。
    /// </summary>
    public sealed partial record LoadSettings : SettingsEffect;

    /// <summary>
    /// 表示持久化安全模式副作用。
    /// </summary>
    /// <param name="Enabled">目标安全模式状态。</param>
    public sealed partial record SaveSafeMode(bool Enabled) : SettingsEffect;

    /// <summary>
    /// 表示持久化 Windows 通知开关副作用。
    /// </summary>
    /// <param name="Enabled">目标通知开关状态。</param>
    public sealed partial record SaveNotificationsEnabled(bool Enabled) : SettingsEffect;

    /// <summary>
    /// 表示持久化 DSH 更新通道副作用。
    /// </summary>
    /// <param name="Channel">目标通道。</param>
    public sealed partial record SaveChannel(string Channel) : SettingsEffect;

    /// <summary>
    /// 表示持久化"关闭窗口最小化到托盘"开关副作用。
    /// </summary>
    /// <param name="Enabled">目标开关状态。</param>
    public sealed partial record SaveMinimizeToTrayOnClose(bool Enabled) : SettingsEffect;

    /// <summary>
    /// 表示持久化"开机自动启动"开关副作用（含注册表 Run 键写/删）。
    /// </summary>
    /// <param name="Enabled">目标开关状态。</param>
    public sealed partial record SaveLaunchOnStartup(bool Enabled) : SettingsEffect;

    /// <summary>
    /// 表示持久化"后台检查更新"开关副作用。
    /// </summary>
    /// <param name="Enabled">目标开关状态。</param>
    public sealed partial record SaveBackgroundUpdateCheck(bool Enabled) : SettingsEffect;

    /// <summary>
    /// 表示持久化"自动下载安装"开关副作用。
    /// </summary>
    /// <param name="Enabled">目标开关状态。</param>
    public sealed partial record SaveAutoDownloadUpdates(bool Enabled) : SettingsEffect;

    /// <summary>
    /// 表示打开目录副作用（经 Mediator 路由到组合根的 IPathOpener 端口）。
    /// </summary>
    /// <param name="Path">目标目录绝对路径。</param>
    public sealed partial record OpenDirectory(string Path) : SettingsEffect;
}
