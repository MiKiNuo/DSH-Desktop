using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using DshDesktop.Domain.Runtime;
using DshDesktop.Presentation.Avalonia.Features.AppShell;
using MiKiNuo.Mvi.Platforms.Avalonia.Views;
using MiKiNuo.Mvi.Presentation.Disposables;
using Path = Avalonia.Controls.Shapes.Path;

namespace DshDesktop.Presentation.Avalonia.Features.Runtime;

/// <summary>
/// 表示 Runtime 视图（Phase 8 Issue 04）：状态机 stepper 高亮（.current/.current-ok/.off）、
/// 生命周期图标（Path，按状态取图标与着色）、Failed 恢复面板显隐由 View 依据 State 投影计算
/// （表现逻辑属于 View）。停止 DSH 经二次确认后再执行命令。
/// </summary>
public sealed partial class RuntimeView : MviAvaloniaView<RuntimeViewModel>
{
    private readonly IReadOnlyDictionary<RuntimeLifecycle, Border> _states;
    private readonly Path _lifecycleIcon;
    private readonly Border _lifecycleIconBorder;
    private readonly Border _recoverPanel;
    private Button? _stopButton;

    /// <summary>
    /// 初始化 Runtime 视图。
    /// </summary>
    public RuntimeView()
    {
        AvaloniaXamlLoader.Load(this);
        _states = new Dictionary<RuntimeLifecycle, Border>
        {
            [RuntimeLifecycle.Stopped] = FindRequiredBorder("PillStopped"),
            [RuntimeLifecycle.Starting] = FindRequiredBorder("PillStarting"),
            [RuntimeLifecycle.Running] = FindRequiredBorder("PillRunning"),
            [RuntimeLifecycle.Stopping] = FindRequiredBorder("PillStopping"),
            [RuntimeLifecycle.Failed] = FindRequiredBorder("PillFailed"),
            [RuntimeLifecycle.Recovering] = FindRequiredBorder("PillRecovering"),
        };
        _lifecycleIcon = this.FindControl<Path>("LifecycleIcon")
            ?? throw new InvalidOperationException("无法找到 LifecycleIcon 控件。");
        _lifecycleIconBorder = this.FindControl<Border>("LifecycleIconBorder")
            ?? throw new InvalidOperationException("无法找到 LifecycleIconBorder 控件。");
        _recoverPanel = this.FindControl<Border>("RecoverPanel")
            ?? throw new InvalidOperationException("无法找到 RecoverPanel 控件。");
    }

    /// <inheritdoc />
    protected override void OnBind(RuntimeViewModel viewModel, MviDisposableBag bindings)
    {
        base.OnBind(viewModel, bindings);

        // 停止 DSH 接二次确认：将按钮命令包一层确认拦截（XAML 仍绑定原 StopRuntimeCommand，
        // 类名与既有绑定保持一致；仅执行前插入确认）。
        _stopButton = this.FindControl<Button>("StopButton");
        if (_stopButton is not null && _stopButton.Command is not ConfirmStopCommand)
        {
            _stopButton.Command = new ConfirmStopCommand(this, _stopButton.Command);
        }

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
    /// 应用状态机指示：当前态 state 高亮（.current-ok 绿 / .current 强调 / .off 虚线灰）、
    /// 生命周期图标（Path，取值自 <see cref="RuntimeLifecycleProjection"/>）、
    /// Failed 恢复面板显隐（ADR-0004）。
    /// </summary>
    private void ApplyIndicators(RuntimeViewModel viewModel)
    {
        RuntimeLifecycle lifecycle = viewModel.Lifecycle;

        foreach ((RuntimeLifecycle pillLifecycle, Border pill) in _states)
        {
            pill.Classes.Remove("current");
            pill.Classes.Remove("current-ok");
            pill.Classes.Remove("off");
            if (pillLifecycle == lifecycle)
            {
                // Running 且健康视为 current-ok（绿）；其余当前态用 current（强调色）。
                pill.Classes.Add(lifecycle == RuntimeLifecycle.Running ? "current-ok" : "current");
            }
            else
            {
                pill.Classes.Add("off");
            }
        }

        LifecycleProjection projection = RuntimeLifecycleProjection.For(lifecycle);
        if (this.FindResource(projection.IconKey) is StreamGeometry geometry)
        {
            _lifecycleIcon.Data = geometry;
        }

        _lifecycleIcon.Stroke = projection.Color;
        _lifecycleIconBorder.Background = projection.Tint;
        _lifecycleIconBorder.BorderBrush = projection.Color;

        _recoverPanel.IsVisible = lifecycle is RuntimeLifecycle.Failed;
    }

    /// <summary>
    /// 构造停止确认弹窗的等宽上下文：PID 与端口取自现有 ViewModel 属性，取不到则退化为端口或版本信息。
    /// </summary>
    private string BuildStopSubject()
    {
        int? pid = ViewModel.ProcessId;
        string? addr = ViewModel.Url is { } u
            ? u.Split('?')[0]
                .Replace("http://", string.Empty)
                .Replace("https://", string.Empty)
            : null;

        if (pid is { } p && addr is { } a)
        {
            return $"PID {p} · {a}";
        }

        if (addr is { } a2)
        {
            return a2;
        }

        if (pid is { } p2)
        {
            return $"PID {p2}";
        }

        return ViewModel.DshVersion ?? "DSH Runtime";
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

    /// <summary>
    /// 停止 DSH 命令的二次确认拦截：先弹确认框，确认后才执行被包装的原命令。
    /// </summary>
    private sealed class ConfirmStopCommand : ICommand
    {
        private readonly RuntimeView _owner;
        private readonly ICommand? _inner;

        public ConfirmStopCommand(RuntimeView owner, ICommand? inner)
        {
            _owner = owner;
            _inner = inner;
        }

        public bool CanExecute(object? parameter) => _inner?.CanExecute(parameter) ?? false;

        public async void Execute(object? parameter)
        {
            bool ok = await ConfirmDialog.ShowAsync(ConfirmAction.StopRuntime, _owner.BuildStopSubject());
            if (ok)
            {
                _inner?.Execute(parameter);
            }
        }

        public event EventHandler? CanExecuteChanged
        {
            add
            {
                if (_inner is not null)
                {
                    _inner.CanExecuteChanged += value;
                }
            }
            remove
            {
                if (_inner is not null)
                {
                    _inner.CanExecuteChanged -= value;
                }
            }
        }
    }
}
