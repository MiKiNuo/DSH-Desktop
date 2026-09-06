using DshDesktop.Presentation.Avalonia.Features.AppShell;

namespace DshDesktop.Tests;

/// <summary>
/// 关窗处置策略测试（Phase 8 Issue 05，ADR-0005 正交语义）：
/// "最小化到托盘"只决定关窗是否拦截为隐藏；"托盘菜单退出"是显式退出意图，永不拦截。
/// </summary>
public sealed class WindowClosePolicyTests
{
    [Test]
    public async Task MinimizeOn_NormalClose_HidesToTray()
    {
        await Assert.That(WindowClosePolicy.ShouldHideToTray(true, exitRequested: false)).IsTrue();
    }

    [Test]
    public async Task MinimizeOn_TrayExit_DoesNotIntercept()
    {
        // 托盘"退出"必须能真正退出：拦截会吞掉 Shutdown 链路。
        await Assert.That(WindowClosePolicy.ShouldHideToTray(true, exitRequested: true)).IsFalse();
    }

    [Test]
    public async Task MinimizeOff_NormalClose_RealExit()
    {
        await Assert.That(WindowClosePolicy.ShouldHideToTray(false, exitRequested: false)).IsFalse();
    }

    [Test]
    public async Task MinimizeOff_TrayExit_RealExit()
    {
        await Assert.That(WindowClosePolicy.ShouldHideToTray(false, exitRequested: true)).IsFalse();
    }
}
