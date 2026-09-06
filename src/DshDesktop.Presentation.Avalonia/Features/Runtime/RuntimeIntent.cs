using DshDesktop.Domain.Runtime;
using MiKiNuo.Mvi.Domain.MVI.Intent;

namespace DshDesktop.Presentation.Avalonia.Features.Runtime;

/// <summary>
/// 表示 Runtime 意图（架构文档 §15.2 的 Phase 1/2a 子集，业务语义命名）。
/// </summary>
public abstract partial record RuntimeIntent : IMviIntent
{
    /// <summary>
    /// 表示启动 Runtime 意图。
    /// </summary>
    public sealed partial record StartRuntime : RuntimeIntent;

    /// <summary>
    /// 表示停止 Runtime 意图。
    /// </summary>
    public sealed partial record StopRuntime : RuntimeIntent;

    /// <summary>
    /// 表示重启 Runtime 意图（ADR-0004：Stop+Start 原子编排，仅 Running / Failed 合法）。
    /// </summary>
    public sealed partial record RestartRuntime : RuntimeIntent;

    /// <summary>
    /// 表示编排恢复 Runtime 意图（ADR-0004：先禁用全部第三方插件再启动，仅 Failed 合法）。
    /// </summary>
    public sealed partial record RecoverRuntime : RuntimeIntent;

    /// <summary>
    /// 表示恢复第一段（禁用全部第三方插件）已完成的回流意图（ADR-0004：仅 Recovering 合法，
    /// 迁移到 Starting 并复用启动链路）。
    /// </summary>
    public sealed partial record RecoverPluginsDisabled : RuntimeIntent;

    /// <summary>
    /// 表示 Runtime 进程已拉起且 HTTP 就绪的回流意图。
    /// </summary>
    /// <param name="ProcessId">DSH 进程 ID；快照未携带为 null。</param>
    /// <param name="Port">实际监听端口；快照未携带为 null。</param>
    /// <param name="Url">Session URL（含 token）。</param>
    public sealed partial record RuntimeStarted(int? ProcessId, int? Port, string Url) : RuntimeIntent;

    /// <summary>
    /// 表示 Runtime 启动或运行失败的回流意图。
    /// </summary>
    /// <param name="Error">错误信息。</param>
    public sealed partial record RuntimeFailed(string Error) : RuntimeIntent;

    /// <summary>
    /// 表示 Runtime 进程退出的回流意图（崩溃语义由 Reducer 依据当前生命周期判定）。
    /// </summary>
    /// <param name="ExitCode">进程退出码。</param>
    public sealed partial record RuntimeExited(int? ExitCode) : RuntimeIntent;

    /// <summary>
    /// 表示 Supervisor 快照推送的回流意图（§16 修订版推送通道）。
    /// 只承载阶段 / 健康 / 耗时；生命周期迁移由专用 Intent 表达。
    /// </summary>
    /// <param name="Snapshot">Runtime 快照。</param>
    public sealed partial record RuntimeSnapshotReceived(RuntimeSnapshot Snapshot) : RuntimeIntent;

    /// <summary>
    /// 表示进入安全模式意图。
    /// </summary>
    public sealed partial record EnterSafeMode : RuntimeIntent;

    /// <summary>
    /// 表示退出安全模式意图。
    /// </summary>
    public sealed partial record ExitSafeMode : RuntimeIntent;

    /// <summary>
    /// 表示安全模式状态已持久化的回流意图。
    /// </summary>
    /// <param name="Enabled">安全模式是否开启。</param>
    public sealed partial record SafeModeChanged(bool Enabled) : RuntimeIntent;

    /// <summary>
    /// 表示不改变生命周期的操作失败回流意图（如安全模式写入失败）。
    /// </summary>
    /// <param name="Error">错误信息。</param>
    public sealed partial record RuntimeOperationFailed(string Error) : RuntimeIntent;

    /// <summary>
    /// 表示切换"关闭窗口后保持 DSH Runtime"意图（ADR-0005；无载荷翻转，同 Settings ToggleSafeMode 先例）。
    /// </summary>
    public sealed partial record ToggleKeepRuntimeOnClose : RuntimeIntent;

    /// <summary>
    /// 表示切换"异常启动自动进入安全模式"意图（ADR-0004 修订注；无载荷翻转）。
    /// </summary>
    public sealed partial record ToggleAutoSafeModeOnFailure : RuntimeIntent;

    /// <summary>
    /// 表示切换"启动时检查网络更新"意图（§34 修订注；无载荷翻转）。
    /// </summary>
    public sealed partial record ToggleCheckUpdatesOnStartup : RuntimeIntent;

    /// <summary>
    /// 表示三个策略开关的持久化值已加载的回流意图（组合根初始化时发，config 为权威源）。
    /// </summary>
    /// <param name="KeepRuntimeOnClose">关闭窗口后保持 Runtime。</param>
    /// <param name="AutoSafeModeOnFailure">异常启动自动进入安全模式。</param>
    /// <param name="CheckUpdatesOnStartup">启动时检查网络更新。</param>
    public sealed partial record PoliciesLoaded(
        bool KeepRuntimeOnClose,
        bool AutoSafeModeOnFailure,
        bool CheckUpdatesOnStartup) : RuntimeIntent;

    /// <summary>
    /// 表示运行环境信息已探测的回流意图（组合根初始化时发）。
    /// </summary>
    /// <param name="Environment">运行环境信息。</param>
    public sealed partial record EnvironmentLoaded(RuntimeEnvironmentInfo Environment) : RuntimeIntent;

    /// <summary>
    /// 表示 DSH 版本投影变化的回流意图（自 UpdatesStore.CurrentDshVersion，§11.2）。
    /// </summary>
    /// <param name="Version">当前 DSH 版本；未知为 null。</param>
    public sealed partial record DshVersionChanged(string? Version) : RuntimeIntent;
}
