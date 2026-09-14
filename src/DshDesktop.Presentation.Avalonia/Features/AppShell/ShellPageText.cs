namespace DshDesktop.Presentation.Avalonia.Features.AppShell;

/// <summary>
/// 表示壳页标题映射：文案逐字取自原型 docs/DSH-Desktop-UI-Prototype.html 的 titles 表；
/// 映射放表现层静态类（可测），顶部导航按钮文案以此处为单一映射源（MainWindow 构造时写入）。
/// 副标题映射（原供 30px 页标题条使用）随页标题条一并移除。
/// </summary>
public static class ShellPageText
{
    /// <summary>
    /// 取页标题（顶栏主文案）。
    /// </summary>
    /// <param name="page">壳页面。</param>
    /// <returns>页标题。</returns>
    public static string Title(ShellPage page)
    {
        return page switch
        {
            ShellPage.Dashboard => "概览",
            ShellPage.Workbench => "DSH 工作台",
            ShellPage.Plugins => "插件管理",
            ShellPage.Runtime => "运行环境",
            ShellPage.Updates => "更新中心",
            ShellPage.Diagnostics => "诊断中心",
            ShellPage.Settings => "设置",
            _ => "概览",
        };
    }
}
