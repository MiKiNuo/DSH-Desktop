using DshDesktop.Domain.Runtime;
using DshDesktop.Presentation.Avalonia.Features.AppShell;

namespace DshDesktop.Tests;

/// <summary>
/// 壳 toast 判定纯函数投影器测试（候选 5：边沿判定自 MainWindow 下沉，文案逐字沿用其现状）。
/// 全部用例只走纯函数，不依赖 UI 线程与调度器。
/// </summary>
public sealed class ShellToastProjectorTests
{
    // ===== 徽标上升沿 =====

    [Test]
    public async Task BadgeRisingEdge_Rising_ProducesDiscoveryToast()
    {
        // current > previous → "发现 {current} 项可用更新"。
        var text = ShellToastProjector.ProjectBadgeRisingEdge(0, 3);
        await Assert.That(text).IsEqualTo("发现 3 项可用更新");
    }

    [Test]
    public async Task BadgeRisingEdge_Equal_DoesNotToast()
    {
        // 首帧/持平：previous == current 不弹。
        await Assert.That(ShellToastProjector.ProjectBadgeRisingEdge(3, 3)).IsNull();
    }

    [Test]
    public async Task BadgeRisingEdge_Falling_DoesNotToast()
    {
        await Assert.That(ShellToastProjector.ProjectBadgeRisingEdge(5, 2)).IsNull();
    }

    // ===== Runtime 生命周期迁移 =====

    [Test]
    public async Task Lifecycle_RecoveringToRunning_ProducesRecoveredToast()
    {
        // previous==Recovering && current==Running → "Runtime 已恢复运行"。
        var text = ShellToastProjector.ProjectLifecycleRecovered(RuntimeLifecycle.Recovering, RuntimeLifecycle.Running);
        await Assert.That(text).IsEqualTo("Runtime 已恢复运行");
    }

    [Test]
    public async Task Lifecycle_OtherTransitions_DoNotToast()
    {
        await Assert.That(ShellToastProjector.ProjectLifecycleRecovered(RuntimeLifecycle.Stopped, RuntimeLifecycle.Running)).IsNull();
        await Assert.That(ShellToastProjector.ProjectLifecycleRecovered(RuntimeLifecycle.Running, RuntimeLifecycle.Running)).IsNull();
        await Assert.That(ShellToastProjector.ProjectLifecycleRecovered(RuntimeLifecycle.Recovering, RuntimeLifecycle.Stopped)).IsNull();
        await Assert.That(ShellToastProjector.ProjectLifecycleRecovered(RuntimeLifecycle.Stopped, RuntimeLifecycle.Stopped)).IsNull();
    }
}
