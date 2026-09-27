namespace DshDesktop.Tests;

/// <summary>
/// 诊断中心「实时诊断日志」列表滚动结构守卫。
///
/// 背景（2026-09-28 实机投诉「日志信息满一页后不能滚动」）：ListBox 自身滚动依赖**有界高度链**——
/// 只有 ScrollViewer 拿到有限可用高，视口才小于内容高，滚动范围才非零。
/// 纵向 <c>StackPanel</c> 沿堆叠轴以「无限高」测量子级（Avalonia StackPanel 语义），会打断这条链：
/// 实测 console 被 Arrange 到 7428px 而 panel 仅 640px，ScrollViewer 视口 == 内容（7400/7400）
/// ⇒ 滚动范围 0、Offset 被钳回 0；溢出部分被 <c>Border.panel</c> 的 ClipToBounds 静默裁掉，
/// 且诊断页是全仓唯一**没有**外层 ScrollViewer 兜底的页面（其余 5 页都有），于是表现为「超一页即冻结」。
/// 附带后果：View 层的 ScrollToEnd/ScrollIntoView 自动滚底一直是 no-op。
/// 设计基线 <c>docs/DSH-Desktop-UI-Redesign.html</c> 的 <c>.console{height:400px;overflow-y:auto}</c>
/// 亦要求 console 固定高 + 内部滚动。
///
/// 这是 XAML 编译器看不见的语义约束（改回 StackPanel 编译照过、零告警），故由本测试固化。
/// 断言前先剥注释并把所有空白折叠掉，故不依赖换行（CRLF/LF）、缩进与注释文本。
/// 局限：结构性守卫，不驱动真实布局；行为级锁死需 Avalonia.Headless 基建（测试项目现无）。
/// </summary>
public sealed class DiagnosticsConsoleScrollGuardTests
{
    /// <summary>
    /// panel-body 的直接子级必须就是声明了星号行的 Grid，且它的第一个子级是 console。
    /// 一条断言同时锁住三件事：中间没有插入任何包裹容器（如纵向 StackPanel）、
    /// 星号行已声明、console 占的是星号行那一格。
    /// </summary>
    [Test]
    public async Task DiagnosticsView_LogList_IsNotWrappedByUnboundedContainer()
    {
        string xaml = await ReadCompactedViewAsync();

        await Assert.That(xaml.Contains(
            "<BorderClasses=\"panel-body\"Grid.Row=\"1\"><GridRowDefinitions=\"*,Auto\"><BorderClasses=\"console\"",
            StringComparison.Ordinal)).IsTrue();
    }

    /// <summary>
    /// 行归属必须显式固化：console 在星号行（<c>Grid.Row="0"</c>）、EmptyState 在其下的 Auto 行。
    /// 只声明 <c>RowDefinitions</c> 而不固化谁占哪一行时，两行对调同样编译通过、零告警，
    /// 而 console 一旦落到 Auto 行，滚动范围再次归零——与本 bug 同一失效模式。
    /// EmptyState 位于 EntriesList 之后，故必须在整份文件上断言，不能只查 panel-body 到列表之间。
    /// </summary>
    [Test]
    public async Task DiagnosticsView_ConsoleOwnsStarRow_EmptyStateSitsBelow()
    {
        string xaml = await ReadCompactedViewAsync();

        await Assert.That(xaml.Contains(
            "<BorderClasses=\"console\"Grid.Row=\"0\">", StringComparison.Ordinal)).IsTrue();
        await Assert.That(xaml.Contains(
            "<Borderx:Name=\"EmptyState\"Classes=\"empty\"Grid.Row=\"1\"", StringComparison.Ordinal)).IsTrue();
    }

    /// <summary>
    /// 读取视图 XAML，剥注释后**折叠全部空白**：断言因此不受换行符（本仓源文件 LF，签出可能变 CRLF）、
    /// 缩进与后续重排影响。文件缺失即断言失败，避免守卫静默变成永远通过。
    /// </summary>
    private static async Task<string> ReadCompactedViewAsync()
    {
        var root = XamlScan.FindRepositoryRoot();
        await Assert.That(root).IsNotNull();

        string path = Path.Combine(
            root!,
            "src",
            "DshDesktop.Presentation.Avalonia",
            "Features",
            "Diagnostics",
            "DiagnosticsView.axaml");
        await Assert.That(File.Exists(path)).IsTrue();

        string text = XamlScan.StripComments(await File.ReadAllTextAsync(path));
        return string.Concat(text.Where(static c => !char.IsWhiteSpace(c)));
    }
}
