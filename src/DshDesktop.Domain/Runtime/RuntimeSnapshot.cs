namespace DshDesktop.Domain.Runtime;

/// <summary>
/// 表示 Runtime 启动阶段（架构文档 §15.1 StartupStage，§17 分级启动链）。
/// </summary>
public enum RuntimeStartupStage
{
    /// <summary>未启动。</summary>
    None,

    /// <summary>校验启动参数与路径。</summary>
    Validating,

    /// <summary>拉起 DSH 进程。</summary>
    Spawning,

    /// <summary>等待 stdout 就绪行与 HTTP 就绪。</summary>
    WaitingReady,

    /// <summary>启动完成。</summary>
    Ready,
}

/// <summary>
/// 表示 Runtime 健康状态（CONTEXT.md: Health Status）。
/// </summary>
public enum RuntimeHealth
{
    /// <summary>未知（未运行或未开始检查）。</summary>
    Unknown,

    /// <summary>健康（HTTP 轮询正常）。</summary>
    Healthy,

    /// <summary>无响应（进程存活但 HTTP 连续失败）。</summary>
    Unresponsive,
}

/// <summary>
/// 表示 Runtime 在某时刻的完整快照（§16：真实状态来源是 Application Runtime）。
/// </summary>
/// <param name="Lifecycle">生命周期状态。</param>
/// <param name="Health">健康状态。</param>
/// <param name="StartupStage">启动阶段。</param>
/// <param name="StartupElapsed">本次启动耗时；未启动完成为 null。</param>
/// <param name="ProcessId">DSH 进程 ID；未运行为 null。</param>
/// <param name="Port">实际监听端口；未运行为 null。</param>
/// <param name="Url">Session URL（含 token）；未运行为 null。</param>
public sealed record RuntimeSnapshot(
    RuntimeLifecycle Lifecycle,
    RuntimeHealth Health,
    RuntimeStartupStage StartupStage,
    TimeSpan? StartupElapsed,
    int? ProcessId,
    int? Port,
    string? Url);
