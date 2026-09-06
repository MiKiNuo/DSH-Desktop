using DshDesktop.Domain.Runtime;
using MiKiNuo.Mvi.Domain.MVI.State;

namespace DshDesktop.Presentation.Avalonia.Features.AppShell;

/// <summary>
/// 表示应用壳状态（架构文档 §14：只管理 Desktop Shell，不保存业务数据）。
/// </summary>
/// <param name="CurrentPage">当前页面。</param>
/// <param name="SidebarCollapsed">侧边栏是否折叠。</param>
/// <param name="RuntimeIndicator">Runtime 生命周期投影（BindSiblingState 自 RuntimeStore，§11.2）。</param>
/// <param name="UpdateBadge">可用更新数投影（BindSiblingState 自 UpdatesStore，§11.2；0 表示无可用更新）。</param>
/// <param name="RuntimeProcessId">DSH 进程 ID 投影（状态栏 PID；未运行为 null，Phase 8 Issue 02）。</param>
/// <param name="RuntimePort">实际监听端口投影（状态栏 Port；未运行为 null，Phase 8 Issue 02）。</param>
/// <param name="DshVersion">当前 DSH 版本投影（侧栏 runtime-mini；自 UpdatesStore.CurrentDshVersion，未知为 null）。</param>
public sealed record AppShellState(
    ShellPage CurrentPage,
    bool SidebarCollapsed,
    RuntimeLifecycle RuntimeIndicator,
    int UpdateBadge,
    int? RuntimeProcessId,
    int? RuntimePort,
    string? DshVersion) : IMviState
{
    /// <summary>
    /// 获取初始状态（Phase 8 Issue 02：默认页从 Runtime 改为概览）。
    /// </summary>
    public static AppShellState Initial { get; } = new(
        ShellPage.Dashboard, false, RuntimeLifecycle.Stopped, 0, null, null, null);
}
