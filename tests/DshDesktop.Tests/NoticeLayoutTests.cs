using System.Text.RegularExpressions;

namespace DshDesktop.Tests;

/// <summary>
/// 提示条（.notice）布局契约守卫：全局统一样式。
///
/// 背景：<c>Border.notice</c> 此前只定义外观，没有布局契约，调用点各自手写。
/// Runtime 页安全模式提示条即受害实证：横向 <c>StackPanel</c> 里塞图标 + 长文案 +
/// 尾部按钮——长文案不换行把父级撑爆；尾部按钮的 <c>HorizontalAlignment="Right"</c>
/// 在 StackPanel 中根本不生效（主轴按期望宽度顺序排布，没有"剩余空间"概念）；
/// 溢出内容再被卡片 <c>ClipToBounds</c> 硬裁切，于是文案断掉、按钮只露半截。
///
/// 契约（XAML 编译器看不见，只有渲染时才暴露，故由测试固化）：
/// ① 主题为 notice 内 TextBlock 全局开启 TextWrapping=Wrap（根因：不换行才撑爆）；
/// ② info 型提示条（图标 + 正文 [+ 尾部操作]）统一用 Grid 三列 Auto,*,Auto；
/// ③ notice 内禁用 HorizontalAlignment=Right（Grid 三列下尾部操作自成 Auto 列）；
/// ④ 状态机 stepper 两行整体水平居中。
/// 视觉效果仍需实机确认（沙箱无法截图 Avalonia 窗口）。
/// </summary>
public sealed partial class NoticeLayoutTests
{
    private const string GridColumnsAutoStarAuto = "ColumnDefinitions=\"Auto,*,Auto\"";

    [GeneratedRegex("<Border\\b[^>]*Classes=\"[^\"]*\\bnotice\\b[^\"]*\"[^>]*>")]
    private static partial Regex NoticeBorder();

    /// <summary>
    /// 主题契约：notice 内所有 TextBlock 默认换行。
    /// 样式只能设属性、无法注入子元素，布局结构由调用点测试（见下）固化。
    /// </summary>
    [Test]
    public async Task NoticeTextStyle_EnablesTextWrapping()
    {
        var root = XamlScan.FindRepositoryRoot();
        await Assert.That(root).IsNotNull();

        var themePath = Path.Combine(
            root!, "src", "DshDesktop.Presentation.Avalonia", "Themes", "DshTheme.axaml");
        await Assert.That(File.Exists(themePath)).IsTrue();

        var style = XamlScan.ExtractStyleBlock(
            await File.ReadAllTextAsync(themePath),
            "Border.notice TextBlock");

        // Setter 语法拆成两段：Property="TextWrapping" 与 Value="Wrap"。
        await Assert.That(style).Contains("Property=\"TextWrapping\"");
        await Assert.That(style).Contains("Value=\"Wrap\"");
    }

