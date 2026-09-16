using DshDesktop.Application.Runtime;

namespace DshDesktop.Tests;

/// <summary>
/// BoundedRecoveryPlanner 测试（ADR-0007）：Failed 后的自动恢复上限恒定 1 次，
/// 且成功启动后重置——保证不形成 ADR-0004 所禁止的崩溃重启循环。
/// </summary>
public sealed class BoundedRecoveryPlannerTests
{
    /// <summary>进入 Failed 的第一次询问必须放行。</summary>
    [Test]
    public async Task TryBeginAttempt_FirstFailure_ReturnsTrue()
    {
        BoundedRecoveryPlanner planner = new();

        await Assert.That(planner.TryBeginAttempt()).IsTrue();
    }

    /// <summary>同一失败周期内第二次询问必须拒绝（上限 = 1，这是 ADR-0004 禁令之下的最小放宽）。</summary>
    [Test]
    public async Task TryBeginAttempt_SecondCallInSameFailureCycle_ReturnsFalse()
    {
        BoundedRecoveryPlanner planner = new();
        planner.TryBeginAttempt();

        await Assert.That(planner.TryBeginAttempt()).IsFalse();
    }

    /// <summary>成功启动后重置：下一次故障必须重新获得一次恢复机会（否则第二次故障就永久放弃了）。</summary>
    [Test]
    public async Task Reset_AfterSuccess_AllowsNextRecovery()
    {
        BoundedRecoveryPlanner planner = new();
        planner.TryBeginAttempt();
        await Assert.That(planner.TryBeginAttempt()).IsFalse();

        planner.Reset();

        await Assert.That(planner.TryBeginAttempt()).IsTrue();
    }

    /// <summary>上限契约钉死为 1；改动它必须同步改 ADR-0007，故用断言守卫。</summary>
    [Test]
    public async Task MaxAttemptsPerFailure_IsOne()
    {
        await Assert.That(BoundedRecoveryPlanner.MaxAttemptsPerFailure).IsEqualTo(1);
    }
}
