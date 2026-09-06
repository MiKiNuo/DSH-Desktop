namespace DshDesktop.Application.Runtime;

/// <summary>
/// 表示一次进程指标采样结果（Phase 8 Issue 03：Dashboard 运行健康度数据源）。
/// </summary>
/// <param name="CpuPercent">CPU 占用百分比（TotalProcessorTime 差分 / 墙上时间 / 逻辑核数，0-100）；
/// 首次采样无基线为 null。</param>
/// <param name="WorkingSetBytes">工作集内存（Process.WorkingSet64）。</param>
public sealed record ProcessMetricsSample(double? CpuPercent, long WorkingSetBytes);

/// <summary>
/// 表示进程指标采样端口（Infrastructure 实现，Presentation 不感知；AOT 兼容：只用 Process API，
/// 不用 PerformanceCounter）。
/// </summary>
public interface IProcessMetricsSampler
{
    /// <summary>
    /// 采样指定进程一次。进程不存在或已退出时返回 null。
    /// </summary>
    /// <param name="processId">目标进程 ID。</param>
    /// <returns>采样结果；进程不可达为 null。</returns>
    ProcessMetricsSample? Sample(int processId);
}
