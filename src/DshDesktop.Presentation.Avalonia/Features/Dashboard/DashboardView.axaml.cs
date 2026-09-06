using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using DshDesktop.Domain.Runtime;
using DshDesktop.Presentation.Avalonia.Features.Runtime;
using MiKiNuo.Mvi.Platforms.Avalonia.Views;
using MiKiNuo.Mvi.Presentation.Disposables;

namespace DshDesktop.Presentation.Avalonia.Features.Dashboard;

/// <summary>
/// 表示 Dashboard 视图（Phase 8 Issue 03：独立 DashboardViewModel 投影直显）。
/// 就绪行图标 / 健康状态点的颜色映射属表现逻辑，随生命周期在 View 层计算
/// （与 RuntimeView.ApplyIndicators 同先例，画刷复用 RuntimeLifecycleBrushes）。
/// </summary>
public sealed partial class DashboardView : MviAvaloniaView<DashboardViewModel>
{
    private readonly IBrush _iconTintRunning;
    private readonly IBrush _iconTintTransition;
    private readonly IBrush _iconTintFailed;
    private readonly IBrush _iconTintStopped;
    private readonly IBrush _muted;

    private readonly Border _readyIconBox;
    private readonly TextBlock _readyIconText;
    private readonly TextBlock _healthStatus;

    /// <summary>
    /// 初始化 Dashboard 视图。
    /// </summary>
    public DashboardView()
    {
        AvaloniaXamlLoader.Load(this);
        _readyIconBox = FindRequiredControl<Border>("ReadyIconBox");
        _readyIconText = FindRequiredControl<TextBlock>("ReadyIconText");
        _healthStatus = FindRequiredControl<TextBlock>("HealthStatus");

        // Phase 8 评审 F6：画刷集中 DshTheme 资源字典，code-behind 只引用键。
        _iconTintRunning = RequireBrush("DshDashIconTintRunningBrush");
        _iconTintTransition = RequireBrush("DshDashIconTintTransitionBrush");
        _iconTintFailed = RequireBrush("DshDashIconTintFailedBrush");
        _iconTintStopped = RequireBrush("DshDashIconTintStoppedBrush");
        _muted = RequireBrush("DshMutedBrush");
    }

    /// <inheritdoc />
    protected override void OnBind(DashboardViewModel viewModel, MviDisposableBag bindings)
    {
        base.OnBind(viewModel, bindings);

        PropertyChangedEventHandler handler = (_, args) =>
        {
            if (args.PropertyName is nameof(DashboardViewModel.Lifecycle))
            {
                ApplyLifecycleIndicator(viewModel.Lifecycle);
            }
            else if (args.PropertyName is nameof(DashboardViewModel.Health))
            {
                ApplyHealthIndicator(viewModel.Health);
            }
        };

        viewModel.PropertyChanged += handler;
        bindings.Add(() => viewModel.PropertyChanged -= handler);

        ApplyLifecycleIndicator(viewModel.Lifecycle);
        ApplyHealthIndicator(viewModel.Health);
    }

    private void ApplyLifecycleIndicator(RuntimeLifecycle lifecycle)
    {
        (string glyph, IBrush tint) = lifecycle switch
        {
            RuntimeLifecycle.Running => ("✓", _iconTintRunning),
            RuntimeLifecycle.Starting or RuntimeLifecycle.Stopping or RuntimeLifecycle.Recovering
                => ("●", _iconTintTransition),
            RuntimeLifecycle.Failed => ("✗", _iconTintFailed),
            _ => ("○", _iconTintStopped),
        };
        _readyIconText.Text = glyph;
        _readyIconText.Foreground = RuntimeLifecycleBrushes.For(lifecycle);
        _readyIconBox.Background = tint;
    }

    private void ApplyHealthIndicator(RuntimeHealth health)
    {
        _healthStatus.Foreground = health switch
        {
            RuntimeHealth.Healthy => RuntimeLifecycleBrushes.Running,
            RuntimeHealth.Unresponsive => RuntimeLifecycleBrushes.Failed,
            _ => _muted,
        };
    }

    private TControl FindRequiredControl<TControl>(string name)
        where TControl : Control
    {
        return this.FindControl<TControl>(name)
            ?? throw new InvalidOperationException($"无法找到 {name} 控件。");
    }

    private IBrush RequireBrush(string key)
    {
        return this.TryFindResource(key, out object? value) && value is IBrush brush
            ? brush
            : throw new InvalidOperationException($"DshTheme 缺少 {key} 画刷资源。");
    }
}
