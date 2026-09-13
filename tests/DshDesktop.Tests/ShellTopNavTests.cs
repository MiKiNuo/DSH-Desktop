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

    private static async Task<string> MainWindowXamlAsync()
    {
        var root = XamlScan.FindRepositoryRoot();
        await Assert.That(root).IsNotNull();

        var path = Path.Combine(root!, "src", "DshDesktop.App", "MainWindow.axaml");
        await Assert.That(File.Exists(path)).IsTrue();
        return await File.ReadAllTextAsync(path);
    }
}
