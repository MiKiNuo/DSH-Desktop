using DshDesktop.Domain.Runtime;
using DshDesktop.Presentation.Avalonia.Features.AppShell;

namespace DshDesktop.Tests;

/// <summary>
/// AppShell 规约器测试（§14：壳只管理导航，无业务状态）。
/// </summary>
public sealed class AppShellReducerTests
{
    private readonly AppShellReducer _reducer = new();

    [Test]
    public async Task Initial_DefaultsToWorkbench()
    {
        // 顶部导航改造：默认页由概览（Dashboard）改为工作台（DSH Web UI）。
        await Assert.That(AppShellState.Initial.CurrentPage).IsEqualTo(ShellPage.Workbench);
        await Assert.That(AppShellState.Initial.RuntimeProcessId).IsNull();
        await Assert.That(AppShellState.Initial.RuntimePort).IsNull();
        await Assert.That(AppShellState.Initial.DshVersion).IsNull();
    }

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
    public async Task Navigation_NeverProducesEffects()
    {
        var result = _reducer.Reduce(AppShellState.Initial, new AppShellIntent.ShowSettings());

        await Assert.That(result.Effects.Count).IsEqualTo(0);
    }

    [Test]
    public async Task RuntimeIndicatorChanged_ProjectsLifecycle()
    {
        var result = _reducer.Reduce(AppShellState.Initial, new AppShellIntent.RuntimeIndicatorChanged(RuntimeLifecycle.Running));

        await Assert.That(result.State.RuntimeIndicator).IsEqualTo(RuntimeLifecycle.Running);
        await Assert.That(result.Effects.Count).IsEqualTo(0);
    }

    [Test]
    public async Task UpdateBadgeChanged_ProjectsCount()
    {
        var result = _reducer.Reduce(AppShellState.Initial, new AppShellIntent.UpdateBadgeChanged(4));

        await Assert.That(result.State.UpdateBadge).IsEqualTo(4);
        await Assert.That(result.Effects.Count).IsEqualTo(0);
    }

    [Test]
    public async Task RuntimeEndpointChanged_ProjectsProcessIdAndPort()
    {
        // Phase 8 Issue 02：状态栏 PID / Port 投影（BindSiblingState 自 RuntimeStore，§11.2）。
        var result = _reducer.Reduce(
            AppShellState.Initial,
            new AppShellIntent.RuntimeEndpointChanged(16428, 3080));

        await Assert.That(result.State.RuntimeProcessId).IsEqualTo(16428);
        await Assert.That(result.State.RuntimePort).IsEqualTo(3080);
        await Assert.That(result.Effects.Count).IsEqualTo(0);
    }

    [Test]
    public async Task RuntimeEndpointChanged_AllowsNull_WhenStopped()
    {
        var result = _reducer.Reduce(
            AppShellState.Initial,
            new AppShellIntent.RuntimeEndpointChanged(null, null));

        await Assert.That(result.State.RuntimeProcessId).IsNull();
        await Assert.That(result.State.RuntimePort).IsNull();
    }

    [Test]
    public async Task DshVersionChanged_ProjectsVersion()
    {
        // 状态栏 DSH 版本段的投影（自 UpdatesStore.CurrentDshVersion）。
        var result = _reducer.Reduce(
            AppShellState.Initial,
            new AppShellIntent.DshVersionChanged("0.1.0-rc.12"));

        await Assert.That(result.State.DshVersion).IsEqualTo("0.1.0-rc.12");
        await Assert.That(result.Effects.Count).IsEqualTo(0);
    }
}
