using DshDesktop.Domain.Plugins;
using MiKiNuo.Mvi.Application.MVI.Command;
using MiKiNuo.Mvi.Application.MVI.Store;
using MiKiNuo.Mvi.Application.MVI.Threading;
using MiKiNuo.Mvi.Application.MVI.ViewModel;
using MiKiNuo.Mvi.Domain.MVI.Binding;

namespace DshDesktop.Presentation.Avalonia.Features.Plugins;

/// <summary>
/// 表示 Plugins ViewModel。
/// </summary>
public sealed partial class PluginsViewModel
    : MviViewModelBase<PluginsState, PluginsIntent, PluginsEffect>
{
    /// <summary>
    /// 初始化 Plugins ViewModel。
    /// </summary>
    /// <param name="store">Plugins 状态存储。</param>
    /// <param name="uiDispatcher">UI 调度器。</param>
    public PluginsViewModel(
        IMviStore<PluginsState, PluginsIntent, PluginsEffect> store,
        IMviUiDispatcher? uiDispatcher = null)
        : base(store, uiDispatcher)
    {
    }

    /// <summary>
    /// 获取插件清单。
    /// </summary>
    [MviBind(nameof(PluginsState.Plugins), BindingMode = MviBindingMode.OneWay)]
    public partial IReadOnlyList<PluginInfo> Plugins { get; private set; }

    /// <summary>
    /// 获取进行中的操作描述。
    /// </summary>
    [MviBind(nameof(PluginsState.PendingOperation), BindingMode = MviBindingMode.OneWay)]
    public partial string? PendingOperation { get; private set; }

    /// <summary>
    /// 获取安装事务进度（§20 状态机）。
    /// </summary>
    [MviBind(nameof(PluginsState.Operation), BindingMode = MviBindingMode.OneWay)]
    public partial PluginOperation? Operation { get; private set; }

    /// <summary>
    /// 获取最近一次错误信息。
    /// </summary>
    [MviBind(nameof(PluginsState.LastError), BindingMode = MviBindingMode.OneWay)]
    public partial string? LastError { get; private set; }

    /// <summary>
    /// 获取启用插件命令（载荷：插件包名）。
    /// </summary>
    [MviCommand(typeof(PluginsIntent.EnablePlugin), PayloadType = typeof(string))]
    public partial IMviAsyncCommand EnablePluginCommand { get; private set; }

    /// <summary>
    /// 获取禁用插件命令（载荷：插件包名）。
    /// </summary>
    [MviCommand(typeof(PluginsIntent.DisablePlugin), PayloadType = typeof(string))]
    public partial IMviAsyncCommand DisablePluginCommand { get; private set; }

    /// <summary>
    /// 获取卸载插件命令（载荷：插件包名）。
    /// </summary>
    [MviCommand(typeof(PluginsIntent.UninstallPlugin), PayloadType = typeof(string))]
    public partial IMviAsyncCommand UninstallPluginCommand { get; private set; }

    /// <summary>
    /// 获取安装插件命令（载荷：包名或 .tgz 路径）。
    /// </summary>
    [MviCommand(typeof(PluginsIntent.InstallPlugin), PayloadType = typeof(string))]
    public partial IMviAsyncCommand InstallPluginCommand { get; private set; }

    /// <summary>
    /// 获取禁用全部第三方插件命令。
    /// </summary>
    [MviCommand(typeof(PluginsIntent.DisableAllThirdParty))]
    public partial IMviAsyncCommand DisableAllThirdPartyCommand { get; private set; }
}
