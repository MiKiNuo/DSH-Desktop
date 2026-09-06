using System.ComponentModel;
using System.Diagnostics;

namespace DshDesktop.Infrastructure.Runtime;

/// <summary>
/// 表示 <see cref="DshDesktop.Application.Runtime.IProcessMetricsSampler"/> 的 Process API 实现
/// （Phase 8 Issue 03）：WorkingSet64 直读 + TotalProcessorTime 差分估 CPU%；
/// AOT 兼容（不用 PerformanceCounter）。实例有状态（差分基线），按进程 ID 自动重建基线。
/// </summary>
public sealed class ProcessMetricsSampler : DshDesktop.Application.Runtime.IProcessMetricsSampler
{
    private int? _baselineProcessId;
    private TimeSpan _baselineCpu;
    private long _baselineTimestamp;

    /// <inheritdoc />
    public DshDesktop.Application.Runtime.ProcessMetricsSample? Sample(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            long now = Stopwatch.GetTimestamp();
            TimeSpan cpu = process.TotalProcessorTime;
            long workingSet = process.WorkingSet64;

            double? cpuPercent = null;
            if (_baselineProcessId == processId)
            {
                double wallSeconds = Stopwatch.GetElapsedTime(_baselineTimestamp, now).TotalSeconds;
                double cpuDeltaSeconds = (cpu - _baselineCpu).TotalSeconds;
                if (wallSeconds > 0 && cpuDeltaSeconds >= 0)
                {
                    cpuPercent = Math.Clamp(
                        cpuDeltaSeconds / (wallSeconds * Environment.ProcessorCount) * 100.0,
                        0.0, 100.0);
                }
            }

            _baselineProcessId = processId;
            _baselineCpu = cpu;
            _baselineTimestamp = now;

            return new DshDesktop.Application.Runtime.ProcessMetricsSample(cpuPercent, workingSet);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception)
        {
            // 进程不存在 / 已退出 / 拒绝访问：重置基线并报告不可达。
            _baselineProcessId = null;
            return null;
        }
    }
}
