using DshDesktop.Infrastructure.Plugins;

namespace DshDesktop.Tests;

/// <summary>
/// NodeJsToolRunner 测试（B1）：ERR_PNPM_UNEXPECTED_VIRTUAL_STORE 自愈路径。
/// 通过注入 runOnce 委托捕获每次传给 pnpm 的参数与结果，避免真实启动 node/pnpm 进程。
/// </summary>
public sealed class NodeJsToolRunnerTests
{
    // 构造一个 runOnce 注入器：记录每次参数，并按 responder 返回 (exit, tail)。
    private static Func<string, string, string, string[], CancellationToken, Task<(int ExitCode, string OutputTail)>> Capture(
        Func<string[], (int ExitCode, string OutputTail)> responder,
        List<string[]> capturedArgs)
    {
        return (_, _, _, args, _) =>
        {
            capturedArgs.Add(args);
            return Task.FromResult(responder(args));
        };
    }

    /// <summary>
    /// 命中 VIRTUAL_STORE 时，修复步骤必须带 --force 与 --no-frozen-lockfile（本次修复核心）。
    /// 修复步骤的 install 以 args[0]=="install" 识别；原命令/重试以其它命令识别。
    /// </summary>
    [Test]
    public async Task RunAsync_VirtualStoreError_RepairUsesForceAndNoFrozenLockfile()
    {
        List<string[]> calls = [];
        int originalCalls = 0;
        var runner = Capture(args =>
        {
            if (args.Length > 0 && args[0] == "install")
            {
                return (0, "repair-ok");
            }

            // 原命令首跑报 VIRTUAL_STORE，重试成功。
            originalCalls++;
            return originalCalls == 1
                ? (1, "ERR_PNPM_UNEXPECTED_VIRTUAL_STORE at old path")
                : (0, "ok after retry");
        }, calls);

        (int exitCode, _) = await NodeJsToolRunner.RunAsync(
            "node", "pnpm.cjs", "wd", ["add", "foo"], CancellationToken.None, runner);

        await Assert.That(exitCode).IsEqualTo(0);
        bool repairHasFlags = calls.Any(a => a.Contains("--force") && a.Contains("--no-frozen-lockfile"));
        await Assert.That(repairHasFlags).IsTrue();
    }

    /// <summary>
    /// 修复步骤失败时，最终返回的 outputTail 必须包含修复步骤的失败信息（锁死「吞掉退出码」回归）。
    /// </summary>
    [Test]
    public async Task RunAsync_RepairFails_OutputTailContainsRepairFailure()
    {
        List<string[]> calls = [];
        var runner = Capture(args =>
        {
            if (args.Length > 0 && args[0] == "install")
            {
                return (1, "REPAIR_STEP_DIED exit=1");
            }

            // 原命令与重试均报 VIRTUAL_STORE 失败。
            return (1, "ERR_PNPM_UNEXPECTED_VIRTUAL_STORE at old path");
        }, calls);

        (int exitCode, string outputTail) = await NodeJsToolRunner.RunAsync(
            "node", "pnpm.cjs", "wd", ["add", "foo"], CancellationToken.None, runner);

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(outputTail.Contains("REPAIR_STEP_DIED")).IsTrue();
        await Assert.That(outputTail.Contains("修复步骤失败")).IsTrue();
    }

    /// <summary>
    /// 原命令首次成功时不得触发任何修复步骤（不无谓 purge）。
    /// </summary>
    [Test]
    public async Task RunAsync_InitialSuccess_NoRepairTriggered()
    {
        List<string[]> calls = [];
        var runner = Capture(_ => (0, "ok"), calls);

        (int exitCode, _) = await NodeJsToolRunner.RunAsync(
            "node", "pnpm.cjs", "wd", ["add", "foo"], CancellationToken.None, runner);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(calls.Count).IsEqualTo(1);
        await Assert.That(calls.Any(a => a.Contains("--force"))).IsFalse();
    }
}
