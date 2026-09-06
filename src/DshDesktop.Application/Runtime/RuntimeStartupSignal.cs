namespace DshDesktop.Application.Runtime;

/// <summary>
/// 表示启动进度信号（Phase 8 评审 F16）：阶段推进经 IProgress 有序回报；
/// HttpProbing 是不进快照状态机的纯计时标记——计时标记不是 Domain 概念，故从
/// <see cref="DshDesktop.Domain.Runtime.RuntimeStartupStage"/> 移出到本 Application 枚举。
/// </summary>
public enum RuntimeStartupSignal
{
    /// <summary>校验启动参数与路径。</summary>
    Validating,

    /// <summary>拉起 DSH 进程。</summary>
    Spawning,

    /// <summary>等待 stdout 就绪行与 HTTP 就绪。</summary>
    WaitingReady,

    /// <summary>stdout 就绪行已捕获（DSH / Profile / Plugins 就绪），HTTP 探测中（纯计时标记，不进快照状态机）。</summary>
    HttpProbing,

    /// <summary>启动完成。</summary>
    Ready,
}

/// <summary>
/// 表示一次启动中某信号到达时的累计耗时（§46 Runtime.Start.Stage 的结构化副本，
/// 自 Runtime.Start.Begin 起算；供 Dashboard 启动 timeline 投影）。
/// Phase 8 评审 F16：自 Domain 移入（Stage 载荷为含计时标记的 Application 信号）。
/// </summary>
/// <param name="Stage">到达的启动信号。</param>
/// <param name="Elapsed">自启动开始的累计耗时。</param>
public sealed record StartupStageTiming(RuntimeStartupSignal Stage, TimeSpan Elapsed);
