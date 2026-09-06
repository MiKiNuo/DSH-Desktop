using DshDesktop.Presentation.Avalonia.Features.AppShell;

namespace DshDesktop.Tests;

/// <summary>
/// 壳页标题/副标题映射测试（Phase 8 Issue 02）：文案与原型 DSH-Desktop-UI-Prototype.html
/// 的 titles 表逐字一致，映射逻辑位于表现层（可测），禁止 View 硬编码。
/// </summary>
public sealed class ShellPageTextTests
{
    [Test]
    [Arguments(ShellPage.Dashboard, "概览", "DSH Desktop 运行状态与快捷入口")]
    [Arguments(ShellPage.Workbench, "DSH 工作台", "官方 Web UI · NativeWebView")]
    [Arguments(ShellPage.Plugins, "插件管理", "独立于 DSH Web UI 的原生插件管理")]
    [Arguments(ShellPage.Runtime, "运行环境", "DSH Runtime 生命周期与恢复策略")]
    [Arguments(ShellPage.Updates, "更新中心", "Desktop、DSH Runtime 与插件独立更新")]
    [Arguments(ShellPage.Diagnostics, "诊断中心", "启动性能、错误与 Runtime 日志")]
    [Arguments(ShellPage.Settings, "设置", "数据目录、桌面行为与更新策略")]
    public async Task PageText_MatchesPrototypeTitles(ShellPage page, string title, string subtitle)
    {
        await Assert.That(ShellPageText.Title(page)).IsEqualTo(title);
        await Assert.That(ShellPageText.Subtitle(page)).IsEqualTo(subtitle);
    }

    [Test]
    public async Task PageText_CoversEveryShellPage()
    {
        foreach (ShellPage page in Enum.GetValues<ShellPage>())
        {
            await Assert.That(ShellPageText.Title(page)).IsNotNullOrEmpty();
            await Assert.That(ShellPageText.Subtitle(page)).IsNotNullOrEmpty();
        }
    }
}
