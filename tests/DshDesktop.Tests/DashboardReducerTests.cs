using DshDesktop.Application.Runtime;
using DshDesktop.Domain.Diagnostics;
using DshDesktop.Domain.Runtime;
using DshDesktop.Presentation.Avalonia.Features.AppShell;
using DshDesktop.Presentation.Avalonia.Features.Dashboard;

namespace DshDesktop.Tests;

/// <summary>
/// Dashboard 规约器测试（Phase 8 Issue 03）：纯函数，投影回流 / 采样 /  timeline / 活动截断 / 导航 Effect。
/// </summary>
public sealed class DashboardReducerTests
{
    private readonly DashboardReducer _reducer = new();

    [Test]
    public async Task RuntimeProjectionChanged_UpdatesHeroFields()
    {
        var result = _reducer.Reduce(
            DashboardState.Initial,
            new DashboardIntent.RuntimeProjectionChanged(
                RuntimeLifecycle.Running, RuntimeHealth.Healthy, 3080, TimeSpan.FromMilliseconds(1820), "24.9.0"));

        await Assert.That(result.State.Lifecycle).IsEqualTo(RuntimeLifecycle.Running);
        await Assert.That(result.State.Health).IsEqualTo(RuntimeHealth.Healthy);
        await Assert.That(result.State.Port).IsEqualTo(3080);
        await Assert.That(result.State.StartupElapsed).IsEqualTo(TimeSpan.FromMilliseconds(1820));
        await Assert.That(result.State.NodeVersion).IsEqualTo("24.9.0"); // F8：Node 版本走 Runtime 投影
        await Assert.That(result.Effects.Count).IsEqualTo(0);
    }

    [Test]
    public async Task RuntimeProjectionChanged_NotRunning_ClearsMetrics()
    {
        DashboardState running = _reducer.Reduce(
            DashboardState.Initial,
            new DashboardIntent.RuntimeProjectionChanged(
                RuntimeLifecycle.Running, RuntimeHealth.Healthy, 3080, null, null)).State;
        running = _reducer.Reduce(running, new DashboardIntent.MetricsSampled(18.5, 412L * 1024 * 1024)).State;

        var result = _reducer.Reduce(
            running,
            new DashboardIntent.RuntimeProjectionChanged(
                RuntimeLifecycle.Stopped, RuntimeHealth.Unknown, null, null, null));

        await Assert.That(result.State.CpuPercent).IsNull();
        await Assert.That(result.State.MemoryBytes).IsNull();
        await Assert.That(result.State.Port).IsNull();
    }

    [Test]
    public async Task MetricsSampled_ProjectsHealthMetrics()
    {
        var result = _reducer.Reduce(
            DashboardState.Initial,
            new DashboardIntent.MetricsSampled(18.5, 412L * 1024 * 1024));

        await Assert.That(result.State.CpuPercent).IsEqualTo(18.5);
        await Assert.That(result.State.MemoryBytes).IsEqualTo(412L * 1024 * 1024);
    }

    [Test]
    public async Task MetricsSampled_FirstSample_CpuNull()
    {
        var result = _reducer.Reduce(
            DashboardState.Initial,
            new DashboardIntent.MetricsSampled(null, 100));

        await Assert.That(result.State.CpuPercent).IsNull();
        await Assert.That(result.State.MemoryBytes).IsEqualTo(100);
    }

    [Test]
    public async Task ActivityFeedChanged_FiltersAndTrimsToNewestN()
    {
        var baseTime = new DateTimeOffset(2026, 9, 5, 10, 0, 0, TimeSpan.FromHours(8));
        List<DiagnosticEvent> entries = [];
        for (int i = 0; i < ActivityFeed.MaxEntries + 3; i++)
        {
            entries.Add(new DiagnosticEvent(
                baseTime.AddMinutes(i), DiagnosticSource.App, DiagnosticLevel.Info, $"Event.{i}"));
        }

        entries.Add(new DiagnosticEvent(
            baseTime.AddMinutes(99), DiagnosticSource.DshStdout, DiagnosticLevel.Info, "raw stdout"));

        var result = _reducer.Reduce(DashboardState.Initial, new DashboardIntent.ActivityFeedChanged(entries));

        await Assert.That(result.State.Activities.Count).IsEqualTo(ActivityFeed.MaxEntries);
        await Assert.That(result.State.Activities[0].Message).IsEqualTo("Event.3");
        await Assert.That(result.State.Activities[^1].Message).IsEqualTo($"Event.{ActivityFeed.MaxEntries + 2}");
    }

    [Test]
    public async Task TimelineReceived_StoresStageTimings()
    {
        StartupStageTiming[] timings =
        [
            new(RuntimeStartupSignal.Spawning, TimeSpan.FromMilliseconds(100)),
            new(RuntimeStartupSignal.Ready, TimeSpan.FromMilliseconds(1600)),
        ];

        var result = _reducer.Reduce(DashboardState.Initial, new DashboardIntent.TimelineReceived(timings));

        await Assert.That(result.State.StageTimings.Count).IsEqualTo(2);
    }

    [Test]
    public async Task StartupElapsedRecorded_StoresPrevious()
    {
        var result = _reducer.Reduce(DashboardState.Initial, new DashboardIntent.StartupElapsedRecorded(2130));

        await Assert.That(result.State.PreviousStartupElapsedMs).IsEqualTo(2130);
    }

    [Test]
    public async Task EnvironmentLoaded_StoresChannelAndPrevious()
    {
        // Phase 8 评审 F8：Node 版本不再走 EnvironmentLoaded（改经 RuntimeStore 投影）。
        var result = _reducer.Reduce(
            DashboardState.Initial,
            new DashboardIntent.EnvironmentLoaded("stable", 2130));

        await Assert.That(result.State.DesktopChannel).IsEqualTo("stable");
        await Assert.That(result.State.PreviousStartupElapsedMs).IsEqualTo(2130);
        await Assert.That(result.State.NodeVersion).IsNull();
    }

    [Test]
    public async Task PluginAndUpdatesProjections_UpdateStatCards()
    {
        DashboardState state = _reducer.Reduce(
            DashboardState.Initial, new DashboardIntent.PluginsProjectionChanged(6)).State;
        var result = _reducer.Reduce(
            state, new DashboardIntent.UpdatesProjectionChanged("0.1.0-rc.12", 1));

        await Assert.That(result.State.PluginCount).IsEqualTo(6);
        await Assert.That(result.State.DshVersion).IsEqualTo("0.1.0-rc.12");
        await Assert.That(result.State.UpdatablePluginCount).IsEqualTo(1);
    }

    [Test]
    public async Task NavigationIntents_DeclareNavigateEffects()
    {
        var workbench = _reducer.Reduce(DashboardState.Initial, new DashboardIntent.OpenWorkbench());
        var log = _reducer.Reduce(DashboardState.Initial, new DashboardIntent.OpenStartupLog());
        var runtime = _reducer.Reduce(DashboardState.Initial, new DashboardIntent.OpenRuntime());

        await Assert.That(workbench.Effects[0] is DashboardEffect.Navigate { Page: ShellPage.Workbench }).IsTrue();
        await Assert.That(log.Effects[0] is DashboardEffect.Navigate { Page: ShellPage.Diagnostics }).IsTrue();
        await Assert.That(runtime.Effects[0] is DashboardEffect.Navigate { Page: ShellPage.Runtime }).IsTrue();

        // 导航不改变 Dashboard 自身状态。
        await Assert.That(workbench.State).IsEqualTo(DashboardState.Initial);
    }
}
