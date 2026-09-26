using DshDesktop.Domain.Runtime;

namespace DshDesktop.Application.Runtime;

/// <summary>
/// 恢复 / 重接管 / 指标判定的对外副作用端口（组合根适配器闭包 Mediator Store 与 ConfigPersistence）：
/// Application 编排只描述"何时做什么"，具体派发与落盘由组合根落地（批 2b 端口先例）。
/// </summary>
public interface IRuntimeRecoveryHost
{
    /// <summary>恢复重派：延迟后派发一次 StartRuntime 意图。</summary>
    Task RedispatchStartRuntimeAsync(CancellationToken cancellationToken);

    /// <summary>自动进入安全模式：落盘 + SafeModeChanged 派发。</summary>
    Task EnterSafeModeAsync(CancellationToken cancellationToken);

    /// <summary>重接管 Adopted / Running 对账后的 RuntimeStarted 显式回流。</summary>
    void DispatchRuntimeStarted(int? processId, int? port, string url);

    /// <summary>启动阶段 timeline 投影（DashboardIntent.TimelineReceived）。</summary>
    void DispatchTimeline(IReadOnlyList<StartupStageTiming> timings);

    /// <summary>启动耗时投影（DashboardIntent.StartupElapsedRecorded；PreviousMs = 切换前"上次"基准）。</summary>
    void DispatchStartupElapsed(long? previousMs);

    /// <summary>进程指标监控启动（已知 PID）。</summary>
    void StartMetricsMonitor(int processId);

    /// <summary>进程指标监控停止。</summary>
    void StopMetricsMonitor();

    /// <summary>重接管目标落盘（Running 快照 PID/端口写入 config）。</summary>
    Task PersistReattachTargetAsync(int processId, int port, CancellationToken cancellationToken);

    /// <summary>清除重接管记录（NotFound / DegradedToRestart 回退正常启动链）。</summary>
    Task ClearReattachRecordAsync(CancellationToken cancellationToken);

    /// <summary>启动耗时写盘（config.LastStartupElapsedMs）。</summary>
    Task PersistStartupElapsedAsync(long elapsedMs, CancellationToken cancellationToken);
}
