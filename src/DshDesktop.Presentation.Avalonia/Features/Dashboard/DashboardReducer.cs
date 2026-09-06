using MiKiNuo.Mvi.Application.MVI.Reducer;
using MiKiNuo.Mvi.Domain.DI;
using MiKiNuo.Mvi.Domain.MVI.Reducer;
using DshDesktop.Domain.Runtime;
using DshDesktop.Presentation.Avalonia.Features.AppShell;

namespace DshDesktop.Presentation.Avalonia.Features.Dashboard;

/// <summary>
/// 表示 Dashboard 规约器（Phase 8 Issue 03）。纯函数：只修改 State 或声明 Effect，禁止 IO（§9）。
/// </summary>
[MviFeature]
public sealed partial class DashboardReducer
    : MviReducerBase<DashboardState, DashboardIntent, DashboardEffect>
{
    /// <summary>
    /// 处理 Runtime 投影回流意图；非 Running 时清空健康度采样（进程已不可采样）。
    /// </summary>
    [MviReduce(typeof(DashboardIntent.RuntimeProjectionChanged))]
    private MviReduceResult<DashboardState, DashboardEffect> HandleRuntimeProjectionChanged(
        DashboardState state,
        DashboardIntent.RuntimeProjectionChanged intent)
    {
        bool running = intent.Lifecycle is RuntimeLifecycle.Running;
        return Unchanged(state with
        {
            Lifecycle = intent.Lifecycle,
            Health = intent.Health,
            Port = intent.Port,
            StartupElapsed = intent.StartupElapsed ?? state.StartupElapsed,
            NodeVersion = intent.NodeVersion,
            CpuPercent = running ? state.CpuPercent : null,
            MemoryBytes = running ? state.MemoryBytes : null,
        });
    }

    /// <summary>
    /// 处理进程指标采样回流意图（运行健康度卡 CPU / Memory）。
    /// </summary>
    [MviReduce(typeof(DashboardIntent.MetricsSampled))]
    private MviReduceResult<DashboardState, DashboardEffect> HandleMetricsSampled(
        DashboardState state,
        DashboardIntent.MetricsSampled intent)
    {
        return Unchanged(state with
        {
            CpuPercent = intent.CpuPercent,
            MemoryBytes = intent.WorkingSetBytes,
        });
    }

    /// <summary>
    /// 处理插件数投影回流意图。
    /// </summary>
    [MviReduce(typeof(DashboardIntent.PluginsProjectionChanged))]
    private MviReduceResult<DashboardState, DashboardEffect> HandlePluginsProjectionChanged(
        DashboardState state,
        DashboardIntent.PluginsProjectionChanged intent)
    {
        return Unchanged(state with { PluginCount = intent.PluginCount });
    }

    /// <summary>
    /// 处理更新投影回流意图（DSH 版本 + 可更新插件数）。
    /// </summary>
    [MviReduce(typeof(DashboardIntent.UpdatesProjectionChanged))]
    private MviReduceResult<DashboardState, DashboardEffect> HandleUpdatesProjectionChanged(
        DashboardState state,
        DashboardIntent.UpdatesProjectionChanged intent)
    {
        return Unchanged(state with
        {
            DshVersion = intent.DshVersion,
            UpdatablePluginCount = intent.UpdatablePluginCount,
        });
    }

    /// <summary>
    /// 处理环境信息已加载回流意图。
    /// </summary>
    [MviReduce(typeof(DashboardIntent.EnvironmentLoaded))]
    private MviReduceResult<DashboardState, DashboardEffect> HandleEnvironmentLoaded(
        DashboardState state,
        DashboardIntent.EnvironmentLoaded intent)
    {
        return Unchanged(state with
        {
            DesktopChannel = intent.DesktopChannel,
            PreviousStartupElapsedMs = intent.PreviousStartupElapsedMs,
        });
    }

    /// <summary>
    /// 处理启动耗时已记录回流意图（config 持久化前的旧值成为"上次"基准）。
    /// </summary>
    [MviReduce(typeof(DashboardIntent.StartupElapsedRecorded))]
    private MviReduceResult<DashboardState, DashboardEffect> HandleStartupElapsedRecorded(
        DashboardState state,
        DashboardIntent.StartupElapsedRecorded intent)
    {
        return Unchanged(state with { PreviousStartupElapsedMs = intent.PreviousMs });
    }

    /// <summary>
    /// 处理启动阶段计时回流意图（timeline 卡数据源）。
    /// </summary>
    [MviReduce(typeof(DashboardIntent.TimelineReceived))]
    private MviReduceResult<DashboardState, DashboardEffect> HandleTimelineReceived(
        DashboardState state,
        DashboardIntent.TimelineReceived intent)
    {
        return Unchanged(state with { StageTimings = intent.Timings });
    }

    /// <summary>
    /// 处理诊断事件流投影回流意图：过滤非活动事件并截断到最新 N 条。
    /// </summary>
    [MviReduce(typeof(DashboardIntent.ActivityFeedChanged))]
    private MviReduceResult<DashboardState, DashboardEffect> HandleActivityFeedChanged(
        DashboardState state,
        DashboardIntent.ActivityFeedChanged intent)
    {
        IReadOnlyList<DshDesktop.Domain.Diagnostics.DiagnosticEvent> projected =
            ActivityFeed.Project(intent.Entries);
        if (projected.Count == state.Activities.Count
            && projected.SequenceEqual(state.Activities))
        {
            return Unchanged(state);
        }

        return Unchanged(state with { Activities = projected });
    }

    /// <summary>
    /// 处理打开 DSH 工作台意图：只声明导航副作用，自身状态不变。
    /// </summary>
    [MviReduce(typeof(DashboardIntent.OpenWorkbench))]
    private MviReduceResult<DashboardState, DashboardEffect> HandleOpenWorkbench(
        DashboardState state,
        DashboardIntent.OpenWorkbench intent)
    {
        return WithEffect(state, new DashboardEffect.Navigate(ShellPage.Workbench));
    }

    /// <summary>
    /// 处理查看启动日志意图：导航到诊断中心。
    /// </summary>
    [MviReduce(typeof(DashboardIntent.OpenStartupLog))]
    private MviReduceResult<DashboardState, DashboardEffect> HandleOpenStartupLog(
        DashboardState state,
        DashboardIntent.OpenStartupLog intent)
    {
        return WithEffect(state, new DashboardEffect.Navigate(ShellPage.Diagnostics));
    }

    /// <summary>
    /// 处理打开运行环境页意图。
    /// </summary>
    [MviReduce(typeof(DashboardIntent.OpenRuntime))]
    private MviReduceResult<DashboardState, DashboardEffect> HandleOpenRuntime(
        DashboardState state,
        DashboardIntent.OpenRuntime intent)
    {
        return WithEffect(state, new DashboardEffect.Navigate(ShellPage.Runtime));
    }
}
