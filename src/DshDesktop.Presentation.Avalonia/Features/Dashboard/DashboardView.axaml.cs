using System.ComponentModel;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using DshDesktop.Domain.Runtime;
using DshDesktop.Presentation.Avalonia.Features.Runtime;
using MiKiNuo.Mvi.Platforms.Avalonia.Views;
using MiKiNuo.Mvi.Presentation.Disposables;
using Path = Avalonia.Controls.Shapes.Path;

namespace DshDesktop.Presentation.Avalonia.Features.Dashboard;

/// <summary>
/// 把启动阶段耗时占比（0-100）映射为 <see cref="GridLength"/>.Star，
/// 让瀑布图的 <c>wf-fill</c> 按阶段耗时占总量比例占据轨道宽度。
/// <c>Remainder=true</c> 时返回剩余比例（100 - 占比），与填充列配对。
/// </summary>
public sealed class PercentColumnConverter : IValueConverter
{
    public bool Remainder { get; set; }

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double pct = value is double d ? d : 0;
        double star = Remainder ? Math.Max(0, 100 - pct) : Math.Max(0, pct);
        return new GridLength(star, GridUnitType.Star);
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 表示 Dashboard 视图（Phase 8 Issue 03：独立 DashboardViewModel 投影直显）。
/// 就绪图标的图标 / 着色 / 底色取自 <see cref="RuntimeLifecycleProjection"/>（与 Runtime 页同一口径），
/// 健康状态点的着色取自 <see cref="RuntimeLifecycleBrushes"/>；View 只负责把结果贴到控件上。
/// </summary>
public sealed partial class DashboardView : MviAvaloniaView<DashboardViewModel>
{
    private readonly Border _readyIconBox;
    private readonly Path _readyIconPath;
    private readonly TextBlock _healthStatus;

    /// <summary>
    /// 初始化 Dashboard 视图。
    /// </summary>
    public DashboardView()
    {
        AvaloniaXamlLoader.Load(this);
        _readyIconBox = FindRequiredControl<Border>("ReadyIconBox");
        _readyIconPath = FindRequiredControl<Path>("ReadyIconPath");
        _healthStatus = FindRequiredControl<TextBlock>("HealthStatus");
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
        LifecycleProjection projection = RuntimeLifecycleProjection.For(lifecycle);

        if (this.FindResource(projection.IconKey) is StreamGeometry geometry)
        {
            _readyIconPath.Data = geometry;
            _readyIconPath.Stroke = projection.Color;
        }

        _readyIconBox.Background = projection.Tint;
    }

    private void ApplyHealthIndicator(RuntimeHealth health)
    {
        _healthStatus.Foreground = health switch
        {
            RuntimeHealth.Healthy => RuntimeLifecycleBrushes.Running,
            RuntimeHealth.Unresponsive => RuntimeLifecycleBrushes.Failed,
            _ => RuntimeLifecycleBrushes.Muted,
        };
    }

    private TControl FindRequiredControl<TControl>(string name)
        where TControl : Control
    {
        return this.FindControl<TControl>(name)
            ?? throw new InvalidOperationException($"无法找到 {name} 控件。");
    }
}
