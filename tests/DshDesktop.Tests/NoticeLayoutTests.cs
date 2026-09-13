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

    [GeneratedRegex("<Style\\s+Selector=\"([^\"]+)\"\\s*>")]
    private static partial Regex StyleHeader();

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

        var style = ExtractStyleBlock(
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

    private static string ExtractStyleBlock(string text, string selector)
    {
        var match = StyleHeader().Matches(text)
            .FirstOrDefault(m => m.Groups[1].Value == selector);

        if (match is null)
        {
            return string.Empty;
        }

        var end = text.IndexOf("</Style>", match.Index, StringComparison.Ordinal);
        return end < 0 ? string.Empty : text[match.Index..end];
    }
}
