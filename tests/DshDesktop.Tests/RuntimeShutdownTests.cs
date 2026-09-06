using DshDesktop.Application.Runtime;
using DshDesktop.Domain.Runtime;

namespace DshDesktop.Tests;

/// <summary>
/// Shutdown 分叉测试（ADR-0005，Phase 8 Issue 04）：
/// KeepRuntimeOnClose 开 = 关窗只退 Desktop 不杀 Runtime；关 = 现状（停 Runtime）。
/// </summary>
public sealed class RuntimeShutdownTests
{
    [Test]
    public async Task ShutdownRuntime_KeepOnClose_DoesNotStopRuntime()
    {
        var supervisor = new FakeSupervisor();

        RuntimeShutdown.ShutdownRuntime(supervisor, keepRuntimeOnClose: true, Serilog.Core.Logger.None);

        await Assert.That(supervisor.StopCount).IsEqualTo(0);
    }

    [Test]
    public async Task ShutdownRuntime_KeepOff_StopsRuntime()
    {
        var supervisor = new FakeSupervisor();

        RuntimeShutdown.ShutdownRuntime(supervisor, keepRuntimeOnClose: false, Serilog.Core.Logger.None);

        await Assert.That(supervisor.StopCount).IsEqualTo(1);
    }

    [Test]
    public async Task ShutdownRuntime_NullSupervisor_DoesNotThrow()
    {
        RuntimeShutdown.ShutdownRuntime(null, keepRuntimeOnClose: false, Serilog.Core.Logger.None);
        RuntimeShutdown.ShutdownRuntime(null, keepRuntimeOnClose: true, Serilog.Core.Logger.None);

        await Task.CompletedTask; // 不抛异常即通过
    }

    /// <summary>最小 Supervisor 替身：只记录停止次数。</summary>
    private sealed class FakeSupervisor : IRuntimeSupervisor
    {
        public int StopCount { get; private set; }

        public RuntimeSnapshot Current { get; } = new(
            RuntimeLifecycle.Stopped, RuntimeHealth.Unknown, RuntimeStartupStage.None,
            null, null, null, null);

#pragma warning disable CS0067 // 测试替身不触发事件
        public event EventHandler<RuntimeSnapshot>? SnapshotChanged;
        public event EventHandler<RuntimeExitedEventArgs>? Exited;
#pragma warning restore CS0067

        public Task<RuntimeSnapshot> StartAsync(RuntimeLaunchOptions options, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCount++;
            return Task.CompletedTask;
        }

        public Task<RuntimeSnapshot> RestartAsync(RuntimeLaunchOptions options, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}
