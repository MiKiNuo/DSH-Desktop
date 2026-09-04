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
public sealed partial class AppShellViewModel
    : MviViewModelBase<AppShellState, AppShellIntent, UnitEffect>
{
    /// <summary>
    /// 初始化应用壳 ViewModel。
    /// </summary>
    /// <param name="store">应用壳状态存储。</param>
    /// <param name="uiDispatcher">UI 调度器。</param>
    public AppShellViewModel(
        IMviStore<AppShellState, AppShellIntent, UnitEffect> store,
        IMviUiDispatcher? uiDispatcher = null)
        : base(store, uiDispatcher)
    {
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
    /// 获取导航到 Runtime 页命令。
    /// </summary>
    [MviCommand(typeof(AppShellIntent.ShowRuntime))]
    public partial IMviAsyncCommand ShowRuntimeCommand { get; private set; }

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
    /// 获取切换侧边栏折叠状态命令。
    /// </summary>
    [MviCommand(typeof(AppShellIntent.ToggleSidebar))]
    public partial IMviAsyncCommand ToggleSidebarCommand { get; private set; }
}
