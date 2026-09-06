namespace DshDesktop.Presentation.Avalonia.Features.AppShell;

/// <summary>
/// 表示壳页标题/副标题映射（Phase 8 Issue 02）：文案逐字取自原型
/// docs/DSH-Desktop-UI-Prototype.html 的 titles 表；映射放表现层静态类（可测），
/// 顶栏 View 只绑定 <see cref="AppShellViewModel.PageTitle"/> / <see cref="AppShellViewModel.PageSubtitle"/>。
/// Phase 8 评审 F14：侧栏导航按钮文案同样以此处为单一映射源（MainWindow 构造时写入）。
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

    /// <summary>
    /// 取页副标题（顶栏辅助文案）。
    /// </summary>
    /// <param name="page">壳页面。</param>
    /// <returns>页副标题。</returns>
    public static string Subtitle(ShellPage page)
    {
        return page switch
        {
            ShellPage.Dashboard => "DSH Desktop 运行状态与快捷入口",
            ShellPage.Workbench => "官方 Web UI · NativeWebView",
            ShellPage.Plugins => "独立于 DSH Web UI 的原生插件管理",
            ShellPage.Runtime => "DSH Runtime 生命周期与恢复策略",
            ShellPage.Updates => "Desktop、DSH Runtime 与插件独立更新",
            ShellPage.Diagnostics => "启动性能、错误与 Runtime 日志",
            ShellPage.Settings => "数据目录、桌面行为与更新策略",
            _ => "DSH Desktop 运行状态与快捷入口",
        };
    }
}