    /// <summary>
    /// info 型提示条（图标 + 正文 [+ 尾部操作]）统一用 Grid 三列：
    /// 图标列 / 可换行正文列（*）/ 尾部操作列。两处现存调用
    /// （Runtime 安全模式、Plugins 安装提示）均已按此改造。
    /// </summary>
    [Test]
    public async Task InfoNotices_UseThreeColumnGrid()
    {
        var root = XamlScan.FindRepositoryRoot();
        await Assert.That(root).IsNotNull();

        var offenders = new List<string>();
        foreach (var file in XamlScan.EnumerateViews(root!))
        {
            var text = XamlScan.StripComments(await File.ReadAllTextAsync(file));
            foreach (Match border in NoticeBorder().Matches(text))
            {
                if (!border.Value.Contains("info", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!NoticeContent(text, border).Contains(GridColumnsAutoStarAuto, StringComparison.Ordinal))
                {
                    offenders.Add(Path.GetRelativePath(root!, file));
                }
            }
        }

        await Assert.That(string.Join(", ", offenders.Distinct())).IsEmpty();
    }

    /// <summary>
    /// notice 内禁用 HorizontalAlignment=Right：Grid 三列下尾部操作自成 Auto 列，
    /// 无需对齐属性；而在 StackPanel 中该属性本就不生效——正是裁切 bug 的成因之一。
    /// 守卫范围是全部 notice（含 err/warn 等变体）；仓内非 notice 位置的既有写法不在此列。
    /// </summary>
    [Test]
    public async Task Notices_AvoidIneffectiveRightAlignment()
    {
        var root = XamlScan.FindRepositoryRoot();
        await Assert.That(root).IsNotNull();

        var offenders = new List<string>();
        foreach (var file in XamlScan.EnumerateViews(root!))
        {
            var text = XamlScan.StripComments(await File.ReadAllTextAsync(file));
            foreach (Match border in NoticeBorder().Matches(text))
            {
                if (NoticeContent(text, border).Contains("HorizontalAlignment=\"Right\"", StringComparison.Ordinal))
                {
                    offenders.Add(Path.GetRelativePath(root!, file));
                }
            }
        }

        await Assert.That(string.Join(", ", offenders.Distinct())).IsEmpty();
    }

    /// <summary>
    /// 状态机 stepper 的两行流转（正常 / 异常）必须整体居中：
    /// 标签所在行容器声明 HorizontalAlignment=Center。
    /// </summary>
    [Test]
    public async Task FsmStepperRows_AreCentered()
    {
        var root = XamlScan.FindRepositoryRoot();
        await Assert.That(root).IsNotNull();

        var viewPath = Path.Combine(
            root!, "src", "DshDesktop.Presentation.Avalonia", "Features", "Runtime", "RuntimeView.axaml");
        await Assert.That(File.Exists(viewPath)).IsTrue();

        // 注释里也写了"正常流转"字样，先剥注释再定位真实标签。
        var text = XamlScan.StripComments(await File.ReadAllTextAsync(viewPath));
        foreach (var label in new[] { "正常流转", "异常流转" })
        {
            var index = text.IndexOf(label, StringComparison.Ordinal);
            await Assert.That(index).IsGreaterThan(-1);

            // 标签所在行容器（最近的 StackPanel 开标签）必须整体居中。
            var panelStart = text.LastIndexOf("<StackPanel", index, StringComparison.Ordinal);
            var panelTagEnd = text.IndexOf(">", panelStart, StringComparison.Ordinal);
            var panelTag = text[panelStart..(panelTagEnd + 1)];
            await Assert.That(panelTag.Contains("HorizontalAlignment=\"Center\"", StringComparison.Ordinal))
                .IsTrue();
        }
    }

    /// <summary>
    /// 状态机 stepper 尾部的说明文字（"Recovering 完成后回到 Starting"）不得挂
    /// <c>fsm-label</c> —— 该类带 <c>Width=64</c>（为两个 4 字标签对齐），长文案会被硬裁切成
    /// "Recovering 完"。长提示改挂无固定宽度的 <c>fsm-hint</c>，外观仍与标签一致。
    /// </summary>
    [Test]
    public async Task FsmStepperHint_IsNotWidthCapped()
    {
        const string hint = "Recovering 完成后回到 Starting";

        var root = XamlScan.FindRepositoryRoot();
        await Assert.That(root).IsNotNull();

        var viewPath = Path.Combine(
            root!, "src", "DshDesktop.Presentation.Avalonia", "Features", "Runtime", "RuntimeView.axaml");
        await Assert.That(File.Exists(viewPath)).IsTrue();

        var text = XamlScan.StripComments(await File.ReadAllTextAsync(viewPath));
        var index = text.IndexOf(hint, StringComparison.Ordinal);
        await Assert.That(index).IsGreaterThan(-1);

        // 提示所在 TextBlock 的开标签：取文案之前最近的一个 <TextBlock。
        var tagStart = text.LastIndexOf("<TextBlock", index, StringComparison.Ordinal);
        var tagEnd = text.IndexOf(">", tagStart, StringComparison.Ordinal);
        var tag = text[tagStart..(tagEnd + 1)];

        await Assert.That(tag.Contains("fsm-label", StringComparison.Ordinal)).IsFalse();
        await Assert.That(tag.Contains("fsm-hint", StringComparison.Ordinal)).IsTrue();
    }

    /// <summary>
    /// <c>fsm-hint</c> 必须存在且与 <c>fsm-label</c> 同观感（同样的前景色 / 字号 / 垂直居中），
    /// 但**不带** <c>Width</c>：固定宽度正是裁切根因，改回去等于回归。
    /// </summary>
    [Test]
    public async Task FsmHintStyle_HasNoFixedWidth()
    {
        var root = XamlScan.FindRepositoryRoot();
        await Assert.That(root).IsNotNull();

        var themePath = Path.Combine(
            root!, "src", "DshDesktop.Presentation.Avalonia", "Themes", "DshTheme.axaml");
        await Assert.That(File.Exists(themePath)).IsTrue();

        var style = XamlScan.ExtractStyleBlock(
            await File.ReadAllTextAsync(themePath),
            "TextBlock.fsm-hint");

        await Assert.That(style).IsNotEmpty();
        await Assert.That(style.Contains("Property=\"Foreground\"", StringComparison.Ordinal)).IsTrue();
        await Assert.That(style.Contains("Property=\"FontSize\"", StringComparison.Ordinal)).IsTrue();
        await Assert.That(style.Contains("Property=\"Width\"", StringComparison.Ordinal)).IsFalse();
    }

    /// <summary>取 notice Border 自身闭合范围内的内容（防止越界匹配到后续元素）。</summary>
    private static string NoticeContent(string text, Match border)
    {
        if (border.Value.TrimEnd().EndsWith("/>", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var end = text.IndexOf("</Border>", border.Index, StringComparison.Ordinal);
        if (end < 0)
        {
            end = Math.Min(text.Length, border.Index + 900);
        }

        return text[border.Index..Math.Min(text.Length, end)];
    }
}
