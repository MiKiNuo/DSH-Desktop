using DshDesktop.Application.Runtime;
using DshDesktop.Domain.Diagnostics;
using DshDesktop.Domain.Runtime;
using MiKiNuo.Mvi.Domain.MVI.Intent;

namespace DshDesktop.Presentation.Avalonia.Features.Dashboard;

/// <summary>
/// 表示 Dashboard 意图（Phase 8 Issue 03）。
/// </summary>
public abstract partial record DashboardIntent : IMviIntent
{
    /// <summary>
    /// 表示 Runtime 投影变化的回流意图（BindSiblingState 自 RuntimeStore，§11.2）。
    /// </summary>
    /// <param name="Lifecycle">最新生命周期。</param>
    /// <param name="Health">最新健康状态。</param>
    /// <param name="Port">实际监听端口。</param>
    /// <param name="StartupElapsed">本次启动耗时。</param>
    /// <param name="NodeVersion">Node 运行时版本投影（RuntimeState.Environment；未加载为 null）。Phase 8 评审 F8：替代组合根独立采集通道。</param>
    public sealed partial record RuntimeProjectionChanged(
        RuntimeLifecycle Lifecycle,
        RuntimeHealth Health,
        int? Port,
        TimeSpan? StartupElapsed,
        string? NodeVersion) : DashboardIntent;

    /// <summary>
    /// 表示进程指标采样回流意图（组合根自 ProcessMetricsMonitor，2s 节奏）。
    /// </summary>
    /// <param name="CpuPercent">CPU 百分比；首次采样无基线为 null。</param>
    /// <param name="WorkingSetBytes">工作集内存字节数。</param>
    public sealed partial record MetricsSampled(double? CpuPercent, long WorkingSetBytes) : DashboardIntent;

    /// <summary>
    /// 表示插件数投影变化的回流意图（BindSiblingState 自 PluginsStore，§11.2）。
    /// </summary>
    /// <param name="PluginCount">已安装插件数。</param>
    public sealed partial record PluginsProjectionChanged(int PluginCount) : DashboardIntent;

    /// <summary>
    /// 表示更新投影变化的回流意图（BindSiblingState 自 UpdatesStore，§11.2）。
    /// </summary>
    /// <param name="DshVersion">当前 DSH 版本；未知为 null。</param>
    /// <param name="UpdatablePluginCount">可更新插件数。</param>
    public sealed partial record UpdatesProjectionChanged(
        string? DshVersion,
        int UpdatablePluginCount) : DashboardIntent;

    /// <summary>
    /// 表示环境信息已加载回流意图（组合根启动时一次性输入：通道 / 上次启动耗时；
    /// Node 版本改经 RuntimeStore 投影，Phase 8 评审 F8）。
    /// </summary>
    /// <param name="DesktopChannel">Desktop 更新通道。</param>
    /// <param name="PreviousStartupElapsedMs">上次启动耗时（毫秒）；首次为 null。</param>
    public sealed partial record EnvironmentLoaded(
        string DesktopChannel,
        long? PreviousStartupElapsedMs) : DashboardIntent;

    /// <summary>
    /// 表示本次启动耗时已记录回流意图（组合根在 Runtime Ready 时写 config 前回流旧值）。
    /// </summary>
    /// <param name="PreviousMs">被覆盖前的上次启动耗时（毫秒）；首次为 null。</param>
    public sealed partial record StartupElapsedRecorded(long? PreviousMs) : DashboardIntent;

    /// <summary>
    /// 表示启动阶段计时已到达回流意图（组合根自 RuntimeSupervisor.LastStartupStageTimings）。
    /// </summary>
    /// <param name="Timings">阶段累计计时（单调不减）。</param>
    public sealed partial record TimelineReceived(IReadOnlyList<StartupStageTiming> Timings) : DashboardIntent;

    /// <summary>
    /// 表示诊断事件流投影变化的回流意图（BindSiblingState 自 DiagnosticsStore，§11.2；
    /// Reducer 负责过滤与截断）。
    /// </summary>
    /// <param name="Entries">Diagnostics Store 当前事件窗口（时间升序）。</param>
    public sealed partial record ActivityFeedChanged(IReadOnlyList<DiagnosticEvent> Entries) : DashboardIntent;

    /// <summary>
    /// 表示打开 DSH 工作台意图（hero 主按钮）。
    /// </summary>
    public sealed partial record OpenWorkbench : DashboardIntent;

    /// <summary>
    /// 表示查看启动日志意图（hero / timeline 卡"完整日志"按钮）。
    /// </summary>
    public sealed partial record OpenStartupLog : DashboardIntent;

    /// <summary>
    /// 表示打开运行环境页意图（hero 按钮）。
    /// </summary>
    public sealed partial record OpenRuntime : DashboardIntent;
}
