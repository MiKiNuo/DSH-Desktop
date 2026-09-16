using DshDesktop.Infrastructure.Runtime;

namespace DshDesktop.Tests;

/// <summary>
/// PnpmProvisioner 测试：pnpm 不可用时用宿主 npm 自举，全程不抛异常（装不了插件 ≠ 应用起不来）。
/// 通过注入 <see cref="PnpmProvisioner.ToolRunner"/> 假实现捕获传给 npm 的参数与结果，避免真实启动 node/npm。
/// </summary>
public sealed class PnpmProvisionerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pnpm-prov-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>
    /// 现有 pnpm.cjs 存在时必须短路返回它，绝不调用 npm（性能关键：正常路径零开销）。
    /// </summary>
    [Test]
    public async Task EnsureAvailableAsync_ExistingValidPnpm_ReturnsItWithoutRunningNpm()
    {
        string existing = Path.Combine(_root, "pnpm.cjs");
        Directory.CreateDirectory(_root);
        File.WriteAllText(existing, "// pnpm");

        int calls = 0;
        PnpmProvisioner.ToolRunner runner = (_, _, _, _, _) =>
        {
            calls++;
            return Task.FromResult((0, string.Empty));
        };

        string? result = await PnpmProvisioner.EnsureAvailableAsync(
            _root, "node", "npm-cli.js", existing, CancellationToken.None, runner);

        await Assert.That(result).IsEqualTo(existing);
        await Assert.That(calls).IsEqualTo(0);
    }

    /// <summary>
    /// 无现成 pnpm 时走 npm install pnpm@10，并解析回产物路径。
    /// 断言 Runner 收到的 arguments 正是 ["install","pnpm@10","--no-audit","--no-fund"]，
    /// 且返回路径指向 &lt;dataRoot&gt;\tools\pnpm\node_modules\pnpm\bin\pnpm.cjs
    /// （与宿主自持工具链的工具目录约定一致）。
    /// </summary>
    [Test]
    public async Task EnsureAvailableAsync_NoPnpm_RunsNpmInstallAndReturnsResolvedPath()
    {
        string dataRoot = Path.Combine(_root, "data");
        Directory.CreateDirectory(dataRoot);
        // npm 实体存在（假文件即可，不真跑）。
        string npmCjs = Path.Combine(_root, "npm-cli.js");
        File.WriteAllText(npmCjs, "// npm");

        string[]? capturedArgs = null;
        PnpmProvisioner.ToolRunner runner = (_, _, workingDirectory, args, _) =>
        {
            capturedArgs = args;
            // 伪造 npm install 的产物（pnpm.cjs）。
            string pnpmCjs = Path.Combine(
                workingDirectory, "node_modules", "pnpm", "bin", "pnpm.cjs");
            Directory.CreateDirectory(Path.GetDirectoryName(pnpmCjs)!);
            File.WriteAllText(pnpmCjs, "// pnpm");
            return Task.FromResult((0, string.Empty));
        };

        string? result = await PnpmProvisioner.EnsureAvailableAsync(
            dataRoot, "node", npmCjs, null, CancellationToken.None, runner);

        string expected = Path.Combine(
            dataRoot, "tools", "pnpm", "node_modules", "pnpm", "bin", "pnpm.cjs");
        await Assert.That(result).IsEqualTo(expected);
        await Assert.That(capturedArgs).IsNotNull();
        await Assert.That(capturedArgs!).IsEquivalentTo(
            new[] { "install", "pnpm@10", "--no-audit", "--no-fund" });
    }

    /// <summary>
    /// npm 实体缺失时无法自举：必须返回 null，且不调用 Runner（留可观测日志由实现负责）。
    /// </summary>
    [Test]
    public async Task EnsureAvailableAsync_NpmMissing_ReturnsNull()
    {
        string dshHome = Path.Combine(_root, "home");
        Directory.CreateDirectory(dshHome);

        int calls = 0;
        PnpmProvisioner.ToolRunner runner = (_, _, _, _, _) =>
        {
            calls++;
            return Task.FromResult((0, string.Empty));
        };

        string? result = await PnpmProvisioner.EnsureAvailableAsync(
            dshHome, "node", "C:\\no\\such\\npm-cli.js", null, CancellationToken.None, runner);

        await Assert.That(result).IsNull();
        await Assert.That(calls).IsEqualTo(0);
    }

    /// <summary>
    /// npm 安装失败（非 0 退出码）必须返回 null 且不抛异常（设计原则：装不了插件 ≠ 应用起不来）。
    /// </summary>
    [Test]
    public async Task EnsureAvailableAsync_NpmFails_ReturnsNullWithoutThrowing()
    {
        string dshHome = Path.Combine(_root, "home");
        Directory.CreateDirectory(dshHome);
        string npmCjs = Path.Combine(_root, "npm-cli.js");
        File.WriteAllText(npmCjs, "// npm");

        PnpmProvisioner.ToolRunner runner = (_, _, _, _, _) =>
            Task.FromResult((1, "ERR_PNPM_SOMETHING_DIED"));

        // 不抛即过关；返回必须为 null。
        string? result = await PnpmProvisioner.EnsureAvailableAsync(
            dshHome, "node", npmCjs, null, CancellationToken.None, runner);

        await Assert.That(result).IsNull();
    }
}
