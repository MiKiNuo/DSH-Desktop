using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using DshDesktop.Domain.Runtime;
using MiKiNuo.Mvi.Platforms.Avalonia.Views;
using MiKiNuo.Mvi.Presentation.Disposables;

namespace DshDesktop.Presentation.Avalonia.Features.Runtime;

/// <summary>
/// 表示 Runtime 视图（Phase 8 Issue 04）：六态 pills 高亮、状态图标着色与 Failed 恢复面板
/// 显隐由 View 依据 State 投影计算（表现逻辑属于 View）。
/// </summary>
public sealed partial class RuntimeView : MviAvaloniaView<RuntimeViewModel>
{
    private readonly IReadOnlyDictionary<RuntimeLifecycle, Border> _pills;
    private readonly TextBlock _lifecycleIconText;
    private readonly StackPanel _recoverPanel;

    /// <summary>
    /// 初始化 Runtime 视图。
    /// </summary>
    public RuntimeView()
    {
        AvaloniaXamlLoader.Load(this);
        _pills = new Dictionary<RuntimeLifecycle, Border>
        {
            [RuntimeLifecycle.Stopped] = FindRequiredBorder("PillStopped"),
            [RuntimeLifecycle.Starting] = FindRequiredBorder("PillStarting"),
            [RuntimeLifecycle.Running] = FindRequiredBorder("PillRunning"),
            [RuntimeLifecycle.Stopping] = FindRequiredBorder("PillStopping"),
            [RuntimeLifecycle.Failed] = FindRequiredBorder("PillFailed"),
            [RuntimeLifecycle.Recovering] = FindRequiredBorder("PillRecovering"),
        };
        _lifecycleIconText = this.FindControl<TextBlock>("LifecycleIconText")
            ?? throw new InvalidOperationException("无法找到 LifecycleIconText 控件。");
        _recoverPanel = this.FindControl<StackPanel>("RecoverPanel")
            ?? throw new InvalidOperationException("无法找到 RecoverPanel 控件。");
    }

    /// <inheritdoc />
    protected override void OnBind(RuntimeViewModel viewModel, MviDisposableBag bindings)
    {
        base.OnBind(viewModel, bindings);

        PropertyChangedEventHandler handler = (_, args) =>
        {
            if (args.PropertyName is nameof(RuntimeViewModel.Lifecycle))
            {
                ApplyIndicators(viewModel);
            }
        };

        viewModel.PropertyChanged += handler;
        bindings.Add(() => viewModel.PropertyChanged -= handler);

        ApplyIndicators(viewModel);
    }

    /// <summary>
    /// 应用状态机指示：当前态 pill 高亮（原型 .state-pill.on）、图标着色（与壳状态点同色系）、
    /// Failed 恢复面板显隐（ADR-0004）。
    /// </summary>
    private void ApplyIndicators(RuntimeViewModel viewModel)
    {
        RuntimeLifecycle lifecycle = viewModel.Lifecycle;

        foreach ((RuntimeLifecycle pillLifecycle, Border pill) in _pills)
        {
            if (pillLifecycle == lifecycle)
            {
                if (!pill.Classes.Contains("on"))
                {
                    pill.Classes.Add("on");
                }
            }
            else
            {
                pill.Classes.Remove("on");
            }
        }

        (string glyph, IBrush brush) = lifecycle switch
        {
            RuntimeLifecycle.Running => ("✓", RuntimeLifecycleBrushes.Running),
            RuntimeLifecycle.Failed => ("✗", RuntimeLifecycleBrushes.Failed),
            RuntimeLifecycle.Starting or RuntimeLifecycle.Stopping or RuntimeLifecycle.Recovering =>
                ("●", RuntimeLifecycleBrushes.Transition),
            _ => ("○", RuntimeLifecycleBrushes.Stopped),
        };
        _lifecycleIconText.Text = glyph;
        _lifecycleIconText.Foreground = brush;

        _recoverPanel.IsVisible = lifecycle is RuntimeLifecycle.Failed;
    }

    private void OnKeepRuntimeOnCloseToggled(object? sender, RoutedEventArgs args)
    {
        // 无载荷翻转：目标状态由 Reducer 从 State 推导（同 Settings ToggleSafeMode 先例）。
        ViewModel.ToggleKeepRuntimeOnCloseCommand.Execute(null);
    }

    private void OnAutoSafeModeOnFailureToggled(object? sender, RoutedEventArgs args)
    {
        ViewModel.ToggleAutoSafeModeOnFailureCommand.Execute(null);
    }

    private void OnCheckUpdatesOnStartupToggled(object? sender, RoutedEventArgs args)
    {
        ViewModel.ToggleCheckUpdatesOnStartupCommand.Execute(null);
    }

    private Border FindRequiredBorder(string name)
    {
        return this.FindControl<Border>(name)
            ?? throw new InvalidOperationException($"无法找到 {name} 控件。");
    }
}
