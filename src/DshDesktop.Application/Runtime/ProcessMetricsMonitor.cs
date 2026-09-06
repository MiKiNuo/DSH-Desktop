namespace DshDesktop.Application.Runtime;

/// <summary>
/// 表示进程指标监控器（Phase 8 Issue 03）：Application 编排的定时采样循环（默认 2s），
/// 采样 IO 委托给 <see cref="IProcessMetricsSampler"/> 端口，结果以事件发布；
/// 由组合根接线到 MVI Store，Presentation 只收投影。
/// </summary>
public sealed class ProcessMetricsMonitor : IDisposable
{
    /// <summary>默认采样间隔（Dashboard 健康度刷新节奏）。</summary>
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(2);

    private readonly IProcessMetricsSampler _sampler;
    private readonly TimeSpan _interval;
    private readonly object _sync = new();

    private CancellationTokenSource? _loopSource;
    private int? _activeProcessId;

    /// <summary>
    /// 初始化进程指标监控器。
    /// </summary>
    /// <param name="sampler">采样端口。</param>
    /// <param name="interval">采样间隔；null 用默认 2s。</param>
    public ProcessMetricsMonitor(IProcessMetricsSampler sampler, TimeSpan? interval = null)
    {
        ArgumentNullException.ThrowIfNull(sampler);
        _sampler = sampler;
        _interval = interval ?? DefaultInterval;
    }

    /// <summary>
    /// 每次有效采样发布一次（进程不可达的 null 采样不发布）。
    /// 回调在后台线程触发，订阅方自行编排线程。
    /// </summary>
    public event EventHandler<ProcessMetricsSample>? Sampled;

    /// <summary>
    /// 开始监控指定进程；同进程重复调用为幂等（保持 CPU 差分基线连续），换进程则重启循环。
    /// </summary>
    /// <param name="processId">目标进程 ID。</param>
    public void Start(int processId)
    {
        lock (_sync)
        {
            if (_activeProcessId == processId && _loopSource is not null)
            {
                return;
            }

            StopLocked();
            _activeProcessId = processId;
            _loopSource = new CancellationTokenSource();
            _ = RunLoopAsync(processId, _loopSource.Token);
        }
    }

    /// <summary>
    /// 停止监控（无循环时为 no-op）。
    /// </summary>
    public void Stop()
    {
        lock (_sync)
        {
            StopLocked();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Stop();
    }

    private void StopLocked()
    {
        _loopSource?.Cancel();
        _loopSource?.Dispose();
        _loopSource = null;
        _activeProcessId = null;
    }

    private async Task RunLoopAsync(int processId, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_interval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            ProcessMetricsSample? sample = _sampler.Sample(processId);
            if (sample is not null && !cancellationToken.IsCancellationRequested)
            {
                Sampled?.Invoke(this, sample);
            }
        }
    }
}
