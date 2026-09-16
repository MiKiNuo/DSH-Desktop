namespace DshDesktop.Application.Runtime;

/// <summary>
/// 表示 Runtime 进入 Failed 之后的有界自动恢复决策器（ADR-0007）。
/// 只回答「本次故障是否还应自动重试一次」，不负责等待与派发——那是组合根的编排职责，
/// 因此本类保持纯逻辑、可直测，与 <see cref="StartupFailureTracker"/> 同一范式。
/// 上限恒定为 <see cref="MaxAttemptsPerFailure"/>：ADR-0004 禁止「崩溃自动重启循环」，
/// 本类是该禁令之下的最小放宽（一次自动兜底，之后交回人工 / 自动安全模式）。
/// </summary>
public sealed class BoundedRecoveryPlanner
{
    /// <summary>单个失败周期内允许的自动重试次数上限（ADR-0007：1 次）。</summary>
    public const int MaxAttemptsPerFailure = 1;

    private int _attempts;

    /// <summary>
    /// Runtime 进入 Failed 时询问是否应发起一次自动恢复。
    /// </summary>
    /// <returns>同一失败周期内未超过上限时返回 true 并计数；超出则返回 false。</returns>
    public bool TryBeginAttempt()
    {
        if (_attempts >= MaxAttemptsPerFailure)
        {
            return false;
        }

        _attempts++;
        return true;
    }

    /// <summary>
    /// Runtime 成功进入 Running 时调用：重置计数，使下一次故障重新获得一次恢复机会。
    /// </summary>
    public void Reset()
    {
        _attempts = 0;
    }
}
