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
    /// 表示重启 Runtime 副作用（Supervisor Stop+Start 原子编排）。
    /// </summary>
    public sealed partial record RestartRuntime : RuntimeEffect;

    /// <summary>
    /// 表示编排恢复 Runtime 副作用（ADR-0004 第一段：仅禁用全部第三方插件；
    /// 成功回流 RecoverPluginsDisabled，由 Reducer 迁移 Starting 并复用启动链路）。
    /// </summary>
    public sealed partial record RecoverRuntime : RuntimeEffect;

    /// <summary>
    /// 表示持久化安全模式状态副作用。
    /// </summary>
    /// <param name="Enabled">目标安全模式状态。</param>
    public sealed partial record SetSafeMode(bool Enabled) : RuntimeEffect;

    /// <summary>
    /// 表示持久化"关闭窗口后保持 DSH Runtime"开关副作用（ADR-0005）。
    /// </summary>
    /// <param name="Enabled">目标开关状态。</param>
    public sealed partial record SaveKeepRuntimeOnClose(bool Enabled) : RuntimeEffect;

    /// <summary>
    /// 表示持久化"异常启动自动进入安全模式"开关副作用（ADR-0004 修订注）。
    /// </summary>
    /// <param name="Enabled">目标开关状态。</param>
    public sealed partial record SaveAutoSafeModeOnFailure(bool Enabled) : RuntimeEffect;

    /// <summary>
    /// 表示持久化"启动时检查网络更新"开关副作用（§34 修订注）。
    /// </summary>
    /// <param name="Enabled">目标开关状态。</param>
    public sealed partial record SaveCheckUpdatesOnStartup(bool Enabled) : RuntimeEffect;
}
