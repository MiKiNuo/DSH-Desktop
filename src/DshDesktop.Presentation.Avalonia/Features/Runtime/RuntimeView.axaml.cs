using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using DshDesktop.Domain.Runtime;
using MiKiNuo.Mvi.Platforms.Avalonia.Views;
using MiKiNuo.Mvi.Presentation.Disposables;

namespace DshDesktop.Presentation.Avalonia.Features.Runtime;

/// <summary>
/// 表示 Runtime 视图：阶段清单与健康徽章由 View 依据 State 投影计算（表现逻辑属于 View）。
/// </summary>
public sealed partial class RuntimeView : MviAvaloniaView<RuntimeViewModel>
{
    private static readonly IBrush DoneBrush = new SolidColorBrush(Color.Parse("#22C55E"));
    private static readonly IBrush ActiveBrush = new SolidColorBrush(Color.Parse("#EAB308"));
    private static readonly IBrush PendingBrush = new SolidColorBrush(Color.Parse("#6B7280"));
    private static readonly IBrush FailedBrush = new SolidColorBrush(Color.Parse("#EF4444"));

    private readonly TextBlock _stageIcon2;
    private readonly TextBlock _stageIcon3;
    private readonly TextBlock _stageIcon4;
    private readonly TextBlock _stageIcon1;
    private readonly Border _healthBadge;

    /// <summary>
    /// 初始化 Runtime 视图。
    /// </summary>
    public RuntimeView()
    {
        AvaloniaXamlLoader.Load(this);
        _stageIcon1 = FindRequiredTextBlock("StageIcon1");
        _stageIcon2 = FindRequiredTextBlock("StageIcon2");
        _stageIcon3 = FindRequiredTextBlock("StageIcon3");
        _stageIcon4 = FindRequiredTextBlock("StageIcon4");
        _healthBadge = this.FindControl<Border>("HealthBadge")
            ?? throw new InvalidOperationException("无法找到 HealthBadge 控件。");
    }

    /// <inheritdoc />
    protected override void OnBind(RuntimeViewModel viewModel, MviDisposableBag bindings)
    {
        base.OnBind(viewModel, bindings);

        PropertyChangedEventHandler handler = (_, args) =>
        {
            if (args.PropertyName
                is nameof(RuntimeViewModel.Lifecycle)
                or nameof(RuntimeViewModel.StartupStage)
                or nameof(RuntimeViewModel.Health))
            {
                ApplyIndicators(viewModel);
            }
        };

        viewModel.PropertyChanged += handler;
        bindings.Add(() => viewModel.PropertyChanged -= handler);

        ApplyIndicators(viewModel);
    }

    private void ApplyIndicators(RuntimeViewModel viewModel)
    {
        bool failed = viewModel.Lifecycle is RuntimeLifecycle.Failed;
        bool running = viewModel.Lifecycle is RuntimeLifecycle.Running;
        bool starting = viewModel.Lifecycle is RuntimeLifecycle.Starting;
        RuntimeStartupStage stage = viewModel.StartupStage;

        SetRow(_stageIcon1, done: true, active: false, failed: false);
        SetRow(
            _stageIcon2,
            done: running || stage >= RuntimeStartupStage.WaitingReady,
            active: starting && stage <= RuntimeStartupStage.Spawning,
            failed: failed && stage < RuntimeStartupStage.WaitingReady);
        SetRow(
            _stageIcon3,
            done: running,
            active: starting && stage is RuntimeStartupStage.WaitingReady,
            failed: failed && stage is >= RuntimeStartupStage.WaitingReady and < RuntimeStartupStage.Ready);
        SetRow(_stageIcon4, done: running, active: false, failed: false);

        IBrush brush = viewModel.Health switch
        {
            RuntimeHealth.Healthy => DoneBrush,
            RuntimeHealth.Unresponsive => FailedBrush,
            _ => PendingBrush,
        };
        _healthBadge.Background = brush;
    }

    private static void SetRow(TextBlock icon, bool done, bool active, bool failed)
    {
        if (failed)
        {
            icon.Text = "✗";
            icon.Foreground = FailedBrush;
        }
        else if (done)
        {
            icon.Text = "✓";
            icon.Foreground = DoneBrush;
        }
        else if (active)
        {
            icon.Text = "●";
            icon.Foreground = ActiveBrush;
        }
        else
        {
            icon.Text = "○";
            icon.Foreground = PendingBrush;
        }
    }

    private TextBlock FindRequiredTextBlock(string name)
    {
        return this.FindControl<TextBlock>(name)
            ?? throw new InvalidOperationException($"无法找到 {name} 控件。");
    }
}
