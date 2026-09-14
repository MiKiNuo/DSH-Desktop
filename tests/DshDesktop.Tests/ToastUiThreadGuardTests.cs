namespace DshDesktop.Tests;

/// <summary>
/// 壳层 toast 线程编组守卫（2026-09-14 实机崩溃回归）。
///
/// 背景：MainWindow 直接订阅 <c>store.States</c>（Plugins / Runtime），而 <c>MviStore.DispatchAsync</c>
/// 在**派发线程**上同步发布 State；组合根又是在后台线程派发 intent 的（插件编排 OperationChanged、
/// Runtime 快照回流）。这两个订阅回调因此运行在线程池线程上，只要 toast 逻辑直接改 Avalonia 控件，
/// 就会抛 <c>InvalidOperationException: The calling thread cannot access this object because
/// a different thread owns it.</c>（R3 的默认未处理异常处理器只会把日志打出来，用户看不到任何提示）。
///
/// 崩溃点在 <c>MainWindow.ShowToast</c>，是全部 3 个 toast 场景（插件终态 / 更新徽标 / Runtime
/// 恢复）的唯一收口处，故只需守住这一个方法必须做 UI 线程编组。
///
/// 测试项目不引用 <c>DshDesktop.App</c>，无法运行时断言，故按仓内既有惯例
/// （ShellTopNavTests / ThemeResourceTests）读源码文本固化。
/// </summary>
public sealed class ToastUiThreadGuardTests
{
    /// <summary>
    /// <c>ShowToast</c> 必须在非 UI 线程调用时编组到 UI 线程后再改控件。
    /// </summary>
    [Test]
    public async Task ShowToast_MarshalsToUiThread()
    {
        var body = await ShowToastBodyAsync(await MainWindowCodeBehindAsync());

        await Assert.That(body).Contains("Dispatcher.UIThread.CheckAccess()");
        await Assert.That(body).Contains("Dispatcher.UIThread.Post(");
    }

    /// <summary>
    /// 截取 <c>ShowToast</c> 方法体（自方法签名起至其 4 空格缩进的闭合花括号）。
    /// </summary>
    private static async Task<string> ShowToastBodyAsync(string source)
    {
        const string signature = "public void ShowToast(string text)";
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        await Assert.That(start).IsGreaterThan(-1);

        // 方法体内部（若）出现嵌套花括号，其缩进更深；4 空格缩进的 "}" 即方法自身的闭合。
        var end = source.IndexOf("\n    }", start, StringComparison.Ordinal);
        await Assert.That(end).IsGreaterThan(start);

        return source[start..end];
    }

    private static async Task<string> MainWindowCodeBehindAsync()
    {
        var root = XamlScan.FindRepositoryRoot();
        await Assert.That(root).IsNotNull();

        var path = Path.Combine(root!, "src", "DshDesktop.App", "MainWindow.axaml.cs");
        await Assert.That(File.Exists(path)).IsTrue();
        return await File.ReadAllTextAsync(path);
    }
}
