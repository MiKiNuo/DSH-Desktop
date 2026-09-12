using DshDesktop.Presentation.Avalonia.Features.AppShell;

namespace DshDesktop.Tests;

/// <summary>
/// 侧栏折叠布局规则测试（视觉基准 docs/DSH-Desktop-UI-Redesign.html：
/// 展开 248px / 折叠 72px；折叠态隐藏品牌文案、分组标题、导航文字与徽标，
/// 底部 runtime 卡退化为居中状态点）。
/// </summary>
public sealed class SidebarLayoutTests
{
    [Test]
    public async Task Expanded_Width_Is248()
    {
        await Assert.That(SidebarLayout.WidthFor(collapsed: false)).IsEqualTo(248d);
    }

    [Test]
    public async Task Collapsed_Width_Is72()
    {
        await Assert.That(SidebarLayout.WidthFor(collapsed: true)).IsEqualTo(72d);
    }

    [Test]
    public async Task Expanded_ShowsBrandTextAndNavLabels()
    {
        await Assert.That(SidebarLayout.ShowsTextContent(collapsed: false)).IsTrue();
    }

    [Test]
    public async Task Collapsed_HidesBrandTextAndNavLabels()
    {
        await Assert.That(SidebarLayout.ShowsTextContent(collapsed: true)).IsFalse();
    }
}
