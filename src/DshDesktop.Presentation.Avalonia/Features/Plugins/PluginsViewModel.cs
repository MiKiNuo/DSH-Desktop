using DshDesktop.Domain.Plugins;
using DshDesktop.Presentation.Avalonia.Features.Updates;
using MiKiNuo.Mvi.Application.MVI.Command;
using MiKiNuo.Mvi.Application.MVI.Store;
using MiKiNuo.Mvi.Application.MVI.Threading;
using MiKiNuo.Mvi.Application.MVI.ViewModel;
using MiKiNuo.Mvi.Domain.MVI.Binding;

namespace DshDesktop.Presentation.Avalonia.Features.Plugins;

/// <summary>
/// 表示 Plugins ViewModel。
/// 可更新插件名经 <see cref="MviViewModelBase{TState, TIntent, TEffect}.BindSiblingState"/>
/// 自 UpdatesStore 只读投影（§11.2；Phase 8 评审 F3）。
/// </summary>
public sealed partial class PluginsViewModel
    : MviViewModelBase<PluginsState, PluginsIntent, PluginsEffect>
{
    /// <summary>
    /// 初始化 Plugins ViewModel。
    /// </summary>
    /// <param name="store">Plugins 状态存储。</param>
    /// <param name="updatesStore">Updates 状态存储（兄弟 Store，只读订阅可更新投影）。</param>
    /// <param name="uiDispatcher">UI 调度器。</param>
    public PluginsViewModel(
        IMviStore<PluginsState, PluginsIntent, PluginsEffect> store,
        IMviStore<UpdatesState, UpdatesIntent, UpdatesEffect> updatesStore,
        IMviUiDispatcher? uiDispatcher = null)
        : base(store, uiDispatcher)
    {
        ArgumentNullException.ThrowIfNull(updatesStore);

        _ = BindSiblingState(updatesStore, ApplyUpdatesState);
        ApplyUpdatesState(updatesStore.CurrentState);
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
    /// 获取可更新插件包名投影（UpdatesStore.PluginUpdates）。
    /// </summary>
    [MviBind(nameof(PluginsState.UpdatablePlugins), BindingMode = MviBindingMode.OneWay)]
    public partial IReadOnlyList<string> UpdatablePlugins { get; private set; }

    /// <summary>
    /// 获取启用插件命令（载荷：插件包名）。
    /// </summary>
    [MviCommand(typeof(PluginsIntent.EnablePlugin), PayloadType = typeof(string))]
    public partial IMviAsyncCommand EnablePluginCommand { get; private set; }

    /// <summary>
    /// 获取刷新插件清单命令（Phase 8 Issue 06 工具条"刷新"按钮）。
    /// </summary>
    [MviCommand(typeof(PluginsIntent.LoadPlugins))]
    public partial IMviAsyncCommand LoadPluginsCommand { get; private set; }

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
    /// 获取更新插件命令（载荷：插件包名；Phase 8 评审 F3 行内"更新"按钮）。
    /// </summary>
    [MviCommand(typeof(PluginsIntent.UpdatePlugin), PayloadType = typeof(string))]
    public partial IMviAsyncCommand UpdatePluginCommand { get; private set; }

    private void ApplyUpdatesState(UpdatesState updatesState)
    {
        IReadOnlyList<string> names = updatesState.PluginUpdates.Select(u => u.Name).ToArray();
        if (!names.SequenceEqual(Store.CurrentState.UpdatablePlugins))
        {
            _ = DispatchAsync(new PluginsIntent.UpdatablePluginsChanged(names));
        }
    }
}
