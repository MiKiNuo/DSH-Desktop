using System.Text.RegularExpressions;

namespace DshDesktop.Tests;

/// <summary>
/// 工作台启动加载框（小鲸鱼）结构守卫。
///
/// 背景（2026-09-15 实机投诉「启动后卡在黑黑的界面」）：内容区是 <c>NativeWebView</c>，
/// 即 Win32 原生子窗口宿主。Avalonia 官方 native-interop 文档明确：native views 永远渲染在
/// Avalonia 内容**之上**，无法用 Avalonia 控件覆盖它（airspace 约束）——因此任何遮罩层都盖不住
/// WebView，加载期只能让两者互斥：WebView 隐藏、遮罩独占内容区。
///
/// 这些是 XAML 编译器看不见的语义约束（改回去编译照过），沙箱也无法截图 Avalonia 窗口
/// （UI 只能静态审查 + 用户实机确认），故由本测试固化。
/// </summary>
public sealed partial class WorkbenchLoadingOverlayTests
{
    [GeneratedRegex("<web:NativeWebView[^>]*/>", RegexOptions.Singleline)]
    private static partial Regex SelfClosedNativeWebView();

    /// <summary>
    /// 工作台内容区不得再有不确定态进度条：它是 DshTheme 里明令禁止的反模式
    /// （来回扫的滑块会被读成「进度在动」，2026-09-15 实机投诉），且与鲸鱼遮罩功能重复。
    /// </summary>
    [Test]
    public async Task WorkbenchView_HasNoIndeterminateProgressBar()
    {
        var text = XamlScan.StripComments(await ReadViewAsync());

        await Assert.That(text.Contains("IsIndeterminate=", StringComparison.Ordinal)).IsFalse();
    }

    /// <summary>
    /// WebView 必须**初始隐藏**：这是 airspace 死结的唯一解——只要原生子窗口在场，
    /// 它就会盖住鲸鱼遮罩，用户看到的仍是黑屏。
    /// </summary>
    [Test]
    public async Task WorkbenchView_WebViewStartsHidden()
    {
        var text = XamlScan.StripComments(await ReadViewAsync());

        Match host = SelfClosedNativeWebView().Match(text);
        await Assert.That(host.Success).IsTrue();
        await Assert.That(host.Value.Contains("IsVisible=\"False\"", StringComparison.Ordinal)).IsTrue();
    }

    /// <summary>
    /// 加载遮罩、鲸鱼图标与两行文案必须在场。
    /// </summary>
    [Test]
    public async Task WorkbenchView_HasWhaleLoadingOverlay()
    {
        var text = XamlScan.StripComments(await ReadViewAsync());

        await Assert.That(text.Contains("x:Name=\"LoadingOverlay\"", StringComparison.Ordinal)).IsTrue();
        await Assert.That(text.Contains("x:Name=\"LoadingWhale\"", StringComparison.Ordinal)).IsTrue();
        await Assert.That(text.Contains("Classes=\"whale\"", StringComparison.Ordinal)).IsTrue();
        await Assert.That(text.Contains("IconWhale", StringComparison.Ordinal)).IsTrue();
        await Assert.That(text.Contains("x:Name=\"LoadingTitle\"", StringComparison.Ordinal)).IsTrue();
        await Assert.That(text.Contains("x:Name=\"LoadingHint\"", StringComparison.Ordinal)).IsTrue();
    }

    /// <summary>
    /// 遮罩只在**导航成功**时撤除：失败分支必须保留遮罩并换成失败文案。
    /// 断言用位置关系而非字符串包含——隐藏遮罩的语句必须落在 <c>if (args.IsSuccess)</c>
    /// 与其 <c>else</c> 之间，失败文案必须落在 <c>else</c> 之后。
    /// </summary>
    [Test]
    public async Task ViewCode_HidesOverlayOnlyOnSuccessfulNavigation()
    {
        var text = await ReadCodeBehindAsync();

        var success = text.IndexOf("if (args.IsSuccess)", StringComparison.Ordinal);
        var failure = text.IndexOf("else", success, StringComparison.Ordinal);
        var hide = text.IndexOf("_loadingOverlay.IsVisible = false;", StringComparison.Ordinal);
        var failedTitle = text.IndexOf("界面加载失败", StringComparison.Ordinal);

        await Assert.That(success).IsGreaterThan(-1);
        await Assert.That(failure).IsGreaterThan(success);
        await Assert.That(hide).IsGreaterThan(success);
        await Assert.That(hide).IsLessThan(failure);
        await Assert.That(failedTitle).IsGreaterThan(failure);
    }

