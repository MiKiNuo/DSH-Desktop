using DshDesktop.Domain.Plugins;
using DshDesktop.Domain.Runtime;
using DshDesktop.Presentation.Avalonia.Features.AppShell;

namespace DshDesktop.Tests;

/// <summary>
/// 壳 toast 三判定纯函数投影器测试（候选 5：边沿判定自 MainWindow 下沉，文案逐字沿用其现状）。
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

    // ===== 插件终态引用去重 =====

    [Test]
    public async Task PluginOperation_NullCurrent_DoesNotToast()
    {
        var op = new PluginOperation(PluginOperationStage.Completed, "demo", null);
        await Assert.That(ShellToastProjector.ProjectPluginOperationDone(op, null)).IsNull();
    }

    [Test]
    public async Task PluginOperation_SameReference_DoesNotToast()
    {
        // 已通知过的同一引用再次流入：去重不弹。
        var op = new PluginOperation(PluginOperationStage.Completed, "demo", null);
        await Assert.That(ShellToastProjector.ProjectPluginOperationDone(op, op)).IsNull();
    }

    [Test]
    public async Task PluginOperation_CompletedNewReference_ProducesCompletionToast()
    {
        var op = new PluginOperation(PluginOperationStage.Completed, "demo-plugin", null);
        var text = ShellToastProjector.ProjectPluginOperationDone(null, op);
        await Assert.That(text).IsEqualTo("插件 demo-plugin 安装完成");
    }

    [Test]
    public async Task PluginOperation_FailedNewReference_ProducesFailureToast()
    {
        var op = new PluginOperation(PluginOperationStage.Failed, "demo-plugin", "boom", PluginOperationKind.Update);
        var text = ShellToastProjector.ProjectPluginOperationDone(null, op);
        await Assert.That(text).IsEqualTo("插件 demo-plugin 更新失败：boom");
    }

    [Test]
    public async Task PluginOperation_NonTerminalNewReference_DoesNotToast()
    {
        // 阶段推进（非终态）整体替换引用，但非 Completed/Failed，不弹（也不应标记为已通知）。
        var op = new PluginOperation(PluginOperationStage.Installing, "demo", null);
        await Assert.That(ShellToastProjector.ProjectPluginOperationDone(null, op)).IsNull();
    }
}
