using DshDesktop.Presentation.Avalonia.Features.AppShell;
using MiKiNuo.Mvi.Application.MVI.Effect;
using MiKiNuo.Mvi.Application.MVI.Mediator;
using MiKiNuo.Mvi.Domain.MVI.Effect;

namespace DshDesktop.Presentation.Avalonia.Features.Dashboard;

/// <summary>
/// 表示 Dashboard 副作用分发器（§10 桥梁；跨 Feature 导航经 Mediator 路由到组合根，§28）。
/// </summary>
public sealed partial class DashboardEffectDispatcher
    : MviEffectDispatcherBase<DashboardIntent, DashboardEffect>
{
    private readonly IMviMediator _mediator;

    /// <summary>
    /// 初始化 Dashboard 副作用分发器。
    /// </summary>
    /// <param name="mediator">跨层协调中介者。</param>
    public DashboardEffectDispatcher(IMviMediator mediator)
    {
        ArgumentNullException.ThrowIfNull(mediator);
        _mediator = mediator;
    }

    /// <summary>
    /// 处理导航副作用：转发为应用壳 NavigateRequest。
    /// </summary>
    [MviEffect(typeof(DashboardEffect.Navigate))]
    private async ValueTask HandleNavigate(
        DashboardEffect.Navigate effect,
        CancellationToken cancellationToken)
    {
        _ = await _mediator
            .SendAsync(new NavigateRequest(effect.Page), cancellationToken)
            .ConfigureAwait(false);
    }
}