    /// <summary>
    /// 撤遮罩必须再缩一层：只有**业务地址**在途时的成功导航才算数。
    /// Runtime 停回时会导航到 <c>about:blank</c> 复位，该次导航同样报成功；若要一并撤遮罩，
    /// 用户会在已隐藏 WebView 的内容区看到一片空白。WebView 创建时的初始空白导航同理。
    /// </summary>
    [Test]
    public async Task ViewCode_OnlyHidesOverlayForBusinessNavigation()
    {
        var text = await ReadCodeBehindAsync();

        var success = text.IndexOf("if (args.IsSuccess)", StringComparison.Ordinal);
        var failure = text.IndexOf("else", success, StringComparison.Ordinal);
        var guard = text.IndexOf("_navigatedUrl is not null", success, StringComparison.Ordinal);
        var hide = text.IndexOf("_loadingOverlay.IsVisible = false;", StringComparison.Ordinal);

        await Assert.That(guard).IsGreaterThan(success);
        await Assert.That(guard).IsLessThan(hide);
        await Assert.That(hide).IsLessThan(failure);
    }

    /// <summary>
    /// 原生 WebView 的显隐必须随 DshUrl 切换：就绪后显示（准备导航），
    /// Runtime 停回时重新隐藏。两处赋值都要在 <c>ApplyDshUrl</c> 里。
    /// </summary>
    [Test]
    public async Task ViewCode_TogglesWebViewWithRuntimeUrl()
    {
        var text = await ReadCodeBehindAsync();

        var apply = text.IndexOf("private void ApplyDshUrl", StringComparison.Ordinal);
        await Assert.That(apply).IsGreaterThan(-1);

        var next = text.IndexOf("\n    private", apply, StringComparison.Ordinal);
        var body = next > apply ? text[apply..next] : text[apply..];

        await Assert.That(body.Contains("_webViewHost.IsVisible = true;", StringComparison.Ordinal)).IsTrue();
        await Assert.That(body.Contains("_webViewHost.IsVisible = false;", StringComparison.Ordinal)).IsTrue();
    }

    /// <summary>
    /// 鲸鱼必须真的在游：样式块内须有 <c>TranslateTransform.X</c> 横向无限循环
    /// 与 <c>TranslateTransform.Y</c> 浮沉。锚在样式块内，避免蹭到别处的
    /// <c>IterationCount</c> 假绿。
    /// <para>
    /// 关键帧只能打 transform 的**分量**：打 <c>Property="RenderTransform"</c> 会让样式在
    /// 挂载阶段就解析失败，把整个应用打死在启动之前（Avalonia 未注册该动画器，
    /// 见 <c>ThemeResourceTests.NoAnimationKeyframe_TargetsRenderTransform</c>）。
    /// </para>
    /// </summary>
    [Test]
    public async Task WhaleStyle_SwimsInInfiniteLoop()
    {
        var text = XamlScan.StripComments(
            await ReadAsync("DshDesktop.Presentation.Avalonia", "Themes", "DshTheme.axaml"));

        var block = XamlScan.ExtractStyleBlock(text, "Path.whale");
        await Assert.That(block).IsNotEmpty();
        await Assert.That(block.Contains("TranslateTransform.X", StringComparison.Ordinal)).IsTrue();
        await Assert.That(block.Contains("TranslateTransform.Y", StringComparison.Ordinal)).IsTrue();
        await Assert.That(block.Contains("IterationCount=\"Infinite\"", StringComparison.Ordinal)).IsTrue();
    }

    /// <summary>
    /// 图标键必须在 <c>DshIconKeys</c> 有编译期常量——裸字符串拼错即静默无图标
    /// （<c>FindResource</c> 找不到键会抛异常）。
    /// </summary>
    [Test]
    public async Task DshIconKeys_HasWhale()
    {
        var text = await ReadAsync("DshDesktop.Presentation.Avalonia", "Themes", "DshIconKeys.cs");

        await Assert.That(text.Contains("IconWhale", StringComparison.Ordinal)).IsTrue();
    }

    private static Task<string> ReadViewAsync()
    {
        return ReadAsync(
            "DshDesktop.Presentation.Avalonia", "Features", "Workbench", "WorkbenchView.axaml");
    }

    private static Task<string> ReadCodeBehindAsync()
    {
        return ReadAsync(
            "DshDesktop.Presentation.Avalonia", "Features", "Workbench", "WorkbenchView.axaml.cs");
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
