using DshDesktop.Application.Runtime;
using DshDesktop.Domain.Diagnostics;
using DshDesktop.Domain.Runtime;
using DshDesktop.Presentation.Avalonia.Features.Diagnostics;
using DshDesktop.Presentation.Avalonia.Features.Plugins;
using DshDesktop.Presentation.Avalonia.Features.Runtime;
using DshDesktop.Presentation.Avalonia.Features.Updates;
using MiKiNuo.Mvi.Application.MVI.Command;
using MiKiNuo.Mvi.Application.MVI.Store;
using MiKiNuo.Mvi.Application.MVI.Threading;
using MiKiNuo.Mvi.Application.MVI.ViewModel;
using MiKiNuo.Mvi.Domain.MVI.Binding;

namespace DshDesktop.Presentation.Avalonia.Features.Dashboard;

/// <summary>
/// 表示活动 feed 的一行（View 展示模型；Title = 事件名，TimeText = 相对时间）。
/// </summary>
/// <param name="Title">事件名。</param>
/// <param name="TimeText">相对时间文本。</param>
public sealed record DashboardActivityItem(string Title, string TimeText);

/// <summary>
/// 表示 Dashboard ViewModel（Phase 8 Issue 03：独立 MVI 三元组 + BindSiblingState 投影）。
/// </summary>
/// <remarks>
/// Runtime / Updates / Plugins / Diagnostics 状态经
/// <see cref="MviViewModelBase{TState, TIntent, TEffect}.BindSiblingState"/> 只读投影（§11.2），
/// 变化时回流意图进入自身 Store；派生投影（文案 / 条形 / 活动项）跟随状态属性联动刷新。
/// </remarks>
public sealed partial class DashboardViewModel
    : MviViewModelBase<DashboardState, DashboardIntent, DashboardEffect>
{
    /// <summary>内存条形的视觉参考刻度（4 GiB；无自然上限，仅条形比例用）。</summary>
    private const double MemoryBarReferenceBytes = 4.0 * 1024 * 1024 * 1024;

    /// <summary>启动耗时条形的视觉参考刻度（10s；仅为条形比例，非超时语义）。</summary>
    private static readonly TimeSpan StartupBarReference = TimeSpan.FromSeconds(10);

    /// <summary>插件数条形的视觉参考刻度（10 个；仅为条形比例）。</summary>
    private const int PluginBarReferenceCount = 10;

    /// <summary>
    /// 初始化 Dashboard ViewModel。
    /// </summary>
    /// <param name="store">Dashboard 状态存储。</param>
    /// <param name="runtimeStore">Runtime 状态存储（兄弟 Store，只读订阅）。</param>
    /// <param name="updatesStore">Updates 状态存储（兄弟 Store，只读订阅）。</param>
    /// <param name="pluginsStore">Plugins 状态存储（兄弟 Store，只读订阅）。</param>
    /// <param name="diagnosticsStore">Diagnostics 状态存储（兄弟 Store，只读订阅）。</param>
    /// <param name="uiDispatcher">UI 调度器。</param>
    public DashboardViewModel(
        IMviStore<DashboardState, DashboardIntent, DashboardEffect> store,
        IMviStore<RuntimeState, RuntimeIntent, RuntimeEffect> runtimeStore,
        IMviStore<UpdatesState, UpdatesIntent, UpdatesEffect> updatesStore,
        IMviStore<PluginsState, PluginsIntent, PluginsEffect> pluginsStore,
        IMviStore<DiagnosticsState, DiagnosticsIntent, DiagnosticsEffect> diagnosticsStore,
        IMviUiDispatcher? uiDispatcher = null)
        : base(store, uiDispatcher)
    {
        ArgumentNullException.ThrowIfNull(runtimeStore);
        ArgumentNullException.ThrowIfNull(updatesStore);
        ArgumentNullException.ThrowIfNull(pluginsStore);
        ArgumentNullException.ThrowIfNull(diagnosticsStore);

        _ = BindSiblingState(runtimeStore, ApplyRuntimeState);
        _ = BindSiblingState(updatesStore, ApplyUpdatesState);
        _ = BindSiblingState(pluginsStore, ApplyPluginsState);
        _ = BindSiblingState(diagnosticsStore, ApplyDiagnosticsState);
        ApplyRuntimeState(runtimeStore.CurrentState);
        ApplyUpdatesState(updatesStore.CurrentState);
        ApplyPluginsState(pluginsStore.CurrentState);
        ApplyDiagnosticsState(diagnosticsStore.CurrentState);

        // 派生投影（hero 文案 / 条形值 / footer / timeline 行 / 活动项）跟随状态投影联动刷新。
        PropertyChanged += (_, args) =>
        {
            switch (args.PropertyName)
            {
                case nameof(Lifecycle):
                    OnPropertyChanged(nameof(HeroTitle));
                    OnPropertyChanged(nameof(HeroSubtitle));
                    break;
                case nameof(Port):
                    OnPropertyChanged(nameof(HeroSubtitle));
                    break;
                case nameof(Health):
                    OnPropertyChanged(nameof(HealthText));
                    break;
                case nameof(CpuPercent):
                    OnPropertyChanged(nameof(CpuText));
                    OnPropertyChanged(nameof(CpuBarValue));
                    break;
                case nameof(MemoryBytes):
                    OnPropertyChanged(nameof(MemoryText));
                    OnPropertyChanged(nameof(MemoryBarValue));
                    break;
                case nameof(StartupElapsed):
                    OnPropertyChanged(nameof(StartupElapsedText));
                    OnPropertyChanged(nameof(StartupBarValue));
                    OnPropertyChanged(nameof(StartupComparisonText));
                    break;
                case nameof(PreviousStartupElapsedMs):
                    OnPropertyChanged(nameof(StartupComparisonText));
                    break;
                case nameof(PluginCount):
                    OnPropertyChanged(nameof(PluginBarValue));
                    break;
                case nameof(UpdatablePluginCount):
                    OnPropertyChanged(nameof(PluginsFooterText));
                    break;
                case nameof(NodeVersion):
                    OnPropertyChanged(nameof(NodeVersionFooter));
                    break;
                case nameof(DesktopChannel):
                    OnPropertyChanged(nameof(DesktopChannelFooter));
                    break;
                case nameof(StageTimings):
                    OnPropertyChanged(nameof(TimelineRows));
                    OnPropertyChanged(nameof(HasTimeline));
                    break;
                case nameof(Activities):
                    OnPropertyChanged(nameof(ActivityItems));
                    OnPropertyChanged(nameof(HasActivities));
                    break;
            }
        };
    }

    /// <summary>
    /// 获取 Runtime 生命周期投影。
    /// </summary>
    [MviBind(nameof(DashboardState.Lifecycle), BindingMode = MviBindingMode.OneWay)]
    public partial RuntimeLifecycle Lifecycle { get; private set; }

    /// <summary>
    /// 获取健康状态投影。
    /// </summary>
    [MviBind(nameof(DashboardState.Health), BindingMode = MviBindingMode.OneWay)]
    public partial RuntimeHealth Health { get; private set; }

    /// <summary>
    /// 获取实际监听端口投影。
    /// </summary>
    [MviBind(nameof(DashboardState.Port), BindingMode = MviBindingMode.OneWay)]
    public partial int? Port { get; private set; }

    /// <summary>
    /// 获取本次启动耗时投影。
    /// </summary>
    [MviBind(nameof(DashboardState.StartupElapsed), BindingMode = MviBindingMode.OneWay)]
    public partial TimeSpan? StartupElapsed { get; private set; }

    /// <summary>
    /// 获取 CPU 百分比投影（无基线或未运行为 null）。
    /// </summary>
    [MviBind(nameof(DashboardState.CpuPercent), BindingMode = MviBindingMode.OneWay)]
    public partial double? CpuPercent { get; private set; }

    /// <summary>
    /// 获取工作集内存投影（字节；未运行为 null）。
    /// </summary>
    [MviBind(nameof(DashboardState.MemoryBytes), BindingMode = MviBindingMode.OneWay)]
    public partial long? MemoryBytes { get; private set; }

    /// <summary>
    /// 获取已安装插件数投影。
    /// </summary>
    [MviBind(nameof(DashboardState.PluginCount), BindingMode = MviBindingMode.OneWay)]
    public partial int PluginCount { get; private set; }

    /// <summary>
    /// 获取可更新插件数投影。
    /// </summary>
    [MviBind(nameof(DashboardState.UpdatablePluginCount), BindingMode = MviBindingMode.OneWay)]
    public partial int UpdatablePluginCount { get; private set; }

    /// <summary>
    /// 获取当前 DSH 版本投影。
    /// </summary>
    [MviBind(nameof(DashboardState.DshVersion), BindingMode = MviBindingMode.OneWay)]
    public partial string? DshVersion { get; private set; }

    /// <summary>
    /// 获取 Node 运行时版本。
    /// </summary>
    [MviBind(nameof(DashboardState.NodeVersion), BindingMode = MviBindingMode.OneWay)]
    public partial string? NodeVersion { get; private set; }

    /// <summary>
    /// 获取 Desktop 更新通道。
    /// </summary>
    [MviBind(nameof(DashboardState.DesktopChannel), BindingMode = MviBindingMode.OneWay)]
    public partial string DesktopChannel { get; private set; }

    /// <summary>
    /// 获取上次启动耗时（毫秒；首次为 null）。
    /// </summary>
    [MviBind(nameof(DashboardState.PreviousStartupElapsedMs), BindingMode = MviBindingMode.OneWay)]
    public partial long? PreviousStartupElapsedMs { get; private set; }

    /// <summary>
    /// 获取最近一次启动的阶段累计计时。
    /// </summary>
    [MviBind(nameof(DashboardState.StageTimings), BindingMode = MviBindingMode.OneWay)]
    public partial IReadOnlyList<StartupStageTiming> StageTimings { get; private set; }

    /// <summary>
    /// 获取最近活动事件（过滤截断后，时间升序）。
    /// </summary>
    [MviBind(nameof(DashboardState.Activities), BindingMode = MviBindingMode.OneWay)]
    public partial IReadOnlyList<DiagnosticEvent> Activities { get; private set; }

    // ===== 派生投影（纯函数推导，见 DashboardText / DashboardTimeline / ActivityFeed） =====

    /// <summary>获取 hero 标题。</summary>
    public string HeroTitle => DashboardText.HeroTitle(Lifecycle);

    /// <summary>获取 hero 副文案。</summary>
    public string HeroSubtitle => DashboardText.HeroSubtitle(Lifecycle, Port);

    /// <summary>获取健康状态文本。</summary>
    public string HealthText => DashboardText.HealthText(Health);

    /// <summary>获取 CPU 文本。</summary>
    public string CpuText => DashboardText.FormatCpu(CpuPercent);

    /// <summary>获取 CPU 条形值（0-100）。</summary>
    public double CpuBarValue => CpuPercent ?? 0;

    /// <summary>获取内存文本。</summary>
    public string MemoryText => MemoryBytes is { } bytes ? DashboardText.FormatMemoryBytes(bytes) : "—";

    /// <summary>获取内存条形值（4 GiB 视觉参考刻度，0-100）。</summary>
    public double MemoryBarValue => MemoryBytes is { } bytes
        ? Math.Clamp(bytes / MemoryBarReferenceBytes * 100.0, 0, 100)
        : 0;

    /// <summary>获取启动耗时文本（最近启动统计卡与健康度卡共用）。</summary>
    public string StartupElapsedText => StartupElapsed is { } elapsed
        ? AppShell.TrayTooltipText.FormatStartupElapsed(elapsed)
        : "—";

    /// <summary>获取启动耗时条形值（10s 视觉参考刻度，0-100）。</summary>
    public double StartupBarValue => StartupElapsed is { } elapsed
        ? Math.Clamp(elapsed / StartupBarReference * 100.0, 0, 100)
        : 0;

    /// <summary>获取插件数条形值（10 个视觉参考刻度，0-100）。</summary>
    public double PluginBarValue =>
        Math.Clamp(PluginCount / (double)PluginBarReferenceCount * 100.0, 0, 100);

    /// <summary>获取启动耗时对比文本（统计卡 footer）。</summary>
    public string StartupComparisonText =>
        DashboardText.FormatStartupComparison(StartupElapsed, PreviousStartupElapsedMs);

    /// <summary>获取插件统计卡 footer。</summary>
    public string PluginsFooterText => DashboardText.PluginsFooter(UpdatablePluginCount);

    /// <summary>获取 Node 版本 footer。</summary>
    public string NodeVersionFooter => NodeVersion is { } version ? $"Node {version}" : "—";

    /// <summary>获取 Desktop 通道 footer。</summary>
    public string DesktopChannelFooter => DashboardText.ChannelFooter(DesktopChannel);

    /// <summary>获取启动 timeline 行（分段耗时 + 相对比例宽度）。</summary>
    public IReadOnlyList<DashboardTimelineRow> TimelineRows =>
        StageTimings is null ? [] : DashboardTimeline.Build(StageTimings);

    /// <summary>获取是否有 timeline 数据（首次启动前为空态）。</summary>
    public bool HasTimeline => StageTimings is { Count: > 0 };

    /// <summary>获取是否有活动数据（空态占位）。</summary>
    public bool HasActivities => Activities is { Count: > 0 };

    /// <summary>获取活动 feed 展示项（最新在前）。</summary>
    public IReadOnlyList<DashboardActivityItem> ActivityItems
    {
        get
        {
            if (Activities is null)
            {
                return [];
            }

            DateTimeOffset now = DateTimeOffset.Now;
            return Activities
                .Reverse()
                .Select(e => new DashboardActivityItem(
                    e.Message,
                    DashboardText.FormatRelativeTime(now, e.Timestamp)))
                .ToArray();
        }
    }

    /// <summary>获取 Desktop 版本（编译期常量，统计卡值）。</summary>
    public string DesktopVersion => DesktopInfo.Version;

    /// <summary>
    /// 获取打开 DSH 工作台命令。
    /// </summary>
    [MviCommand(typeof(DashboardIntent.OpenWorkbench))]
    public partial IMviAsyncCommand OpenWorkbenchCommand { get; private set; }

    /// <summary>
    /// 获取查看启动日志命令。
    /// </summary>
    [MviCommand(typeof(DashboardIntent.OpenStartupLog))]
    public partial IMviAsyncCommand OpenStartupLogCommand { get; private set; }

    /// <summary>
    /// 获取打开运行环境页命令。
    /// </summary>
    [MviCommand(typeof(DashboardIntent.OpenRuntime))]
    public partial IMviAsyncCommand OpenRuntimeCommand { get; private set; }

    private void ApplyRuntimeState(RuntimeState runtimeState)
    {
        DashboardState current = Store.CurrentState;
        if (runtimeState.Lifecycle != current.Lifecycle
            || runtimeState.Health != current.Health
            || runtimeState.Port != current.Port
            || runtimeState.StartupElapsed != current.StartupElapsed
            || runtimeState.Environment?.NodeVersion != current.NodeVersion)
        {
            _ = DispatchAsync(new DashboardIntent.RuntimeProjectionChanged(
                runtimeState.Lifecycle,
                runtimeState.Health,
                runtimeState.Port,
                runtimeState.StartupElapsed,
                runtimeState.Environment?.NodeVersion));
        }
    }

    private void ApplyUpdatesState(UpdatesState updatesState)
    {
        DashboardState current = Store.CurrentState;
        if (updatesState.CurrentDshVersion != current.DshVersion
            || updatesState.PluginUpdates.Count != current.UpdatablePluginCount)
        {
            _ = DispatchAsync(new DashboardIntent.UpdatesProjectionChanged(
                updatesState.CurrentDshVersion,
                updatesState.PluginUpdates.Count));
        }
    }

    private void ApplyPluginsState(PluginsState pluginsState)
    {
        if (pluginsState.Plugins.Count != Store.CurrentState.PluginCount)
        {
            _ = DispatchAsync(new DashboardIntent.PluginsProjectionChanged(pluginsState.Plugins.Count));
        }
    }

    private void ApplyDiagnosticsState(DiagnosticsState diagnosticsState)
    {
        _ = DispatchAsync(new DashboardIntent.ActivityFeedChanged(diagnosticsState.Entries));
    }
}
