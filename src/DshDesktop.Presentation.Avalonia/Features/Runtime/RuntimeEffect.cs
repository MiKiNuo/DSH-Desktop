using MiKiNuo.Mvi.Domain.MVI.Effect;

namespace DshDesktop.Presentation.Avalonia.Features.Runtime;

/// <summary>
/// 表示 Runtime 副作用（架构文档 §15.3 的 Phase 1 子集）。
/// </summary>
public abstract partial record RuntimeEffect : IMviEffect
{
    /// <summary>
    /// 表示启动 Runtime 副作用。
    /// </summary>
    public sealed partial record StartRuntime : RuntimeEffect;

    /// <summary>
    /// 表示停止 Runtime 副作用。
    /// </summary>
    public sealed partial record StopRuntime : RuntimeEffect;

    /// <summary>
    /// 表示持久化安全模式状态副作用。
    /// </summary>
    /// <param name="Enabled">目标安全模式状态。</param>
    public sealed partial record SetSafeMode(bool Enabled) : RuntimeEffect;
}
