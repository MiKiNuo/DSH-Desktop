namespace DshDesktop.Application.Runtime;

/// <summary>
/// 表示连续启动失败计数器（ADR-0004 修订注，Phase 8 Issue 04）：
/// 连续 <see cref="AutoSafeModeThreshold"/> 次启动失败且开关开启 → 触发自动安全模式；
/// 成功启动清零。仅存内存（崩溃重启后重新计数，不惩罚历史失败）。
/// </summary>
public sealed class StartupFailureTracker
{
    /// <summary>触发自动安全模式的连续失败阈值（ADR-0004 修订注：2 次）。</summary>
    public const int AutoSafeModeThreshold = 2;

    /// <summary>获取当前连续失败次数。</summary>
    public int ConsecutiveFailures { get; private set; }

    /// <summary>
    /// 记录一次启动失败。
    /// </summary>
    /// <param name="autoSafeModeEnabled">"异常启动自动进入安全模式"开关当前值。</param>
    /// <returns>达到阈值且开关开启时返回 true（应进入安全模式并通知）。</returns>
    public bool RecordFailure(bool autoSafeModeEnabled)
    {
        ConsecutiveFailures++;
        return autoSafeModeEnabled && ConsecutiveFailures >= AutoSafeModeThreshold;
    }

    /// <summary>
    /// 记录一次成功启动：清零计数器。
    /// </summary>
    public void RecordSuccess()
    {
        ConsecutiveFailures = 0;
    }
}
