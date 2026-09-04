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
    /// 表示 Runtime 进程已拉起且 HTTP 就绪的回流意图。
    /// </summary>
    /// <param name="ProcessId">DSH 进程 ID。</param>
    /// <param name="Port">实际监听端口。</param>
    /// <param name="Url">Session URL（含 token）。</param>
    public sealed partial record RuntimeStarted(int ProcessId, int Port, string Url) : RuntimeIntent;

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
}
