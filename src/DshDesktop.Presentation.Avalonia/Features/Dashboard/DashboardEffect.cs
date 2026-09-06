using DshDesktop.Presentation.Avalonia.Features.AppShell;
using MiKiNuo.Mvi.Domain.MVI.Effect;

namespace DshDesktop.Presentation.Avalonia.Features.Dashboard;

/// <summary>
/// 表示 Dashboard 副作用（Phase 8 Issue 03）。
/// </summary>
public abstract partial record DashboardEffect : IMviEffect
{
    /// <summary>
    /// 表示跨 Feature 导航副作用（§28：经 Mediator NavigateRequest 路由到应用壳，
    /// 不直接依赖 AppShell Store）。
    /// </summary>
    /// <param name="Page">目标页面。</param>
    public sealed partial record Navigate(ShellPage Page) : DashboardEffect;
}
