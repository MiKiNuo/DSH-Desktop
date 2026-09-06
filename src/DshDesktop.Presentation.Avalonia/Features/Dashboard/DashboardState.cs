using DshDesktop.Application.Runtime;
using DshDesktop.Domain.Diagnostics;
using DshDesktop.Domain.Runtime;
using MiKiNuo.Mvi.Domain.MVI.State;

namespace DshDesktop.Presentation.Avalonia.Features.Dashboard;

/// <summary>
/// 表示 Dashboard 状态（Phase 8 Issue 03：独立 MVI 三元组，消解 Phase 5 Q9-A 偏离——
/// 旧实现直接复用 RuntimeViewModel 直显）。
/// 全部为只读投影 / 派生输入：Runtime、Updates、Plugins、Diagnostics 业务状态本体仍在各自 Store，
/// 本 State 只保存经 BindSiblingState 回流的投影副本与组合根本地输入（采样 / 环境信息）。
/// </summary>
/// <param name="Lifecycle">Runtime 生命周期投影（hero 就绪行）。</param>
/// <param name="Health">健康状态投影（运行健康度卡状态点）。</param>
/// <param name="Port">实际监听端口投影（hero 副文案地址；未运行为 null）。</param>
/// <param name="StartupElapsed">本次启动耗时投影（健康度卡 + 最近启动统计卡）。</param>
/// <param name="CpuPercent">DSH 进程 CPU 占用投影（2s 采样；无基线或未运行为 null）。</param>
/// <param name="MemoryBytes">DSH 进程工作集内存投影（2s 采样；未运行为 null）。</param>
/// <param name="PluginCount">已安装插件数投影（Plugins Store）。</param>
/// <param name="UpdatablePluginCount">可更新插件数投影（Updates Store.PluginUpdates）。</param>
/// <param name="DshVersion">当前 DSH 版本投影（Updates Store.CurrentDshVersion；未知为 null）。</param>
/// <param name="NodeVersion">Node 运行时版本投影（RuntimeState.Environment 经 BindSiblingState；未知为 null）。</param>
/// <param name="DesktopChannel">Desktop 更新通道（统计卡 footer）。</param>
/// <param name="PreviousStartupElapsedMs">上次启动耗时（config 持久化；首次为 null）。</param>
/// <param name="StageTimings">最近一次启动的阶段累计计时（§46 结构化副本，timeline 卡数据源）。</param>
/// <param name="Activities">最近活动 feed（过滤 + 截断后的最新 N 条，时间升序）。</param>
public sealed record DashboardState(
    RuntimeLifecycle Lifecycle,
    RuntimeHealth Health,
    int? Port,
    TimeSpan? StartupElapsed,
    double? CpuPercent,
    long? MemoryBytes,
    int PluginCount,
    int UpdatablePluginCount,
    string? DshVersion,
    string? NodeVersion,
    string DesktopChannel,
    long? PreviousStartupElapsedMs,
    IReadOnlyList<StartupStageTiming> StageTimings,
    IReadOnlyList<DiagnosticEvent> Activities) : IMviState
{
    /// <summary>
    /// 获取初始状态。
    /// </summary>
    public static DashboardState Initial { get; } = new(
        RuntimeLifecycle.Stopped,
        RuntimeHealth.Unknown,
        null, null, null, null,
        0, 0, null, null,
        "stable", null,
        System.Array.Empty<StartupStageTiming>(),
        System.Array.Empty<DiagnosticEvent>());
}
