using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using DshDesktop.Application.Diagnostics;
using DshDesktop.Domain.Common;
using DshDesktop.Presentation.Avalonia.Features.Workbench.Bridge;
using MiKiNuo.Mvi.Platforms.Avalonia.Views;
using MiKiNuo.Mvi.Presentation.Disposables;

namespace DshDesktop.Presentation.Avalonia.Features.Workbench;

/// <summary>
/// 表示 Workbench 视图：观察 DshUrl 投影驱动 NativeWebView 导航（§21：DSH Web UI 视为黑盒，
/// 禁止 DOM 注入 / JS Hack）。WebView 事件 → Intent 的接线在本代码隐藏层（IO 边界），不进 Reducer。
/// </summary>
/// <remarks>
/// 页内工具条（后退 / 前进 / 刷新）已由用户移除，导航失败也没有页内错误条承载——
/// 本视图因此不再有按钮事件处理器，只剩「投影 → 导航」与 WebView → Intent 两条回流接线。
/// </remarks>
public sealed partial class WorkbenchView : MviAvaloniaView<WorkbenchViewModel>
{
    private readonly NativeWebView _webViewHost;
    private readonly Border _placeholderOverlay;
    private readonly DesktopBridgeProtocol _bridge = new();
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
        _webViewHost.WebMessageReceived += (_, args) => OnWebMessageReceived(args);
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
            ViewModel.NotifyNavigationCompleted(url);
            _ = InstallBridgeAsync();
        }
        else
        {
            // 页内错误条已移除：失败只记日志（诊断中心可见），但仍要结束 Loading，
            // 否则加载条会永久悬停。错误详情留在 Serilog 里。
            Serilog.Log.Warning("Workbench.Navigation.Failed Url={Url}", url);
            ViewModel.NotifyNavigationCompleted(url);
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

    // ---- Desktop Bridge（ADR-0006，§21 修订注扩展点：仅 dshDesktopDirectoryPicker.pick 一方法）----

    private async Task InstallBridgeAsync()
    {
        // 每次导航完成重装：页面上下文重建后 shim 序号归零，挂起表同步清空。
        _bridge.Reset();
        try
        {
            await _webViewHost.InvokeScript(DesktopBridgeShim.InstallScript);
            Serilog.Log.Information(DiagnosticEventNames.BridgeDirectoryPickerInstalled);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, DiagnosticEventNames.BridgeDirectoryPickerInstalled + " 注入失败");
        }
    }

    private async void OnWebMessageReceived(WebMessageReceivedEventArgs args)
    {
        if (!_bridge.TryParseRequest(args.Body, out string id))
        {
            return;
        }

        if (!_bridge.TryBegin(id))
        {
            Serilog.Log.Warning(
                DiagnosticEventNames.BridgeDirectoryPickerInvoked + " 重复请求 id={RequestId}，忽略", id);
            return;
        }

        Serilog.Log.Information(
            DiagnosticEventNames.BridgeDirectoryPickerInvoked + " RequestId={RequestId}", id);
        try
        {
            string? path = await PickFolderAsync();
            await _webViewHost.InvokeScript(DesktopBridgeShim.BuildResolveScript(id, path));
        }
        catch (Exception ex)
        {
            try
            {
                await _webViewHost.InvokeScript(DesktopBridgeShim.BuildRejectScript(id, ex.Message));
            }
            catch (Exception rejectEx)
            {
                Serilog.Log.Warning(rejectEx, DiagnosticEventNames.BridgeDirectoryPickerInvoked + " reject 回调失败");
            }
        }
        finally
        {
            _bridge.Complete(id);
        }
    }

    private async Task<string?> PickFolderAsync()
    {
        IStorageProvider? storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storageProvider is null)
        {
            throw new InvalidOperationException("StorageProvider 不可用。");
        }

        IReadOnlyList<IStorageFolder> folders = await storageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "选择文件夹", AllowMultiple = false });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
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
