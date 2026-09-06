using MiKiNuo.Mvi.Application.MVI.Command;
using MiKiNuo.Mvi.Application.MVI.Store;
using MiKiNuo.Mvi.Application.MVI.Threading;
using MiKiNuo.Mvi.Application.MVI.ViewModel;
using MiKiNuo.Mvi.Domain.MVI.Binding;

namespace DshDesktop.Presentation.Avalonia.Features.Settings;

/// <summary>
/// 表示 Settings ViewModel（State 投影 + 命令，不持有业务真实状态）。
/// </summary>
public sealed partial class SettingsViewModel
    : MviViewModelBase<SettingsState, SettingsIntent, SettingsEffect>
{
    /// <summary>
    /// 初始化 Settings ViewModel。
    /// </summary>
    /// <param name="store">Settings 状态存储。</param>
    /// <param name="uiDispatcher">UI 调度器。</param>
    public SettingsViewModel(
        IMviStore<SettingsState, SettingsIntent, SettingsEffect> store,
        IMviUiDispatcher? uiDispatcher = null)
        : base(store, uiDispatcher)
    {
    }

    /// <summary>
    /// 获取是否处于安全模式。
    /// </summary>
    [MviBind(nameof(SettingsState.SafeMode), BindingMode = MviBindingMode.OneWay)]
    public partial bool SafeMode { get; private set; }

    /// <summary>
    /// 获取是否启用 Windows 通知。
    /// </summary>
    [MviBind(nameof(SettingsState.NotificationsEnabled), BindingMode = MviBindingMode.OneWay)]
    public partial bool NotificationsEnabled { get; private set; }

    /// <summary>
    /// 获取 DSH 更新通道。
    /// </summary>
    [MviBind(nameof(SettingsState.Channel), BindingMode = MviBindingMode.OneWay)]
    public partial string Channel { get; private set; }

    /// <summary>
    /// 获取 node.exe 路径。
    /// </summary>
    [MviBind(nameof(SettingsState.NodePath), BindingMode = MviBindingMode.OneWay)]
    public partial string? NodePath { get; private set; }

    /// <summary>
    /// 获取 DSH_HOME 数据根目录。
    /// </summary>
    [MviBind(nameof(SettingsState.DshHome), BindingMode = MviBindingMode.OneWay)]
    public partial string? DshHome { get; private set; }

    /// <summary>
    /// 获取 Desktop 数据根目录。
    /// </summary>
    [MviBind(nameof(SettingsState.DataDirectory), BindingMode = MviBindingMode.OneWay)]
    public partial string? DataDirectory { get; private set; }

    /// <summary>
    /// 获取插件目录。
    /// </summary>
    [MviBind(nameof(SettingsState.PluginsDirectory), BindingMode = MviBindingMode.OneWay)]
    public partial string? PluginsDirectory { get; private set; }

    /// <summary>
    /// 获取 DSH Runtime 目录。
    /// </summary>
    [MviBind(nameof(SettingsState.DshRuntimeDirectory), BindingMode = MviBindingMode.OneWay)]
    public partial string? DshRuntimeDirectory { get; private set; }

    /// <summary>
    /// 获取是否"关闭窗口最小化到托盘"。
    /// </summary>
    [MviBind(nameof(SettingsState.MinimizeToTrayOnClose), BindingMode = MviBindingMode.OneWay)]
    public partial bool MinimizeToTrayOnClose { get; private set; }

    /// <summary>
    /// 获取是否"开机自动启动"。
    /// </summary>
    [MviBind(nameof(SettingsState.LaunchOnStartup), BindingMode = MviBindingMode.OneWay)]
    public partial bool LaunchOnStartup { get; private set; }

    /// <summary>
    /// 获取是否"后台检查更新"。
    /// </summary>
    [MviBind(nameof(SettingsState.BackgroundUpdateCheck), BindingMode = MviBindingMode.OneWay)]
    public partial bool BackgroundUpdateCheck { get; private set; }

    /// <summary>
    /// 获取是否"自动下载安装"。
    /// </summary>
    [MviBind(nameof(SettingsState.AutoDownloadUpdates), BindingMode = MviBindingMode.OneWay)]
    public partial bool AutoDownloadUpdates { get; private set; }

    /// <summary>
    /// 获取 Desktop 版本（编译期常量，非状态——§6 不入 State，同 AppShell/Updates 先例）。
    /// </summary>
    public string DesktopVersion => DesktopInfo.Version;

    /// <summary>
    /// 获取进行中的操作描述。
    /// </summary>
    [MviBind(nameof(SettingsState.PendingOperation), BindingMode = MviBindingMode.OneWay)]
    public partial string? PendingOperation { get; private set; }

    /// <summary>
    /// 获取最近一次错误信息。
    /// </summary>
    [MviBind(nameof(SettingsState.LastError), BindingMode = MviBindingMode.OneWay)]
    public partial string? LastError { get; private set; }

    /// <summary>
    /// 获取加载设置命令。
    /// </summary>
    [MviCommand(typeof(SettingsIntent.LoadSettings))]
    public partial IMviAsyncCommand LoadSettingsCommand { get; private set; }

    /// <summary>
    /// 获取切换安全模式命令（无载荷翻转）。
    /// </summary>
    [MviCommand(typeof(SettingsIntent.ToggleSafeMode))]
    public partial IMviAsyncCommand ToggleSafeModeCommand { get; private set; }

    /// <summary>
    /// 获取切换 Windows 通知命令（无载荷翻转）。
    /// </summary>
    [MviCommand(typeof(SettingsIntent.ToggleNotifications))]
    public partial IMviAsyncCommand ToggleNotificationsCommand { get; private set; }

    /// <summary>
    /// 获取修改 DSH 更新通道命令（载荷：latest / alpha）。
    /// </summary>
    [MviCommand(typeof(SettingsIntent.ChangeChannel), PayloadType = typeof(string))]
    public partial IMviAsyncCommand ChangeChannelCommand { get; private set; }

    /// <summary>
    /// 获取切换"关闭窗口最小化到托盘"命令（无载荷翻转）。
    /// </summary>
    [MviCommand(typeof(SettingsIntent.ToggleMinimizeToTrayOnClose))]
    public partial IMviAsyncCommand ToggleMinimizeToTrayOnCloseCommand { get; private set; }

    /// <summary>
    /// 获取切换"开机自动启动"命令（无载荷翻转）。
    /// </summary>
    [MviCommand(typeof(SettingsIntent.ToggleLaunchOnStartup))]
    public partial IMviAsyncCommand ToggleLaunchOnStartupCommand { get; private set; }

    /// <summary>
    /// 获取切换"后台检查更新"命令（无载荷翻转）。
    /// </summary>
    [MviCommand(typeof(SettingsIntent.ToggleBackgroundUpdateCheck))]
    public partial IMviAsyncCommand ToggleBackgroundUpdateCheckCommand { get; private set; }

    /// <summary>
    /// 获取切换"自动下载安装"命令（无载荷翻转）。
    /// </summary>
    [MviCommand(typeof(SettingsIntent.ToggleAutoDownloadUpdates))]
    public partial IMviAsyncCommand ToggleAutoDownloadUpdatesCommand { get; private set; }

    /// <summary>
    /// 获取打开目录命令（载荷：目录绝对路径）。
    /// </summary>
    [MviCommand(typeof(SettingsIntent.OpenDirectory), PayloadType = typeof(string))]
    public partial IMviAsyncCommand OpenDirectoryCommand { get; private set; }
}
