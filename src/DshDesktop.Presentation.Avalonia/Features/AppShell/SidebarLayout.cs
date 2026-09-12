namespace DshDesktop.Presentation.Avalonia.Features.AppShell;

/// <summary>
/// 表示侧栏折叠的布局规则（视觉基准 docs/DSH-Desktop-UI-Redesign.html：展开 248px /
/// 折叠 72px）。规则集中在此以便单测，MainWindow 只按结果消费，不重复硬编码。
/// </summary>
public static class SidebarLayout
{
    /// <summary>
    /// 侧栏展开宽度（原型 <c>--rail:248px</c>）。
    /// </summary>
    public const double ExpandedWidth = 248d;

    /// <summary>
    /// 侧栏折叠宽度（原型 <c>--rail:72px</c>）。
    /// </summary>
    public const double CollapsedWidth = 72d;

    /// <summary>
    /// 取当前侧栏宽度。
    /// </summary>
    /// <param name="collapsed">是否折叠。</param>
    /// <returns>侧栏列宽（像素）。</returns>
    public static double WidthFor(bool collapsed)
    {
        return collapsed ? CollapsedWidth : ExpandedWidth;
    }

    /// <summary>
    /// 取文本内容是否可见（原型折叠态隐藏 <c>.brand-text / .nav-title / .nav-label /
    /// .nav-badge / .runtime-mini</c>，即全部文字与徽标、底部状态卡）。
    /// </summary>
    /// <param name="collapsed">是否折叠。</param>
    /// <returns>文本内容可见为 true。</returns>
    public static bool ShowsTextContent(bool collapsed)
    {
        return !collapsed;
    }
}
