using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using DshDesktop.Application.Diagnostics;
using DshDesktop.Domain.Common;
using DshDesktop.Domain.Runtime;
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
    private readonly Border _loadingOverlay;
    private readonly TextBlock _loadingTitle;
    private readonly TextBlock _loadingHint;
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
        _loadingOverlay = this.FindControl<Border>("LoadingOverlay")
            ?? throw new InvalidOperationException("无法找到 LoadingOverlay 控件。");
        _loadingTitle = this.FindControl<TextBlock>("LoadingTitle")
            ?? throw new InvalidOperationException("无法找到 LoadingTitle 控件。");
        _loadingHint = this.FindControl<TextBlock>("LoadingHint")
            ?? throw new InvalidOperationException("无法找到 LoadingHint 控件。");

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
            // 只有业务地址在途时的成功导航才撤遮罩。_navigatedUrl 为 null 表示这次完成的是
            // 内部导航——Runtime 停回时那条 about:blank 复位，或 WebView 创建时的初始空白页，
            // 它们同样报成功；若一并撤遮罩，用户会在已隐藏 WebView 的内容区看到一片空白。
            if (_navigatedUrl is not null)
            {
                _loadingOverlay.IsVisible = false;
            }

            ViewModel.NotifyNavigationCompleted(url);
            _ = InstallBridgeAsync();
        }
        else
        {
            // 导航失败：撤下原生 WebView、遮罩保留并换成失败文案。
            // 页内错误条已移除（§21 Phase 6 修订注），但也不能直接撤掉遮罩——原生 WebView
            // 会盖住任何 Avalonia 文案，撤掉只会露出它未渲染的黑底（airspace 约束）。
            // 仍要结束 Loading，否则状态机会停在加载中；错误详情留在 Serilog / 诊断中心。
            // 打码再走日志（CONTEXT.md Session URL：日志中 token 强制打码）——url 可能是含一次性
            // token 的 Session URL，明文落 data/logs 即泄漏；规则与进程输出日志同一来源。
            Serilog.Log.Warning("Workbench.Navigation.Failed Url={Url}", SessionUrlRedactor.Redact(url));
            _webViewHost.IsVisible = false;
            ShowLoadingOverlay("界面加载失败", "详情见诊断中心");
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

        // DshUrl 由兄弟 Store 回流驱动，其 PropertyChanged 可能在派发线程上直接触发（不经 Post）；
        // 回调里直接对 NativeWebView 做 Navigate / IsVisible，必须自行编组（详见 OnViewModelPropertyChanged）。
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        bindings.Add(() => viewModel.PropertyChanged -= OnViewModelPropertyChanged);

        ApplyDshUrl(viewModel.DshUrl);
    }

    /// <summary>
    /// ViewModel 投影变化处理：兄弟 Store 回流可能在后台派发线程触发，触及 NativeWebView 控件前必须
    /// 编组到 UI 线程（与 MainWindow 的 OnUiThread 收口方式一致）。编组后在 UI 线程重读 DshUrl。
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnViewModelPropertyChanged(sender, args));
            return;
        }

        if (args.PropertyName is nameof(WorkbenchViewModel.DshUrl))
        {
            ApplyDshUrl(ViewModel.DshUrl);
        }
    }

    private void ApplyDshUrl(string? dshUrl)
    {
        if (dshUrl is not null && !string.Equals(dshUrl, _navigatedUrl, StringComparison.Ordinal))
        {
            // 先露出原生 WebView 再导航：适配器是挂树后异步创建的，隐藏状态下能否缓存
            // Navigate 没有实机依据，故不赌——显隐顺序固定为「可见 → 导航」。
            _webViewHost.IsVisible = true;
            NavigateTo(dshUrl);
        }
        else if (dshUrl is null && _navigatedUrl is not null)
        {
            // 回到未就绪态后复位 _navigatedUrl：保持"null ⟺ 未导航业务地址"口径，空 URL 状态不重复导航。
            _webViewHost.Navigate(new Uri("about:blank", UriKind.Absolute));
            _webViewHost.IsVisible = false;
            _navigatedUrl = null;
        }

        ShowLoadingOverlay("正在启动 DSH 工作台…", "Runtime 就绪后自动载入界面");
    }

    private void NavigateTo(string url)
    {
        _webViewHost.Navigate(new Uri(url, UriKind.Absolute));
        _navigatedUrl = url;
    }

    /// <summary>
    /// 显示加载遮罩并写入文案。原生 WebView 在场时遮罩会被它盖住（airspace 约束），
    /// 故调用方必须保证此刻 WebView 不可见。
    /// </summary>
    private void ShowLoadingOverlay(string title, string hint)
    {
        _loadingTitle.Text = title;
        _loadingHint.Text = hint;
        _loadingOverlay.IsVisible = true;
    }
}
