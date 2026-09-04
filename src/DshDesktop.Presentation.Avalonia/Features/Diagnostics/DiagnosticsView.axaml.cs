using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using MiKiNuo.Mvi.Platforms.Avalonia.Views;
using MiKiNuo.Mvi.Presentation.Disposables;

namespace DshDesktop.Presentation.Avalonia.Features.Diagnostics;

/// <summary>
/// 表示 Diagnostics 视图：滚动列表 + 新事件到达时自动滚到底部。
/// </summary>
public sealed partial class DiagnosticsView : MviAvaloniaView<DiagnosticsViewModel>
{
    private readonly ListBox _entriesList;

    /// <summary>
    /// 初始化 Diagnostics 视图。
    /// </summary>
    public DiagnosticsView()
    {
        AvaloniaXamlLoader.Load(this);
        _entriesList = this.FindControl<ListBox>("EntriesList")
            ?? throw new InvalidOperationException("无法找到 EntriesList 控件。");
    }

    /// <inheritdoc />
    protected override void OnBind(DiagnosticsViewModel viewModel, MviDisposableBag bindings)
    {
        base.OnBind(viewModel, bindings);

        PropertyChangedEventHandler handler = (_, args) =>
        {
            if (args.PropertyName is nameof(DiagnosticsViewModel.Entries))
            {
                ScrollToEnd(viewModel);
            }
        };

        viewModel.PropertyChanged += handler;
        bindings.Add(() => viewModel.PropertyChanged -= handler);
    }

    private void ScrollToEnd(DiagnosticsViewModel viewModel)
    {
        if (viewModel.Entries.Count > 0)
        {
            _entriesList.ScrollIntoView(viewModel.Entries[viewModel.Entries.Count - 1]);
        }
    }
}
