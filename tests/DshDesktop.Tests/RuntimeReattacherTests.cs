using DshDesktop.Application.Runtime;

namespace DshDesktop.Tests;

/// <summary>
/// Runtime 重接管探测测试（ADR-0005，Phase 8 Issue 04）：
/// 存活 + 可恢复 Session URL → 接管；进程死了或 HTTP 不可达 → 回退正常启动链；
/// 存活但 Session URL 不可恢复（一次性 token，禁止落盘）→ 杀旧进程退化重启。
/// Phase 8 评审 F9：探测原语改经 IRuntimeProbe 端口注入（Fake 替代原委托）。
/// </summary>
public sealed class RuntimeReattacherTests
{
    private const string Host = "127.0.0.1";
    private const int Pid = 4321;
    private const int Port = 5678;

    [Test]
    public async Task TryReattach_ProcessDead_ReturnsNotFoundWithoutHttpProbe()
    {
        var probe = new FakeProbe { ProcessAlive = false };

        ReattachOutcome outcome = await new RuntimeReattacher(probe, Serilog.Core.Logger.None)
            .TryReattachAsync(Host, Pid, Port, canRestoreSessionUrl: true, CancellationToken.None);

        await Assert.That(outcome).IsEqualTo(ReattachOutcome.NotFound);
        await Assert.That(probe.HttpProbeCount).IsEqualTo(0);
    }

    [Test]
    public async Task TryReattach_ProcessAliveButHttpUnreachable_ReturnsNotFound()
    {
        var probe = new FakeProbe { ProcessAlive = true, HttpAlive = false };

        ReattachOutcome outcome = await new RuntimeReattacher(probe, Serilog.Core.Logger.None)
            .TryReattachAsync(Host, Pid, Port, canRestoreSessionUrl: true, CancellationToken.None);

        await Assert.That(outcome).IsEqualTo(ReattachOutcome.NotFound);
    }

    [Test]
    public async Task TryReattach_AliveWithRestorableSessionUrl_ReturnsAdopted()
    {
        var probe = new FakeProbe { ProcessAlive = true, HttpAlive = true };

        ReattachOutcome outcome = await new RuntimeReattacher(probe, Serilog.Core.Logger.None)
            .TryReattachAsync(Host, Pid, Port, canRestoreSessionUrl: true, CancellationToken.None);

        await Assert.That(outcome).IsEqualTo(ReattachOutcome.Adopted);
        await Assert.That(probe.KilledPid).IsNull(); // 接管不杀进程
    }

    [Test]
    public async Task TryReattach_AliveWithoutSessionUrl_DegradesToRestartAndKillsOldProcess()
    {
        // ADR-0005：Session URL 一次性且禁止落盘 → 接管后无法恢复会话，退化重启并记录。
        var probe = new FakeProbe { ProcessAlive = true, HttpAlive = true };

        ReattachOutcome outcome = await new RuntimeReattacher(probe, Serilog.Core.Logger.None)
            .TryReattachAsync(Host, Pid, Port, canRestoreSessionUrl: false, CancellationToken.None);

        await Assert.That(outcome).IsEqualTo(ReattachOutcome.DegradedToRestart);
        await Assert.That(probe.KilledPid).IsEqualTo(Pid);
    }

    [Test]
    public async Task TryReattach_KillFailsBecauseProcessAlreadyGone_ReturnsNotFound()
    {
        // 探测与杀进程之间的竞态：进程刚死 → 按未找到处理，走正常启动链。
        var probe = new FakeProbe
        {
            ProcessAlive = true,
            HttpAlive = true,
            KillException = new ArgumentException("进程不存在"),
        };

        ReattachOutcome outcome = await new RuntimeReattacher(probe, Serilog.Core.Logger.None)
            .TryReattachAsync(Host, Pid, Port, canRestoreSessionUrl: false, CancellationToken.None);

        await Assert.That(outcome).IsEqualTo(ReattachOutcome.NotFound);
    }

    /// <summary>
    /// 表示 IRuntimeProbe 测试替身。
    /// </summary>
    private sealed class FakeProbe : IRuntimeProbe
    {
        public bool ProcessAlive { get; init; }

        public bool HttpAlive { get; init; }

        public Exception? KillException { get; init; }

        public int HttpProbeCount { get; private set; }

        public int? KilledPid { get; private set; }

        public bool IsProcessAlive(int processId) => ProcessAlive;

        public Task<bool> IsHttpAliveAsync(string host, int port, CancellationToken cancellationToken)
        {
            HttpProbeCount++;
            return Task.FromResult(HttpAlive);
        }

        public void KillProcessTree(int processId)
        {
            if (KillException is not null)
            {
                throw KillException;
            }

            KilledPid = processId;
        }
    }
}
