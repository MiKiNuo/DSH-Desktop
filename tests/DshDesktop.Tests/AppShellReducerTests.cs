using DshDesktop.Presentation.Avalonia.Features.AppShell;

namespace DshDesktop.Tests;

/// <summary>
/// AppShell 规约器测试（§14：壳只管理导航与折叠，无业务状态）。
/// </summary>
public sealed class AppShellReducerTests
{
    private readonly AppShellReducer _reducer = new();

    [Test]
    public async Task ShowRuntime_NavigatesToRuntime()
    {
        var result = _reducer.Reduce(AppShellState.Initial with { CurrentPage = ShellPage.Plugins }, new AppShellIntent.ShowRuntime());

        await Assert.That(result.State.CurrentPage).IsEqualTo(ShellPage.Runtime);
    }

    [Test]
    public async Task ShowDashboard_NavigatesToDashboard()
    {
        var result = _reducer.Reduce(AppShellState.Initial, new AppShellIntent.ShowDashboard());

        await Assert.That(result.State.CurrentPage).IsEqualTo(ShellPage.Dashboard);
    }

    [Test]
    public async Task ShowWorkbench_NavigatesToWorkbench()
    {
        var result = _reducer.Reduce(AppShellState.Initial, new AppShellIntent.ShowWorkbench());

        await Assert.That(result.State.CurrentPage).IsEqualTo(ShellPage.Workbench);
    }

    [Test]
    public async Task ShowDiagnostics_NavigatesToDiagnostics()
    {
        var result = _reducer.Reduce(AppShellState.Initial, new AppShellIntent.ShowDiagnostics());

        await Assert.That(result.State.CurrentPage).IsEqualTo(ShellPage.Diagnostics);
    }

    [Test]
    public async Task ShowPlugins_NavigatesToPlugins()
    {
        var result = _reducer.Reduce(AppShellState.Initial, new AppShellIntent.ShowPlugins());

        await Assert.That(result.State.CurrentPage).IsEqualTo(ShellPage.Plugins);
    }

    [Test]
    public async Task ShowUpdates_NavigatesToUpdates()
    {
        var result = _reducer.Reduce(AppShellState.Initial, new AppShellIntent.ShowUpdates());

        await Assert.That(result.State.CurrentPage).IsEqualTo(ShellPage.Updates);
    }

    [Test]
    public async Task ShowSettings_NavigatesToSettings()
    {
        var result = _reducer.Reduce(AppShellState.Initial, new AppShellIntent.ShowSettings());

        await Assert.That(result.State.CurrentPage).IsEqualTo(ShellPage.Settings);
    }

    [Test]
    public async Task ToggleSidebar_TogglesBothWays()
    {
        var collapsed = _reducer.Reduce(AppShellState.Initial, new AppShellIntent.ToggleSidebar());
        await Assert.That(collapsed.State.SidebarCollapsed).IsTrue();

        var expanded = _reducer.Reduce(collapsed.State, new AppShellIntent.ToggleSidebar());
        await Assert.That(expanded.State.SidebarCollapsed).IsFalse();
    }

    [Test]
    public async Task Navigation_NeverProducesEffects()
    {
        var result = _reducer.Reduce(AppShellState.Initial, new AppShellIntent.ShowSettings());

        await Assert.That(result.Effects.Count).IsEqualTo(0);
    }
}
