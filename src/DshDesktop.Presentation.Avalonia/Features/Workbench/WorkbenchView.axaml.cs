using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using MiKiNuo.Mvi.Platforms.Avalonia.Views;
using MiKiNuo.Mvi.Presentation.Disposables;

namespace DshDesktop.Presentation.Avalonia.Features.Workbench;

/// <summary>
/// 表示 Workbench 视图：观察 DshUrl 投影驱动 NativeWebView 导航（§21：DSH Web UI 视为黑盒，
/// 禁止 DOM 注入 / JS Hack；导航是 View 对状态投影的响应，不产生状态变更）。
/// </summary>
public sealed partial class WorkbenchView : MviAvaloniaView<WorkbenchViewModel>
{
    private readonly NativeWebView _webViewHost;
    private readonly Border _placeholderOverlay;
    private string? _navigatedUrl;

    /// <summary>
    /// 初始化 Workbench 视图。
    /// </summary>
    public WorkbenchView()
    {
        AvaloniaXamlLoader.Load(this);
        _webViewHost = this.FindControl<NativeWebView>("WebViewHost")
            ?? throw new InvalidOperationException("无法找到 WebViewHost 控件。");
        _placeholderOverlay = this.FindControl<Border>("PlaceholderOverlay")
            ?? throw new InvalidOperationException("无法找到 PlaceholderOverlay 控件。");
    }

    /// <inheritdoc />
    protected override void OnBind(WorkbenchViewModel viewModel, MviDisposableBag bindings)
    {
        base.OnBind(viewModel, bindings);

        PropertyChangedEventHandler handler = (_, args) =>
        {
            if (args.PropertyName is nameof(WorkbenchViewModel.DshUrl))
            {
                ApplyDshUrl(viewModel.DshUrl);
            }
        };

        viewModel.PropertyChanged += handler;
        bindings.Add(() => viewModel.PropertyChanged -= handler);

        ApplyDshUrl(viewModel.DshUrl);
    }

    private void ApplyDshUrl(string? dshUrl)
    {
        if (dshUrl is not null && !string.Equals(dshUrl, _navigatedUrl, StringComparison.Ordinal))
        {
            _webViewHost.Navigate(new Uri(dshUrl, UriKind.Absolute));
            _navigatedUrl = dshUrl;
        }
        else if (dshUrl is null && _navigatedUrl is not null)
        {
            _webViewHost.Navigate(new Uri("about:blank", UriKind.Absolute));
            _navigatedUrl = null;
        }

        _placeholderOverlay.IsVisible = dshUrl is null;
    }
}
