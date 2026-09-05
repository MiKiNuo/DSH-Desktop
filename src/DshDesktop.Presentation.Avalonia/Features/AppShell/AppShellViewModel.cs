using DshDesktop.Domain.Runtime;
using DshDesktop.Presentation.Avalonia.Features.Runtime;
using DshDesktop.Presentation.Avalonia.Features.Updates;
using MiKiNuo.Mvi.Application.MVI.Command;
using MiKiNuo.Mvi.Application.MVI.Store;
using MiKiNuo.Mvi.Application.MVI.Threading;
using MiKiNuo.Mvi.Application.MVI.ViewModel;
using MiKiNuo.Mvi.Domain.MVI.Binding;
using MiKiNuo.Mvi.Domain.MVI.Effect;

namespace DshDesktop.Presentation.Avalonia.Features.AppShell;

/// <summary>
/// 表示应用壳 ViewModel（仅 State 投影与命令，不持有业务状态）。
/// </summary>
/// <remarks>
/// RuntimeIndicator / UpdateBadge 经 <see cref="MviViewModelBase{TState, TIntent, TEffect}.BindSiblingState"/>
/// 从 Runtime / Updates Store 只读投影（兄弟 Store 协作，§11.2），变化时回流意图进入自身 Store。
/// </remarks>
public sealed partial class AppShellViewModel
    : MviViewModelBase<AppShellState, AppShellIntent, UnitEffect>
{
    /// <summary>
    /// 初始化应用壳 ViewModel。
    /// </summary>
    /// <param name="store">应用壳状态存储。</param>
    /// <param name="runtimeStore">Runtime 状态存储（兄弟 Store，只读订阅）。</param>
    /// <param name="updatesStore">Updates 状态存储（兄弟 Store，只读订阅）。</param>
    /// <param name="uiDispatcher">UI 调度器。</param>
    public AppShellViewModel(
        IMviStore<AppShellState, AppShellIntent, UnitEffect> store,
        IMviStore<RuntimeState, RuntimeIntent, RuntimeEffect> runtimeStore,
        IMviStore<UpdatesState, UpdatesIntent, UpdatesEffect> updatesStore,
        IMviUiDispatcher? uiDispatcher = null)
        : base(store, uiDispatcher)
    {
        ArgumentNullException.ThrowIfNull(runtimeStore);
        ArgumentNullException.ThrowIfNull(updatesStore);

        _ = BindSiblingState(runtimeStore, ApplyRuntimeState);
        _ = BindSiblingState(updatesStore, ApplyUpdatesState);
        ApplyRuntimeState(runtimeStore.CurrentState);
        ApplyUpdatesState(updatesStore.CurrentState);
    }

    /// <summary>
    /// 获取当前页面。
    /// </summary>
    [MviBind(nameof(AppShellState.CurrentPage), BindingMode = MviBindingMode.OneWay)]
    public partial ShellPage CurrentPage { get; private set; }

    /// <summary>
    /// 获取侧边栏是否折叠。
    /// </summary>
    [MviBind(nameof(AppShellState.SidebarCollapsed), BindingMode = MviBindingMode.OneWay)]
    public partial bool SidebarCollapsed { get; private set; }

    /// <summary>
    /// 获取 Runtime 生命周期投影（侧栏状态点）。
    /// </summary>
    [MviBind(nameof(AppShellState.RuntimeIndicator), BindingMode = MviBindingMode.OneWay)]
    public partial RuntimeLifecycle RuntimeIndicator { get; private set; }

    /// <summary>
    /// 获取可用更新数投影（侧栏 Updates 入口徽标；0 表示无可用更新）。
    /// </summary>
    [MviBind(nameof(AppShellState.UpdateBadge), BindingMode = MviBindingMode.OneWay)]
    public partial int UpdateBadge { get; private set; }

    /// <summary>
    /// 获取导航到 Runtime 页命令。
    /// </summary>
    [MviCommand(typeof(AppShellIntent.ShowRuntime))]
    public partial IMviAsyncCommand ShowRuntimeCommand { get; private set; }

    /// <summary>
    /// 获取导航到 Dashboard 页命令。
    /// </summary>
    [MviCommand(typeof(AppShellIntent.ShowDashboard))]
    public partial IMviAsyncCommand ShowDashboardCommand { get; private set; }

    /// <summary>
    /// 获取导航到 Workbench 页命令。
    /// </summary>
    [MviCommand(typeof(AppShellIntent.ShowWorkbench))]
    public partial IMviAsyncCommand ShowWorkbenchCommand { get; private set; }

    /// <summary>
    /// 获取导航到 Diagnostics 页命令。
    /// </summary>
    [MviCommand(typeof(AppShellIntent.ShowDiagnostics))]
    public partial IMviAsyncCommand ShowDiagnosticsCommand { get; private set; }

    /// <summary>
    /// 获取导航到 Plugins 页命令。
    /// </summary>
    [MviCommand(typeof(AppShellIntent.ShowPlugins))]
    public partial IMviAsyncCommand ShowPluginsCommand { get; private set; }

    /// <summary>
    /// 获取导航到 Updates 页命令。
    /// </summary>
    [MviCommand(typeof(AppShellIntent.ShowUpdates))]
    public partial IMviAsyncCommand ShowUpdatesCommand { get; private set; }

    /// <summary>
    /// 获取导航到 Settings 页命令。
    /// </summary>
    [MviCommand(typeof(AppShellIntent.ShowSettings))]
    public partial IMviAsyncCommand ShowSettingsCommand { get; private set; }

    /// <summary>
    /// 获取切换侧边栏折叠状态命令。
    /// </summary>
    [MviCommand(typeof(AppShellIntent.ToggleSidebar))]
    public partial IMviAsyncCommand ToggleSidebarCommand { get; private set; }

    /// <summary>
    /// 获取 Desktop 版本（编译期常量，非状态；§50 侧栏常显）。
    /// </summary>
    public string DesktopVersion => DesktopInfo.Version;

    private void ApplyRuntimeState(RuntimeState runtimeState)
    {
        if (runtimeState.Lifecycle != Store.CurrentState.RuntimeIndicator)
        {
            _ = DispatchAsync(new AppShellIntent.RuntimeIndicatorChanged(runtimeState.Lifecycle));
        }
    }

    private void ApplyUpdatesState(UpdatesState updatesState)
    {
        int count = updatesState.AvailableCount;

        if (count != Store.CurrentState.UpdateBadge)
        {
            _ = DispatchAsync(new AppShellIntent.UpdateBadgeChanged(count));
        }
    }
}
