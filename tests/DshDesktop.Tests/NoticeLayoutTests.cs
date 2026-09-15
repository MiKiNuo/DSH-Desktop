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
/// ④ 状态机 stepper 两行共享同一 Grid 列轴（标签列 / 流转列）且整组居中。
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
    /// 状态机 stepper 的两行流转（正常 / 异常）必须共享同一对齐轴：
    /// 两行并入同一个 Grid（ColumnDefinitions 两列 = 标签列 / 流转列），标签各占第 0 列、
    /// 行内容各占第 1 列，且该 Grid 整体 HorizontalAlignment=Center。
    /// 两行标签与其行容器是同一 Grid 的兄弟节点，行容器在标签之后。
    /// 背景（2026-09-15 实机投诉）：两行各自 Center 时因行宽不等而左边界错位。
    /// </summary>
    [Test]
    public async Task FsmStepperRows_ShareAlignAxis()
    {
        var root = XamlScan.FindRepositoryRoot();
        await Assert.That(root).IsNotNull();

        var viewPath = Path.Combine(
            root!, "src", "DshDesktop.Presentation.Avalonia", "Features", "Runtime", "RuntimeView.axaml");
        await Assert.That(File.Exists(viewPath)).IsTrue();

        // 注释里也写了"正常流转""异常流转"字样，先剥注释再定位真实标签。
        var text = XamlScan.StripComments(await File.ReadAllTextAsync(viewPath));

        // ① 承载两行的是同一个 Grid：两列（标签列 / 流转列）+ 整组居中，
        //    行宽不等（正常行 4 pill+3 箭头 / 异常行 2 pill+1 箭头+提示）不再导致左边界错位。
        var firstLabel = text.IndexOf("正常流转", StringComparison.Ordinal);
        await Assert.That(firstLabel).IsGreaterThan(-1);

        var gridStart = text.LastIndexOf("<Grid", firstLabel, StringComparison.Ordinal);
        await Assert.That(gridStart).IsGreaterThan(-1);

        var gridTag = text[gridStart..(text.IndexOf(">", gridStart, StringComparison.Ordinal) + 1)];
        await Assert.That(gridTag.Contains("ColumnDefinitions=\"Auto,Auto\"", StringComparison.Ordinal)).IsTrue();
        await Assert.That(gridTag.Contains("HorizontalAlignment=\"Center\"", StringComparison.Ordinal)).IsTrue();

        // ② 两行都必须显式落在同一对列上（标签第 0 列 / 流转容器第 1 列），且分属不同 Grid 行
        //    ——若两行被放回同一行会重叠，仅查列会漏掉该回归，故这里同时钉住行归属；
        //    另断言该行首枚 pill 确实位于它的流转容器之后（防「Grid.Column 挂在无关面板上」的假绿）。
        //    行容器是标签的兄弟节点（在标签之后），故这里向后查找 <StackPanel。
        foreach (var (label, firstPill, row) in new[] { ("正常流转", "PillStopped", "0"), ("异常流转", "PillFailed", "1") })
        {
            var labelIndex = text.IndexOf(label, StringComparison.Ordinal);
            await Assert.That(labelIndex).IsGreaterThan(-1);

            var labelTag = NearestOpenTag(text, "<TextBlock", labelIndex);
            await Assert.That(labelTag.Contains("Grid.Column=\"0\"", StringComparison.Ordinal)).IsTrue();

            var rowPanelStart = text.IndexOf("<StackPanel", labelIndex, StringComparison.Ordinal);
            await Assert.That(rowPanelStart).IsGreaterThan(labelIndex);

            var rowTag = text[rowPanelStart..(text.IndexOf(">", rowPanelStart, StringComparison.Ordinal) + 1)];
            await Assert.That(rowTag.Contains("Grid.Column=\"1\"", StringComparison.Ordinal)).IsTrue();

            // 行归属：第 0 行省略 Grid.Row（默认 0），第 1 行必须显式 Grid.Row="1"。
            await Assert.That(rowTag.Contains($"Grid.Row=\"{row}\"", StringComparison.Ordinal))
                .IsEqualTo(row == "1");

            var pillIndex = text.IndexOf($"x:Name=\"{firstPill}\"", StringComparison.Ordinal);
            await Assert.That(pillIndex).IsGreaterThan(rowPanelStart);
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

    /// <summary>取 index 之前最近的某个开标签（如 "&lt;StackPanel"）的完整开标签文本（含 "&gt;"）。</summary>
    private static string NearestOpenTag(string text, string openTagStart, int index)
    {
        var tagStart = text.LastIndexOf(openTagStart, index, StringComparison.Ordinal);
        var tagEnd = text.IndexOf(">", tagStart, StringComparison.Ordinal);
        return text[tagStart..(tagEnd + 1)];
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
