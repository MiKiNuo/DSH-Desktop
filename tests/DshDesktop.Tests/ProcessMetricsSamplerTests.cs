using DshDesktop.Application.Runtime;
using DshDesktop.Infrastructure.Runtime;

namespace DshDesktop.Tests;

/// <summary>
/// 进程指标采样器测试（Phase 8 Issue 03）：对真实进程采样（Process API，AOT 兼容）；
/// CPU 采用差分口径，首次采样无基线为 null。
/// </summary>
public sealed class ProcessMetricsSamplerTests
{
    [Test]
    public async Task Sample_CurrentProcess_ReturnsWorkingSet()
    {
        var sampler = new ProcessMetricsSampler();

        ProcessMetricsSample? sample = sampler.Sample(Environment.ProcessId);

        await Assert.That(sample).IsNotNull();
        await Assert.That(sample!.WorkingSetBytes > 0).IsTrue();

        // 首次采样无 CPU 基线。
        await Assert.That(sample.CpuPercent).IsNull();
    }

    [Test]
    public async Task Sample_SecondSample_ComputesCpuPercent()
    {
        var sampler = new ProcessMetricsSampler();
        _ = sampler.Sample(Environment.ProcessId);
        await Task.Delay(50);

        ProcessMetricsSample? sample = sampler.Sample(Environment.ProcessId);

        await Assert.That(sample).IsNotNull();
        await Assert.That(sample!.CpuPercent).IsNotNull();
        await Assert.That(sample.CpuPercent is >= 0 and <= 100).IsTrue();
    }

    [Test]
    public async Task Sample_ExitedProcess_ReturnsNull()
    {
        var sampler = new ProcessMetricsSampler();

        // 不存在的进程 ID（int.MaxValue 不会被占用）。
        ProcessMetricsSample? sample = sampler.Sample(int.MaxValue);

        await Assert.That(sample).IsNull();
    }

    [Test]
    public async Task Sample_ProcessIdChange_ResetsCpuBaseline()
    {
        var sampler = new ProcessMetricsSampler();
        _ = sampler.Sample(Environment.ProcessId);
        _ = sampler.Sample(int.MaxValue);

        ProcessMetricsSample? sample = sampler.Sample(Environment.ProcessId);

        // 进程切换后重新建立基线：CPU 为 null，内存仍可读。
        await Assert.That(sample).IsNotNull();
        await Assert.That(sample!.CpuPercent).IsNull();
    }
}
