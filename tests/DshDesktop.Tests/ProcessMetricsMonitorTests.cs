using DshDesktop.Application.Runtime;

namespace DshDesktop.Tests;

/// <summary>
/// 进程指标监控器测试（Phase 8 Issue 03）：Application 编排 2s 定时采样，
/// Start/Stop 语义与采样事件发布（采样 IO 由 fake 端口替身承担）。
/// </summary>
public sealed class ProcessMetricsMonitorTests
{
    [Test]
    public async Task Start_PublishesSamplesUntilStop()
    {
        var sampler = new FakeSampler();
        using var monitor = new ProcessMetricsMonitor(sampler, TimeSpan.FromMilliseconds(20));
        var sampled = new TaskCompletionSource<ProcessMetricsSample>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.Sampled += (_, sample) => sampled.TrySetResult(sample);

        monitor.Start(1234);

        ProcessMetricsSample sample = await sampled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(sample.WorkingSetBytes).IsEqualTo(42);
        await Assert.That(sampler.Calls.Count > 0).IsTrue();
        await Assert.That(sampler.Calls.All(pid => pid == 1234)).IsTrue();

        monitor.Stop();
        int callsAtStop = sampler.Calls.Count;
        await Task.Delay(100);
        await Assert.That(sampler.Calls.Count).IsEqualTo(callsAtStop);
    }

    [Test]
    public async Task Start_NewProcessId_RestartsWithNewTarget()
    {
        var sampler = new FakeSampler();
        using var monitor = new ProcessMetricsMonitor(sampler, TimeSpan.FromMilliseconds(20));
        var sampled = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.Sampled += (_, _) =>
        {
            if (sampler.Calls.Count > 0 && sampler.Calls[^1] == 4242)
            {
                sampled.TrySetResult();
            }
        };

        monitor.Start(1234);
        monitor.Start(4242);

        await sampled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(sampler.Calls[^1]).IsEqualTo(4242);
    }

    [Test]
    public async Task NullSample_DoesNotPublish()
    {
        var sampler = new FakeSampler { NextSample = null };
        using var monitor = new ProcessMetricsMonitor(sampler, TimeSpan.FromMilliseconds(20));
        int published = 0;
        monitor.Sampled += (_, _) => Interlocked.Increment(ref published);

        monitor.Start(1234);
        await Task.Delay(120);

        await Assert.That(published).IsEqualTo(0);
        await Assert.That(sampler.Calls.Count > 0).IsTrue();
    }

    /// <summary>
    /// 表示采样端口测试替身。
    /// </summary>
    private sealed class FakeSampler : IProcessMetricsSampler
    {
        private readonly object _sync = new();
        private readonly List<int> _calls = [];

        /// <summary>下次采样返回值；null 模拟进程已退出。</summary>
        public ProcessMetricsSample? NextSample { get; set; } = new(10.0, 42);

        /// <summary>采样调用过的进程 ID 序列。</summary>
        public List<int> Calls
        {
            get { lock (_sync) { return [.. _calls]; } }
        }

        /// <inheritdoc />
        public ProcessMetricsSample? Sample(int processId)
        {
            lock (_sync)
            {
                _calls.Add(processId);
            }

            return NextSample;
        }
    }
}
