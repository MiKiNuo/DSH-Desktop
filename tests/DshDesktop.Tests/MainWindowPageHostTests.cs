namespace DshDesktop.Tests;

/// <summary>
/// 壳内容区（页宿主）结构守卫。
///
/// 背景（2026-09-15 实机投诉「每次点顶部按钮都要重新加载一次」）：原 <c>RenderCurrentPage</c>
/// 每次切页都 <c>new()</c> 一个视图并整体替换 <c>RootContent.Content</c>。工作台一旦脱离视觉树，
/// <c>NativeControlHost</c> 就会销毁它的原生子窗口 —— 销毁只发生在 <c>OnDetachedFromVisualTree</c>
/// 之后的 <c>CheckDestruction</c>，而 <c>IsVisible=false</c> 只走 <c>HideWithSize</c>、不销毁 ——
/// 于是 WebView2 实例随之消失，切回来必须重新导航 = 又一次加载，DSH 页内状态也全丢。
///
/// 契约：工作台视图**常驻**视觉树，切页只切 <c>IsVisible</c>；其余页保持既有的每次重建。
/// 这些是 XAML/编译器都看不见的语义约束（改回去编译照过），App 项目又不被测试项目引用，
/// 故按本仓库既定手法由源码文本守卫固化。
/// </summary>
public sealed class MainWindowPageHostTests
{
    /// <summary>
    /// 内容区必须换成常驻宿主，且 <c>RootContent.Content</c> 只被赋值这一次——
    /// 每次切页替换 Content 就等于每次销毁原生 WebView。
    /// </summary>
    [Test]
    public async Task WorkbenchPageHost_InstallsStableContentHost()
    {
        var text = await ReadMainWindowSourceAsync();

        await Assert.That(text.Contains("private readonly Panel _pageHost = new();", StringComparison.Ordinal))
            .IsTrue();
        await Assert.That(text.Contains("_rootContent.Content = _pageHost;", StringComparison.Ordinal)).IsTrue();
        await Assert.That(CountOccurrences(text, "_rootContent.Content =")).IsEqualTo(1);
    }

    /// <summary>
    /// 工作台视图只允许在「已存在则复用」的守卫下创建，且不得再出现在 switch 的按页新建分支里。
    /// </summary>
    [Test]
    public async Task WorkbenchPageHost_WorkbenchViewIsCreatedOnceBehindGuard()
    {
        var text = await ReadMainWindowSourceAsync();

        var guard = text.IndexOf("if (_workbenchView is null)", StringComparison.Ordinal);
        var create = text.IndexOf("CreateView<WorkbenchView, WorkbenchViewModel>()", StringComparison.Ordinal);

        await Assert.That(guard).IsGreaterThan(-1);
        await Assert.That(create).IsGreaterThan(guard);
        await Assert.That(text.Contains("ShellPage.Workbench => CreateView", StringComparison.Ordinal)).IsFalse();
    }

    /// <summary>
    /// 工作台视图一旦入树就不得被移除：移除即销毁原生 WebView（本次投诉的根因）。
    /// 断言用位置关系——移除语句必须晚于「当前页不是工作台」的判断。
    /// </summary>
    [Test]
    public async Task WorkbenchPageHost_WorkbenchViewIsNeverRemoved()
    {
        var text = await ReadMainWindowSourceAsync();

        var guard = text.IndexOf("!ReferenceEquals(_currentPageView, _workbenchView)", StringComparison.Ordinal);
        var remove = text.IndexOf("_pageHost.Children.Remove(_currentPageView);", StringComparison.Ordinal);

        await Assert.That(guard).IsGreaterThan(-1);
        await Assert.That(remove).IsGreaterThan(guard);
        await Assert.That(text.Contains("Children.Remove(_workbenchView)", StringComparison.Ordinal)).IsFalse();
    }

    /// <summary>
    /// 同一时刻只允许一个页面可见。这条尤其关键：原生窗口永远渲染在 Avalonia 内容之上，
    /// 隐藏的工作台若没真的 <c>IsVisible=false</c>，它的 WebView 会盖住其它所有页。
    /// </summary>
    [Test]
    public async Task WorkbenchPageHost_OnlyCurrentPageIsVisible()
    {
        var text = await ReadMainWindowSourceAsync();

        await Assert.That(
            text.Contains("child.IsVisible = ReferenceEquals(child, view);", StringComparison.Ordinal)).IsTrue();
    }

    private static int CountOccurrences(string text, string token)
    {
        var count = 0;
        var at = 0;
        while ((at = text.IndexOf(token, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += token.Length;
        }

        return count;
    }

    private static async Task<string> ReadMainWindowSourceAsync()
    {
        var root = XamlScan.FindRepositoryRoot();
        await Assert.That(root).IsNotNull();

        var path = Path.Combine(root!, "src", "DshDesktop.App", "MainWindow.axaml.cs");
        await Assert.That(File.Exists(path)).IsTrue();
        return await File.ReadAllTextAsync(path);
    }
}
