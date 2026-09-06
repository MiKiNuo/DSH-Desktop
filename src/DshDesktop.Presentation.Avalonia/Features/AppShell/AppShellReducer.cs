using MiKiNuo.Mvi.Application.MVI.Reducer;
using MiKiNuo.Mvi.Domain.DI;
using MiKiNuo.Mvi.Domain.MVI.Effect;
using MiKiNuo.Mvi.Domain.MVI.Reducer;

namespace DshDesktop.Presentation.Avalonia.Features.AppShell;

/// <summary>
/// 表示应用壳规约器。应用壳无副作用，Effect 通道使用 <see cref="UnitEffect"/>。
/// </summary>
[MviFeature]
public sealed partial class AppShellReducer
    : MviReducerBase<AppShellState, AppShellIntent, UnitEffect>
{
    /// <summary>
    /// 处理导航到 Runtime 页意图。
    /// </summary>
    [MviReduce(typeof(AppShellIntent.ShowRuntime))]
    private MviReduceResult<AppShellState, UnitEffect> HandleShowRuntime(
        AppShellState state,
        AppShellIntent.ShowRuntime intent)
    {
        return Unchanged(state with { CurrentPage = ShellPage.Runtime });
    }

    /// <summary>
    /// 处理导航到 Workbench 页意图。
    /// </summary>
    [MviReduce(typeof(AppShellIntent.ShowWorkbench))]
    private MviReduceResult<AppShellState, UnitEffect> HandleShowWorkbench(
        AppShellState state,
        AppShellIntent.ShowWorkbench intent)
    {
        return Unchanged(state with { CurrentPage = ShellPage.Workbench });
    }

    /// <summary>
    /// 处理导航到 Diagnostics 页意图。
    /// </summary>
    [MviReduce(typeof(AppShellIntent.ShowDiagnostics))]
    private MviReduceResult<AppShellState, UnitEffect> HandleShowDiagnostics(
        AppShellState state,
        AppShellIntent.ShowDiagnostics intent)
    {
        return Unchanged(state with { CurrentPage = ShellPage.Diagnostics });
    }

    /// <summary>
    /// 处理导航到 Plugins 页意图。
    /// </summary>
    [MviReduce(typeof(AppShellIntent.ShowPlugins))]
    private MviReduceResult<AppShellState, UnitEffect> HandleShowPlugins(
        AppShellState state,
        AppShellIntent.ShowPlugins intent)
    {
        return Unchanged(state with { CurrentPage = ShellPage.Plugins });
    }

    /// <summary>
    /// 处理导航到 Updates 页意图。
    /// </summary>
    [MviReduce(typeof(AppShellIntent.ShowUpdates))]
    private MviReduceResult<AppShellState, UnitEffect> HandleShowUpdates(
        AppShellState state,
        AppShellIntent.ShowUpdates intent)
    {
        return Unchanged(state with { CurrentPage = ShellPage.Updates });
    }

    /// <summary>
    /// 处理导航到 Dashboard 页意图。
    /// </summary>
    [MviReduce(typeof(AppShellIntent.ShowDashboard))]
    private MviReduceResult<AppShellState, UnitEffect> HandleShowDashboard(
        AppShellState state,
        AppShellIntent.ShowDashboard intent)
    {
        return Unchanged(state with { CurrentPage = ShellPage.Dashboard });
    }

    /// <summary>
    /// 处理导航到 Settings 页意图。
    /// </summary>
    [MviReduce(typeof(AppShellIntent.ShowSettings))]
    private MviReduceResult<AppShellState, UnitEffect> HandleShowSettings(
        AppShellState state,
        AppShellIntent.ShowSettings intent)
    {
        return Unchanged(state with { CurrentPage = ShellPage.Settings });
    }

    /// <summary>
    /// 处理切换侧边栏折叠状态意图。
    /// </summary>
    [MviReduce(typeof(AppShellIntent.ToggleSidebar))]
    private MviReduceResult<AppShellState, UnitEffect> HandleToggleSidebar(
        AppShellState state,
        AppShellIntent.ToggleSidebar intent)
    {
        return Unchanged(state with { SidebarCollapsed = !state.SidebarCollapsed });
    }

    /// <summary>
    /// 处理 Runtime 生命周期投影变化回流意图（§14：仅投影字段，不保存 Runtime 业务状态）。
    /// </summary>
    [MviReduce(typeof(AppShellIntent.RuntimeIndicatorChanged))]
    private MviReduceResult<AppShellState, UnitEffect> HandleRuntimeIndicatorChanged(
        AppShellState state,
        AppShellIntent.RuntimeIndicatorChanged intent)
    {
        return Unchanged(state with { RuntimeIndicator = intent.Lifecycle });
    }

    /// <summary>
    /// 处理可用更新数投影变化回流意图（§14：仅投影字段，不保存 Updates 业务状态）。
    /// </summary>
    [MviReduce(typeof(AppShellIntent.UpdateBadgeChanged))]
    private MviReduceResult<AppShellState, UnitEffect> HandleUpdateBadgeChanged(
        AppShellState state,
        AppShellIntent.UpdateBadgeChanged intent)
    {
        return Unchanged(state with { UpdateBadge = intent.Count });
    }

    /// <summary>
    /// 处理 Runtime 进程/端口投影变化回流意图（§14：仅投影字段，状态栏 PID / Port 数据源）。
    /// </summary>
    [MviReduce(typeof(AppShellIntent.RuntimeEndpointChanged))]
    private MviReduceResult<AppShellState, UnitEffect> HandleRuntimeEndpointChanged(
        AppShellState state,
        AppShellIntent.RuntimeEndpointChanged intent)
    {
        return Unchanged(state with { RuntimeProcessId = intent.ProcessId, RuntimePort = intent.Port });
    }

    /// <summary>
    /// 处理当前 DSH 版本投影变化回流意图（§14：仅投影字段，runtime-mini 数据源）。
    /// </summary>
    [MviReduce(typeof(AppShellIntent.DshVersionChanged))]
    private MviReduceResult<AppShellState, UnitEffect> HandleDshVersionChanged(
        AppShellState state,
        AppShellIntent.DshVersionChanged intent)
    {
        return Unchanged(state with { DshVersion = intent.Version });
    }
}
