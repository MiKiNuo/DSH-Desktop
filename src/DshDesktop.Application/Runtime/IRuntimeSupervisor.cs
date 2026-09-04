using DshDesktop.Domain.Runtime;

namespace DshDesktop.Application.Runtime;

/// <summary>
/// 表示 Runtime 监管器（§16 修订版）：包装 <see cref="IRuntimeOrchestrator"/>，
/// 负责健康检查循环、启动分阶段计时与崩溃检测，是 Runtime 真实状态的来源。
/// </summary>
public interface IRuntimeSupervisor
{
    /// <summary>
    /// 获取当前 Runtime 快照。
    /// </summary>
    RuntimeSnapshot Current { get; }

    /// <summary>
    /// 启动 Runtime 并等待就绪；期间通过快照事件报告各启动阶段。
    /// </summary>
    /// <param name="options">启动参数。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>就绪后的快照。</returns>
    Task<RuntimeSnapshot> StartAsync(RuntimeLaunchOptions options, CancellationToken cancellationToken);

    /// <summary>
    /// 停止 Runtime。
    /// </summary>
    /// <param name="cancellationToken">取消标记。</param>
    Task StopAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 快照变化时触发（阶段推进 / 健康变化 / 启停完成）。
    /// </summary>
    event EventHandler<RuntimeSnapshot>? SnapshotChanged;

    /// <summary>
    /// Runtime 进程退出时触发（透传自编排器；自发退出与主动停止都会触发）。
    /// </summary>
    event EventHandler<RuntimeExitedEventArgs>? Exited;
}
