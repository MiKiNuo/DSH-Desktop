using DshDesktop.Presentation.Avalonia.Features.AppShell;

namespace DshDesktop.Tests;

/// <summary>
/// 壳页标题映射测试：文案与原型 DSH-Desktop-UI-Prototype.html 的 titles 表逐字一致，
/// 映射逻辑位于表现层（可测），导航按钮文案由 MainWindow 构造时写入、禁止 View 硬编码。
/// 副标题映射随页标题条一并移除，不再覆盖。
/// </summary>
public sealed class ShellPageTextTests
{
    [Test]
    [Arguments(ShellPage.Dashboard, "概览")]
    [Arguments(ShellPage.Workbench, "DSH 工作台")]
    [Arguments(ShellPage.Plugins, "插件管理")]
    [Arguments(ShellPage.Runtime, "运行环境")]
    [Arguments(ShellPage.Updates, "更新中心")]
    [Arguments(ShellPage.Diagnostics, "诊断中心")]
    [Arguments(ShellPage.Settings, "设置")]
    public async Task PageTitle_MatchesPrototypeTitles(ShellPage page, string title)
    {
        await Assert.That(ShellPageText.Title(page)).IsEqualTo(title);
    }

    [Test]
    public async Task PageTitle_CoversEveryShellPage()
    {
        foreach (ShellPage page in Enum.GetValues<ShellPage>())
        {
            await Assert.That(ShellPageText.Title(page)).IsNotNullOrEmpty();
        }
    }
}
