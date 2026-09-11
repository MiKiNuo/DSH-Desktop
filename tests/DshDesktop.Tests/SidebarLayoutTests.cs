using DshDesktop.Presentation.Avalonia.Features.AppShell;

namespace DshDesktop.Tests;

/// <summary>
/// 侧栏折叠布局规则测试（视觉基准 docs/DSH-Desktop-UI-Prototype.html 第 64 行
/// 的窄窗降级规则：列宽 218px → 70px，且 brand-text / nav-title / nav-label /
/// nav-badge / runtime-mini 全部隐藏）。
/// </summary>
public sealed class SidebarLayoutTests
{
    [Test]
    public async Task Expanded_Width_Is218()
    {
        await Assert.That(SidebarLayout.WidthFor(collapsed: false)).IsEqualTo(218d);
    }

    [Test]
    public async Task Collapsed_Width_Is70()
    {
        // 原型 @media(max-width:900px) 的 .shell grid-template-columns:70px。
        await Assert.That(SidebarLayout.WidthFor(collapsed: true)).IsEqualTo(70d);
    }

    [Test]
    public async Task Expanded_ShowsBrandTextAndNavLabels()
    {
        await Assert.That(SidebarLayout.ShowsTextContent(collapsed: false)).IsTrue();
    }

    [Test]
    public async Task Collapsed_HidesBrandTextAndNavLabels()
    {
        // 原型折叠态：.brand-text,.nav-title,.nav-label,.nav-badge,.runtime-mini{display:none}。
        await Assert.That(SidebarLayout.ShowsTextContent(collapsed: true)).IsFalse();
    }
}
