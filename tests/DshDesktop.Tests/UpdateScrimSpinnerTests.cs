using System.Text.RegularExpressions;

namespace DshDesktop.Tests;

/// <summary>
/// 更新遮罩（loading 框）与进度条结构守卫：遮罩中央必须是**旋转图标**（progress spinner），
/// 仅当 Desktop 下载存在真实百分比时才切换为确定进度条。
///
/// 背景（2026-09-15 实机投诉）：遮罩里写死 <c>IsIndeterminate="True"</c> 的进度条，与 UpdatesView
/// 页面进度条（无进度数据时同样退化为不确定态）在半透明遮罩下同时可见 → 用户看到「两个进度条在闪烁」。
/// 这些是 XAML 编译器看不见的语义约束（改回去编译照过），故由本测试固化；
/// 视觉效果仍需实机确认（沙箱无法截图 Avalonia 窗口）。
/// </summary>
public sealed partial class UpdateScrimSpinnerTests
{
    [GeneratedRegex("<ProgressBar[^>]*/>", RegexOptions.Singleline)]
    private static partial Regex SelfClosedProgressBar();

    /// <summary>
    /// loading 框不得再写死不确定态进度条：来回扫的滑块正是投诉来源，遮罩应呈现旋转图标。
    /// </summary>
    [Test]
    public async Task UpdateScrim_HasNoLockedIndeterminateBar()
    {
        var text = XamlScan.StripComments(await ReadAsync("DshDesktop.App", "MainWindow.axaml"));

        await Assert.That(text.Contains("IsIndeterminate=\"True\"", StringComparison.Ordinal)).IsFalse();
    }

    /// <summary>
    /// loading 框中央必须是旋转图标（spinner，<c>x:Name="UpdateSpinner"</c>），
    /// 由 code-behind 与确定进度条互斥切换。
    /// </summary>
    [Test]
    public async Task UpdateScrim_HasSpinner()
    {
        var text = XamlScan.StripComments(await ReadAsync("DshDesktop.App", "MainWindow.axaml"));

        await Assert.That(text.Contains("x:Name=\"UpdateSpinner\"", StringComparison.Ordinal)).IsTrue();
        await Assert.That(text.Contains("Classes=\"spinner\"", StringComparison.Ordinal)).IsTrue();
    }

    /// <summary>
    /// spinner 必须真的在转：样式块内须有 <c>RotateTransform.Angle</c> 无限循环动画。
    /// 静态图标不构成 loading 指示（锚在样式块内，避免蹭到别处的 <c>IterationCount</c> 假绿）。
    /// </summary>
    [Test]
    public async Task SpinnerStyle_RotatesInfinitely()
    {
        var text = XamlScan.StripComments(
            await ReadAsync("DshDesktop.Presentation.Avalonia", "Themes", "DshTheme.axaml"));

        var at = text.IndexOf("Selector=\"Path.spinner\"", StringComparison.Ordinal);
        await Assert.That(at).IsGreaterThan(-1);

        var close = text.IndexOf("</Style>", at, StringComparison.Ordinal);
        await Assert.That(close).IsGreaterThan(at);

        var block = text[at..close];
        await Assert.That(block.Contains("RotateTransform.Angle", StringComparison.Ordinal)).IsTrue();
        await Assert.That(block.Contains("IterationCount=\"Infinite\"", StringComparison.Ordinal)).IsTrue();
    }

    /// <summary>
    /// 页面进度条只在**有真实进度数据**时出现：<c>IsVisible</c> 必须绑 <c>DesktopDownloadProgress</c>
    /// （而非 PendingOperation），并删除不确定态绑定——否则插件更新 / Runtime 安装期间它会与遮罩一起闪。
    /// </summary>
    [Test]
    public async Task UpdatesView_ProgressBarRequiresRealProgress()
    {
        var text = XamlScan.StripComments(
            await ReadAsync("DshDesktop.Presentation.Avalonia", "Features", "Updates", "UpdatesView.axaml"));

        Match bar = SelfClosedProgressBar().Match(text);
        await Assert.That(bar.Success).IsTrue();

        var tag = bar.Value;
        await Assert.That(tag.Contains("DesktopDownloadProgress", StringComparison.Ordinal)).IsTrue();
        await Assert.That(tag.Contains("PendingOperation", StringComparison.Ordinal)).IsFalse();
        await Assert.That(tag.Contains("IsIndeterminate", StringComparison.Ordinal)).IsFalse();
    }

    private static async Task<string> ReadAsync(params string[] relativeSegments)
    {
        var root = XamlScan.FindRepositoryRoot();
        await Assert.That(root).IsNotNull();

        var path = Path.Combine(new[] { root!, "src" }.Concat(relativeSegments).ToArray());
        await Assert.That(File.Exists(path)).IsTrue();
        return await File.ReadAllTextAsync(path);
    }
}
