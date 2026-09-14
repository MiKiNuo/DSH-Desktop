using System.Globalization;
using System.Text.RegularExpressions;

namespace DshDesktop.Tests;

/// <summary>
/// 壳顶部导航结构守卫（顶部导航改造）。
///
/// 背景：导航由左侧 248px 竖排侧栏改为顶部横排，侧栏的折叠状态机
/// （SidebarCollapsed / ToggleSidebar / SidebarLayout）整块移除。
/// 这些是 **XAML 编译器看不见** 的拓扑约束——导航退回侧栏、导航项移出顶栏容器，
/// 编译都照过，只有肉眼才发现，故由本测试固化。
/// 视觉效果仍需实机确认（沙箱无法截图 Avalonia 窗口）。
/// </summary>
public sealed partial class ShellTopNavTests
{
    /// <summary>顶栏单行需容纳：品牌 + 7 项导航 + 右侧动作，约 980px。</summary>
    private const int MinimumFittingWidth = 1100;

    /// <summary>导航行高度，同时作为扩展标题栏的高度提示。</summary>
    private const int TopNavHeight = 52;

    /// <summary>顶栏容器为避让 caption 按钮区而右内缩的像素数（含左/上/下 0 的 Thickness 字符串）。</summary>
    private const string TopNavRightPadding = "0,0,140,0";

    private static readonly string[] NavButtonNames =
    [
        "NavDashboard",
        "NavWorkbench",
        "NavPlugins",
        "NavRuntime",
        "NavUpdates",
        "NavDiagnostics",
        "NavSettings",
    ];

    [GeneratedRegex("MinWidth=\"(\\d+)\"")]
    private static partial Regex MinWidthAttribute();

    [GeneratedRegex("Grid\\.Row=\"(\\d+)\"")]
    private static partial Regex GridRowAttribute();

    /// <summary>
    /// 侧栏已废弃：主窗口不得再出现 <c>Classes="sidebar"</c>。
    /// 回退到竖排侧栏会连带复活已删除的折叠状态机，故守住这条。
    /// </summary>
    [Test]
    public async Task Shell_HasNoSidebarRail()
    {
        var text = XamlScan.StripComments(await MainWindowXamlAsync());

        await Assert.That(text.Contains("Classes=\"sidebar\"", StringComparison.Ordinal)).IsFalse();
    }

    /// <summary>
    /// 全部 7 个导航按钮都必须位于顶部导航容器（<c>Classes="topnav"</c>）之内：
    /// 各自 <c>x:Name</c> 的出现位置晚于容器开标签。
    /// </summary>
    [Test]
    public async Task Shell_NavigationLivesInTopNavContainer()
    {
        var text = XamlScan.StripComments(await MainWindowXamlAsync());

        var containerAt = text.IndexOf("Classes=\"topnav\"", StringComparison.Ordinal);
        await Assert.That(containerAt).IsGreaterThan(-1);

        var outside = NavButtonNames
            .Where(name => text.IndexOf($"x:Name=\"{name}\"", StringComparison.Ordinal) < containerAt)
            .ToList();

        await Assert.That(string.Join(", ", outside)).IsEmpty();
    }

