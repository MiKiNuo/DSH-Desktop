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
public sealed record AppShellState(
    ShellPage CurrentPage,
    bool SidebarCollapsed,
    RuntimeLifecycle RuntimeIndicator,
    int UpdateBadge) : IMviState
{
    /// <summary>
    /// 获取初始状态。
    /// </summary>
    public static AppShellState Initial { get; } = new(ShellPage.Runtime, false, RuntimeLifecycle.Stopped, 0);
}
