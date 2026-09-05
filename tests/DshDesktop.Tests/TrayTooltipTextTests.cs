using DshDesktop.Domain.Runtime;
using DshDesktop.Presentation.Avalonia.Features.AppShell;

namespace DshDesktop.Tests;

/// <summary>
/// 托盘 tooltip 状态文本测试（Phase 7 Issue 04）：生命周期 → 中文状态词的映射，纯函数直测。
/// </summary>
public sealed class TrayTooltipTextTests
{
    [Test]
    [Arguments(RuntimeLifecycle.Stopped, "DSH Desktop — 已停止")]
    [Arguments(RuntimeLifecycle.Starting, "DSH Desktop — 启动中")]
    [Arguments(RuntimeLifecycle.Running, "DSH Desktop — 运行中")]
    [Arguments(RuntimeLifecycle.Stopping, "DSH Desktop — 停止中")]
    [Arguments(RuntimeLifecycle.Recovering, "DSH Desktop — 恢复中")]
    [Arguments(RuntimeLifecycle.Failed, "DSH Desktop — 失败")]
    public async Task Lifecycle_MapsToExpectedTooltip(RuntimeLifecycle lifecycle, string expected)
    {
        await Assert.That(TrayTooltipText.Format(lifecycle)).IsEqualTo(expected);
    }

    [Test]
    public async Task UnknownLifecycle_FallsBackToStopped()
    {
        await Assert.That(TrayTooltipText.Format((RuntimeLifecycle)999)).IsEqualTo("DSH Desktop — 已停止");
    }
}