    /// <summary>
    /// 最小窗口宽度必须容得下顶栏单行：低于阈值时品牌 + 导航 + 右侧动作会被挤爆裁切。
    /// </summary>
    [Test]
    public async Task Shell_MinWidth_FitsTopNavRow()
    {
        var text = XamlScan.StripComments(await MainWindowXamlAsync());

        var match = MinWidthAttribute().Match(text);
        await Assert.That(match.Success).IsTrue();

        await Assert.That(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .IsGreaterThanOrEqualTo(MinimumFittingWidth);
    }

    /// <summary>
    /// 导航必须**占据窗口最顶部那一行**，而不是退到系统标题栏下方另起一行。
    ///
    /// Avalonia 需要客户区扩展到装饰区才会发生这件事；缺了 <c>ExtendClientAreaToDecorationsHint</c>，
    /// 导航会落回标题栏下方成为独立一行。
    ///
    /// 装饰必须用系统绘制（<c>WindowDecorations="Full"</c>）：<c>BorderOnly</c> 在实机上被验证
    /// **并不会**去掉原生标题栏——窗口渲染成了两行（顶部一行原生标题栏带 min/max/close，应用顶栏被
    /// 挤到第二行），caption 按钮也落到了原生那一行，用户看到的就是「按钮消失」。故固定用 Full。
    /// ⚠️ 这条只守属性值；视觉两行 vs 单行的回归面只有实机能看出来。
    /// </summary>
    [Test]
    public async Task Shell_ExtendsClientAreaIntoTitleBar()
    {
        var text = XamlScan.StripComments(await MainWindowXamlAsync());

        await Assert.That(
            text.Contains("ExtendClientAreaToDecorationsHint=\"True\"", StringComparison.Ordinal)).IsTrue();

        await Assert.That(
            text.Contains(
                $"ExtendClientAreaTitleBarHeightHint=\"{TopNavHeight}\"",
                StringComparison.Ordinal)).IsTrue();

        // 系统绘制装饰：BorderOnly 在实机上验证无效（仍留原生标题栏、顶栏被挤到第二行），故用 Full。
        // 这条只证明属性值；caption 按钮的可见性只有实机能确认。
        await Assert.That(text.Contains("WindowDecorations=\"Full\"", StringComparison.Ordinal)).IsTrue();
    }

    /// <summary>
    /// 页标题条（<c>Border.pagebar</c>，30px）已移除：顶栏之下直接是内容区。
    ///
    /// 用户诉求是「上面一条菜单、中间直接是原生 DSH harness」——标题条占的正是 harness 上方
    /// 那一行。它复活后编译照过，只是工作台页顶部多出 30px 非 harness 区域，故由本测试固化。
    ///
    /// 行拓扑一并守住：删掉一行定义却漏改 <c>Grid.Row</c> 索引时，内容区会塌到标题条那一行的
    /// 高度，而编译与其余测试都照过（本次改造实际踩到过）。
    /// </summary>
    [Test]
    public async Task Shell_HasNoPageBar()
    {
        var text = XamlScan.StripComments(await MainWindowXamlAsync());

        await Assert.That(text.Contains("Classes=\"pagebar\"", StringComparison.Ordinal)).IsFalse();
        await Assert.That(text.Contains("RowDefinitions=\"52,*,28\"", StringComparison.Ordinal)).IsTrue();

        await Assert.That(RowIndex(text, "<ContentControl[^>]*x:Name=\"RootContent\"[^>]*>")).IsEqualTo("1");
        await Assert.That(RowIndex(text, "<Border[^>]*Classes=\"statusbar\"[^>]*>")).IsEqualTo("2");
    }

    /// <summary>
    /// 重复的品牌文字（<c>Classes="brand-title" Text="DSH Desktop"</c>）不得回到顶栏：
    /// 它会与原生窗口标题（Title="DSH Desktop"）叠印成重影，正是最初的投诉来源。
    /// 品牌只保留 logo 瓦片（<c>brand-mark</c>）。
    /// ⚠️ 本守卫的边界：只查「元素里」是否出现 brand-title 类名与 "DSH Desktop" 文字；
    /// 因 XamlScan.StripComments 会剥离 XML 注释，若把这段内容重新塞进注释则本测试漏检。
    /// </summary>
    [Test]
    public async Task Shell_HasNoBrandTitle()
    {
        var text = XamlScan.StripComments(await MainWindowXamlAsync());

        await Assert.That(text.Contains("Classes=\"brand-title\"", StringComparison.Ordinal)).IsFalse();
        await Assert.That(text.Contains("Text=\"DSH Desktop\"", StringComparison.Ordinal)).IsFalse();
    }

    /// <summary>
    /// 右侧动作通过「顶栏容器整体内缩 140px」避让 caption 区，不再依赖运行时
    /// <c>Window.WindowDecorationMargin</c> 绑定——后者在 <c>WindowDecorations="Full"</c> + Windows 下
    /// binding 拿到的值不可靠（2026-09-14 实机回归：按钮被压扁到只剩一个图标）。
    /// 此语义反转自 <c>Shell_RightActionAvoidsCaptionAreaWithoutHardcoding</c>：
    /// 不再硬编码单按钮 Margin，而改为容器 Padding 整体内缩。
    /// </summary>
    [Test]
    public async Task Shell_RightActionsAvoidCaptionAreaViaTopNavPadding()
    {
        var text = XamlScan.StripComments(await MainWindowXamlAsync());

        // 必须改用顶栏容器内缩 140 的方案；禁止回到运行时 binding（已实机证伪）。
        await Assert.That(text.Contains($"Padding=\"{TopNavRightPadding}\"", StringComparison.Ordinal)).IsTrue();
        await Assert.That(text.Contains("$parent[Window].WindowDecorationMargin", StringComparison.Ordinal)).IsFalse();
    }

    /// <summary>
    /// 「打开工作台」按钮自身不得携带 Margin——避让 caption 区由顶栏容器统一内缩承担
    /// （<c>Border.topnav Padding</c>），单按钮 Margin 会让它在 caption 按钮左侧被压扁。
    /// 2026-09-14 实机回归：<c>Margin="{Binding $parent[Window].WindowDecorationMargin}"</c>
    /// 在 WindowDecorations="Full" + Windows 下 binding 值不可靠，按钮被压到只剩图标。
    /// </summary>
    [Test]
    public async Task Shell_OpenWorkbenchButton_HasNoMargin()
    {
        var text = XamlScan.StripComments(await MainWindowXamlAsync());

        // OpenWorkbenchButton 的开标签里不得出现 Margin= 属性。
        string openWorkbenchTag = ElementTag(text, "<Button[^>]*x:Name=\"OpenWorkbenchButton\"[^>]*>");
        await Assert.That(openWorkbenchTag).IsNotEmpty();
        await Assert.That(openWorkbenchTag.Contains("Margin=", StringComparison.Ordinal)).IsFalse();
    }

    /// <summary>
    /// 顶栏容器统一内缩 140px 避让 caption 区——<c>Border.topnav Padding="0,0,140,0"</c>。
    /// 与上一轮「WindowDecorationMargin 绑定」等价但更可靠（不依赖运行时 StyledProperty 赋值时序）；
    /// 140px 与上一次写死 <c>Margin="0,0,140,0"</c> 的验证值保持一致（实机已确认可用）。
    /// </summary>
    [Test]
    public async Task Shell_TopNavHasRightPaddingOf140()
    {
        var text = XamlScan.StripComments(await MainWindowXamlAsync());

        string topnavTag = ElementTag(text, "<Border[^>]*Classes=\"topnav\"[^>]*>");
        await Assert.That(topnavTag).IsNotEmpty();
        await Assert.That(topnavTag.Contains($"Padding=\"{TopNavRightPadding}\"", StringComparison.Ordinal)).IsTrue();
    }

    /// <summary>
    /// 取元素开标签里声明的 <c>Grid.Row</c>（元素缺失或未声明行号时返回空串，断言即以实际值报错）。
    /// </summary>
    private static string RowIndex(string text, string elementPattern)
    {
        Match row = GridRowAttribute().Match(ElementTag(text, elementPattern));
        return row.Success ? row.Groups[1].Value : string.Empty;
    }

    /// <summary>
    /// 取首个匹配元素的开标签文本（未匹配到时返回空串，断言即以实际值报错）。
    /// </summary>
    private static string ElementTag(string text, string elementPattern)
    {
        Match element = Regex.Match(text, elementPattern);
        return element.Success ? element.Value : string.Empty;
    }

    private static async Task<string> MainWindowXamlAsync()
    {
        var root = XamlScan.FindRepositoryRoot();
        await Assert.That(root).IsNotNull();

        var path = Path.Combine(root!, "src", "DshDesktop.App", "MainWindow.axaml");
        await Assert.That(File.Exists(path)).IsTrue();
        return await File.ReadAllTextAsync(path);
    }
}
