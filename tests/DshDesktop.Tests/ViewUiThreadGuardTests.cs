namespace DshDesktop.Tests;

/// <summary>
/// 视图 code-behind 线程编组守卫（2026-09-17 兄弟 Store 回流崩溃回归）。
///
/// 背景：第三方库 <c>MviViewModelBase</c> 的 <c>IMviUiDispatcher.Post</c> 不覆盖「兄弟 Store 回流」
/// 路径——后台线程 push 兄弟 Store → <c>BindSiblingState</c> 回调 → 回流 Intent 到自己 Store 时，
/// <c>[MviBind]</c> 投影属性的 <c>PropertyChanged</c> 会在派发线程上直接触发（不经 Post）；
/// 自身 Store 直派发路径则经 Post 编组（AppShellViewModelTests 已固化）。
///
/// 因此 View code-behind 的 <c>PropertyChanged</c> 回调若触碰控件，必须自行
/// <c>Dispatcher.UIThread.CheckAccess()</c> + <c>Post</c> 编组（MainWindow.axaml.cs 已是范本）。
/// 本测试对需加固的 3 个视图（Dashboard / Workbench / Plugins）逐一钉死：其 <c>PropertyChanged</c>
/// 处理必须抽成命名方法 <c>OnViewModelPropertyChanged</c> 并在入口编组。
///
/// 测试项目不引用 <c>DshDesktop.App</c>，无法运行时断言，故按仓内既有惯例
/// （ToastUiThreadGuardTests）读源码文本固化：定位 <c>private void OnViewModelPropertyChanged</c>
/// 起止（IndexOf 签名 → 其后第一个 4 空格缩进闭合花括号），断言方法体含编组前置。
/// </summary>
public sealed class ViewUiThreadGuardTests
{
    /// <summary>
    /// Dashboard 视图订阅 Lifecycle / Health（二者均由兄弟 Runtime Store 回流驱动），
    /// 回调里直接改控件，必须在 UI 线程编组。
    /// </summary>
    [Test]
    public async Task DashboardView_PropertyChangedHandler_IsMarshaledToUiThread()
    {
        var body = await ExtractHandlerBodyAsync(
            "DshDesktop.Presentation.Avalonia", "Features", "Dashboard", "DashboardView.axaml.cs");

        await Assert.That(body).Contains("Dispatcher.UIThread.CheckAccess()");
        await Assert.That(body).Contains("Dispatcher.UIThread.Post(");
    }

    /// <summary>
    /// Workbench 视图订阅 DshUrl（回流），回调里直接对 NativeWebView 做 Navigate / IsVisible，必须编组。
    /// 既有守卫 WorkbenchLoadingOverlayTests 锚定 <c>ApplyDshUrl</c> 的 WebView 显隐逻辑，
    /// 本加固仅路由回调，不改变该方法。
    /// </summary>
    [Test]
    public async Task WorkbenchView_PropertyChangedHandler_IsMarshaledToUiThread()
    {
        var body = await ExtractHandlerBodyAsync(
            "DshDesktop.Presentation.Avalonia", "Features", "Workbench", "WorkbenchView.axaml.cs");

        await Assert.That(body).Contains("Dispatcher.UIThread.CheckAccess()");
        await Assert.That(body).Contains("Dispatcher.UIThread.Post(");
    }

    /// <summary>
    /// Plugins 视图订阅 Plugins / UpdatablePlugins（后者回流），回调读 _searchInput.Text 并改行/计数/空态，
    /// 必须编组。
    /// </summary>
    [Test]
    public async Task PluginsView_PropertyChangedHandler_IsMarshaledToUiThread()
    {
        var body = await ExtractHandlerBodyAsync(
            "DshDesktop.Presentation.Avalonia", "Features", "Plugins", "PluginsView.axaml.cs");

        await Assert.That(body).Contains("Dispatcher.UIThread.CheckAccess()");
        await Assert.That(body).Contains("Dispatcher.UIThread.Post(");
    }

    /// <summary>
    /// 截取 <c>private void OnViewModelPropertyChanged</c> 方法体（自方法签名起至其 4 空格缩进的闭合花括号），
    /// 并先钉住该命名方法**确实被订阅**（否则删掉 <c>PropertyChanged +=</c> 一行即可让编组静默失效，
    /// 守卫却仍绿——2026-09-17 代码评审发现的旁路）。
    /// </summary>
    private static async Task<string> ExtractHandlerBodyAsync(params string[] relativeSegments)
    {
        var root = XamlScan.FindRepositoryRoot();
        await Assert.That(root).IsNotNull();

        var path = Path.Combine(new[] { root!, "src" }.Concat(relativeSegments).ToArray());
        await Assert.That(File.Exists(path)).IsTrue();

        // 与仓内既有守卫同款：先去注释再截段（.cs 文件无 XML 注释，StripComments 为无害空操作）。
        var source = XamlScan.StripComments(await File.ReadAllTextAsync(path));

        // 订阅行必须存在且指向该命名方法——方法存在但没人订阅等于没加固。
        await Assert.That(source.Contains("PropertyChanged += OnViewModelPropertyChanged", StringComparison.Ordinal))
            .IsTrue();

        const string signature = "private void OnViewModelPropertyChanged";
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        await Assert.That(start).IsGreaterThan(-1);

        // 方法体内部嵌套花括号缩进更深；4 空格缩进的 "\n    }" 即方法自身的闭合。
        var end = source.IndexOf("\n    }", start, StringComparison.Ordinal);
        await Assert.That(end).IsGreaterThan(start);

        return source[start..end];
    }
}
