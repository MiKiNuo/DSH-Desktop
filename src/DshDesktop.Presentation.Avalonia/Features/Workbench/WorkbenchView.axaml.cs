using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using DshDesktop.Domain.Common;
using MiKiNuo.Mvi.Platforms.Avalonia.Views;
using MiKiNuo.Mvi.Presentation.Disposables;

namespace DshDesktop.Presentation.Avalonia.Features.Workbench;

/// <summary>
/// 表示 Workbench 视图：观察 DshUrl 投影驱动 NativeWebView 导航（§21：DSH Web UI 视为黑盒，
/// 禁止 DOM 注入 / JS Hack）。WebView 事件 → Intent 的接线在本代码隐藏层（IO 边界），不进 Reducer；
/// 后退/前进走 WebView 内部历史，刷新取 ViewModel 提供的最新 Session URL。
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

        _webViewHost.NavigationStarted += (_, args) => OnNavigationStarted(args);
        _webViewHost.NavigationCompleted += (_, args) => OnNavigationCompleted(args);
    }

    private bool _webViewReadyLogged;

    private void OnNavigationStarted(WebViewNavigationStartingEventArgs args)
    {
        ViewModel.NotifyNavigationStarted(args.Request?.ToString() ?? string.Empty);
    }

    private void OnNavigationCompleted(WebViewNavigationCompletedEventArgs args)
    {
        string url = args.Request?.ToString() ?? _navigatedUrl ?? string.Empty;
        if (args.IsSuccess)
        {
            ViewModel.NotifyNavigationCompleted(url, _webViewHost.CanGoBack, _webViewHost.CanGoForward);
        }
        else
        {
            ViewModel.NotifyNavigationFailed($"页面加载失败：{url}");
        }

        // §46：WebView 首次导航完成即 WebView Ready（自进程入口起算；失败导航的虚报接受）。
        if (!_webViewReadyLogged)
        {
            _webViewReadyLogged = true;
            Serilog.Log.Information(
                "Runtime.WebView.Ready ElapsedMs={ElapsedMs}",
                (long)StartupTimer.SinceProcessStart.ElapsedMilliseconds);
        }
    }

    private void OnBackClick(object? sender, RoutedEventArgs args)
    {
        // 以 WebView 自身历史为准（State 回流可能滞后），避免空后退导致 Loading 悬挂。
        if (_webViewHost.CanGoBack)
        {
            ViewModel.RequestGoBack();
            _ = _webViewHost.GoBack();
        }
    }

    private void OnForwardClick(object? sender, RoutedEventArgs args)
    {
        if (_webViewHost.CanGoForward)
        {
            ViewModel.RequestGoForward();
            _ = _webViewHost.GoForward();
        }
    }

    private void OnRefreshClick(object? sender, RoutedEventArgs args)
    {
        // 刷新 / 重试同语义：取最新 Session URL 再导航（token 一次性，禁止缓存旧 URL）。
        ViewModel.RequestReload();
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

        Action<string> reloadHandler = NavigateTo;
        viewModel.ReloadRequested += reloadHandler;
        bindings.Add(() => viewModel.ReloadRequested -= reloadHandler);

        ApplyDshUrl(viewModel.DshUrl);
    }

    private void ApplyDshUrl(string? dshUrl)
    {
        if (dshUrl is not null && !string.Equals(dshUrl, _navigatedUrl, StringComparison.Ordinal))
        {
            NavigateTo(dshUrl);
        }
        else if (dshUrl is null && _navigatedUrl is not null)
        {
            // 回到占位态后复位 _navigatedUrl：保持"null ⟺ 未导航业务地址"口径，空 URL 状态不重复导航。
            _webViewHost.Navigate(new Uri("about:blank", UriKind.Absolute));
            _navigatedUrl = null;
        }

        _placeholderOverlay.IsVisible = dshUrl is null;
    }

    private void NavigateTo(string url)
    {
        _webViewHost.Navigate(new Uri(url, UriKind.Absolute));
        _navigatedUrl = url;
    }
}
