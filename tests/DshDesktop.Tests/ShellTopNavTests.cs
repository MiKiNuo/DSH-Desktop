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
    /// 导航必须**并入 Windows 标题栏**（窗口最顶部那一行），而不是在系统标题栏下方另起一行。
    ///
    /// Avalonia 需要客户区扩展到装饰区才会发生这件事；缺了这两个属性，导航会退回到标题栏
    /// 下方成为独立一行，且窗口出现两个品牌名（系统标题栏 + 导航条各一个）。
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

        // 保留系统 caption 按钮（用户选定方案）：Avalonia 12 用 WindowDecorations 取代了已移除的
        // ExtendClientAreaChromeHints，Full = 由系统绘制装饰。改成 None / BorderOnly 会转入「应用自绘
        // 装饰」，而本窗口并未提供 WindowDrawnDecorations 模板 → 最小化/最大化/关闭按钮会全部消失。
        await Assert.That(text.Contains("WindowDecorations=\"Full\"", StringComparison.Ordinal)).IsTrue();
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
