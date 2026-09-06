using DshDesktop.Application.Updates;

namespace DshDesktop.Tests;

/// <summary>
/// Phase 8 评审 F2（Spec c.2）：两个更新检查时机独立——
/// 启动时开 = 启动早期即检查（§34 知情破例）；后台开 = UI Ready 后检查；都关 = 不检查；同开不重复。
/// </summary>
public sealed class UpdateCheckScheduleTests
{
    [Test]
    public async Task BothOff_NoCheck()
    {
        var plan = UpdateCheckSchedule.Plan(checkOnStartup: false, backgroundCheck: false);

        await Assert.That(plan.AtStartup).IsFalse();
        await Assert.That(plan.AfterUiReady).IsFalse();
    }

    [Test]
    public async Task StartupOnly_ChecksEarly()
    {
        var plan = UpdateCheckSchedule.Plan(checkOnStartup: true, backgroundCheck: false);

        await Assert.That(plan.AtStartup).IsTrue();
        await Assert.That(plan.AfterUiReady).IsFalse();
    }

    [Test]
    public async Task BackgroundOnly_ChecksAfterUiReady()
    {
        var plan = UpdateCheckSchedule.Plan(checkOnStartup: false, backgroundCheck: true);

        await Assert.That(plan.AtStartup).IsFalse();
        await Assert.That(plan.AfterUiReady).IsTrue();
    }

    [Test]
    public async Task BothOn_StartupCheckCovers_NoDuplicate()
    {
        var plan = UpdateCheckSchedule.Plan(checkOnStartup: true, backgroundCheck: true);

        await Assert.That(plan.AtStartup).IsTrue();
        await Assert.That(plan.AfterUiReady).IsFalse();
    }
}
