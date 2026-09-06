using DshDesktop.Application.Runtime;

namespace DshDesktop.Tests;

/// <summary>
/// 启动失败计数器测试（ADR-0004 修订注，Phase 8 Issue 04）：
/// 连续 2 次启动失败且开关开启 → 触发自动安全模式；成功启动清零；开关关闭永不触发。
/// </summary>
public sealed class StartupFailureTrackerTests
{
    [Test]
    public async Task RecordFailure_FirstFailure_DoesNotTrigger()
    {
        var tracker = new StartupFailureTracker();

        bool trigger = tracker.RecordFailure(autoSafeModeEnabled: true);

        await Assert.That(trigger).IsFalse();
        await Assert.That(tracker.ConsecutiveFailures).IsEqualTo(1);
    }

    [Test]
    public async Task RecordFailure_SecondConsecutiveFailure_Triggers()
    {
        var tracker = new StartupFailureTracker();
        _ = tracker.RecordFailure(autoSafeModeEnabled: true);

        bool trigger = tracker.RecordFailure(autoSafeModeEnabled: true);

        await Assert.That(trigger).IsTrue();
        await Assert.That(tracker.ConsecutiveFailures).IsEqualTo(2);
    }

    [Test]
    public async Task RecordFailure_SwitchDisabled_NeverTriggers()
    {
        var tracker = new StartupFailureTracker();

        await Assert.That(tracker.RecordFailure(autoSafeModeEnabled: false)).IsFalse();
        await Assert.That(tracker.RecordFailure(autoSafeModeEnabled: false)).IsFalse();
        await Assert.That(tracker.RecordFailure(autoSafeModeEnabled: false)).IsFalse();
    }

    [Test]
    public async Task RecordSuccess_ResetsConsecutiveFailures()
    {
        var tracker = new StartupFailureTracker();
        _ = tracker.RecordFailure(autoSafeModeEnabled: true);

        tracker.RecordSuccess();

        await Assert.That(tracker.ConsecutiveFailures).IsEqualTo(0);
        // 清零后重新计数：再一次失败不触发。
        await Assert.That(tracker.RecordFailure(autoSafeModeEnabled: true)).IsFalse();
    }
}
