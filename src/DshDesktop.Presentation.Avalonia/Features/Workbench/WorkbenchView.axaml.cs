using System.ComponentModel;
using System.Net;
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

    /// <summary>
    /// 本次会话是否已用过「首次导航失败即重试」的机会（单次上限）。
    /// dsh 认证的 303 Cookie 兑换可能未在首次导航时落定，重试一次即可恢复；
    /// 但 Runtime 真挂时重试无意义，故只给一次机会。
    /// <para>
    /// 换 Runtime 时必须复位：新端口 ⇒ 新 authority ⇒ 旧 Cookie 全部失效，
    /// 新一轮冷启动又要重新兑换，不复位则第二轮起重试机会已耗尽、直接判死。
    /// </para>
    /// </summary>
    private bool _unauthorizedRetryUsed;

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

            // 导航成功即清理陈旧 dsh 会话 Cookie（见 DshSessionCookie 的根因说明）：
            // 每次启动换端口 ⇒ 每个旧 Cookie 名永不复用，只会无界累积，累积过头会让
            // dsh 因请求头越过 16KB 而回 431。此刻 CookieManager 才可用，且工作台已可用，
            // 清理不影响用户当前会话。
            _ = PurgeStaleSessionCookiesAsync();
        }
        else
        {
            // 首次导航失败时先原地重试一次，再判死。
            //
            // dsh 的浏览器认证是「一次性 query token → 303 兑换 → 落 authority 绑定的签名 Cookie
            // → 带 Cookie 复访才 200」。Cookie 名 = sha256(authority)，authority 含端口
            // （dsh-client-connection 的 cookieName/requestAuthority），而宿主每次启动端口都变，
            // 所以旧 Cookie 永远不可复用，每次冷启动都必须当场完成兑换。
            //
            // ⚠ 重试必须直发 Navigate，**不可走 NavigateTo**：NavigateTo 会复位本闸门
            // （换 URL 才复位），走它等于每次重试都把闸门清零 ⇒ 无限重试刷屏。
            // 2026-10-12 实机已踩：日志在 6 秒内刷出数千条 Workbench.Navigation.Retry。
            //
            // 判据只能是「首次失败」：Avalonia 12.1.0 的 WebViewNavigationCompletedEventArgs
            // 只有 IsSuccess / Request，不暴露 HTTP 状态码，无法按 401 精确重试。
            if (!_unauthorizedRetryUsed && _navigatedUrl is { } retryUrl)
            {
                _unauthorizedRetryUsed = true;
                Serilog.Log.Warning("Workbench.Navigation.Retry Url={Url}", SessionUrlRedactor.Redact(url));
                _webViewHost.Navigate(new Uri(retryUrl, UriKind.Absolute));
                return;
            }

            // 重试仍失败：撤下原生 WebView、遮罩保留并换成失败文案。
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

    /// <summary>
    /// 删除陈旧的 dsh 会话 Cookie（根因修复，见 <see cref="DshSessionCookie"/>）。
    /// </summary>
    /// <remarks>
    /// 每次启动换端口 ⇒ 每个旧 Cookie 名（sha256(authority)）永不复用，只会无界累积。
    /// 实机 70 条 / 25883 字节即让请求头越过 Node 16KB 上限、dsh 回 431，页面变 Edge 错误页。
    /// 此刻工作台已加载成功、当前 authority 的 Cookie 正在被使用，故<b>除最新一条外全删</b>。
    /// 用 WebView2 自带 <c>TryGetCookieManager()</c>（<c>System.Net.Cookie</c>，无新依赖），
    /// 失败只记日志——维护动作绝不能影响已可用的工作台。
    /// </remarks>
    private async Task PurgeStaleSessionCookiesAsync()
    {
        try
        {
            // TryGetCookieManager 直接返回管理器（WebView 未就绪时为 null），不是 Try 模式。
            NativeWebViewCookieManager? manager = _webViewHost.TryGetCookieManager();
            if (manager is null)
            {
                return;
            }

            IReadOnlyList<Cookie> cookies = await manager.GetCookiesAsync().ConfigureAwait(true);
            List<Cookie> sessions = cookies
                .Where(c => DshSessionCookie.IsDshSessionCookie(c.Name))
                .ToList();
            if (!DshSessionCookie.NeedsPurge(sessions.Count))
            {
                return;
            }

            // 最新一条 = 最后写入的（WebView2 按创建序枚举）；它对应当前 authority，必须留下。
            HashSet<string> stale = DshSessionCookie
                .SelectStaleNames(sessions.Select(c => c.Name).ToList(), sessions[^1].Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            int removed = 0;
            foreach (Cookie cookie in sessions)
            {
                if (stale.Contains(cookie.Name))
                {
                    manager.DeleteCookie(cookie);
                    removed++;
                }
            }

            if (removed > 0)
            {
                Serilog.Log.Information(
                    "Workbench.SessionCookie.Purged Removed={Removed} Kept={Kept}",
                    removed,
                    sessions.Count - removed);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning("Workbench.SessionCookie.PurgeFailed Error={Error}", ex.Message);
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
        // 换 URL = 换 Runtime（新端口 ⇒ 新 authority ⇒ 旧 Cookie 失效），
        // 是一次全新的冷启动，重试机会随之重置。
        _unauthorizedRetryUsed = false;
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
