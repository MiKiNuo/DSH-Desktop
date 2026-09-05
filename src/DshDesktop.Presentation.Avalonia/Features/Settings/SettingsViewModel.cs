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
    /// 获取 Desktop 版本。
    /// </summary>
    [MviBind(nameof(SettingsState.DesktopVersion), BindingMode = MviBindingMode.OneWay)]
    public partial string? DesktopVersion { get; private set; }

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
    /// 获取修改安全模式命令（载荷：目标状态）。
    /// </summary>
    [MviCommand(typeof(SettingsIntent.ChangeSafeMode), PayloadType = typeof(bool))]
    public partial IMviAsyncCommand ChangeSafeModeCommand { get; private set; }

    /// <summary>
    /// 获取修改 DSH 更新通道命令（载荷：latest / alpha）。
    /// </summary>
    [MviCommand(typeof(SettingsIntent.ChangeChannel), PayloadType = typeof(string))]
    public partial IMviAsyncCommand ChangeChannelCommand { get; private set; }
}
