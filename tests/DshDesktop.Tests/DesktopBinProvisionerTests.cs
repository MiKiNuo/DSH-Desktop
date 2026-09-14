using DshDesktop.Infrastructure.Runtime;

namespace DshDesktop.Tests;

/// <summary>
/// ① 工具垫片（.desktop-bin）：dsh-market 与其拉起的 dsh CLI 都【按名字】执行 pnpm，
/// 而 vendored pnpm 只是 pnpm.cjs（不是可执行名）⇒ 市场探针 probePnpm() 必然失败，
/// 顶部常驻「安装插件前需要先配置 pnpm 环境」且安装被拦停。
/// 落盘 pnpm.cmd / node.cmd 让 pnpm 按名可解析；内容复刻上游 Electron 壳的
/// ensureProfilePnpmShim（差值：不写 @set ELECTRON_RUN_AS_NODE=1——我们的 harness
/// 跑在 vendored 原生 node.exe 上，不是 Electron utility process）。
/// </summary>
public sealed class DesktopBinProvisionerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dsh-desktopbin-" + Guid.NewGuid().ToString("N"));

    private string DshHome => Path.Combine(_root, "dsh-home");

    private string NodePath => Path.Combine(_root, "tools", "node.exe");

    private string PnpmEntry => Path.Combine(_root, "tools", "pnpm.cjs");

    private string RunnerPath => Path.Combine(_root, "resources", "pnpm-runner.mjs");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private void CreateToolchain()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(NodePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(RunnerPath)!);
        File.WriteAllText(NodePath, "stub");
        File.WriteAllText(PnpmEntry, "stub");
        File.WriteAllText(RunnerPath, "stub");
    }

    /// <summary>垫片内容逐字断言（沿用上游同构形态；CRLF，UTF-8 无 BOM）。</summary>
    [Test]
    public async Task Ensure_WritesShims_WithExpectedContent()
    {
        CreateToolchain();

        string directory = DesktopBinProvisioner.Ensure(DshHome, NodePath, PnpmEntry, RunnerPath);

        await Assert.That(directory).IsEqualTo(Path.Combine(DshHome, ".desktop-bin"));
        string expectedPnpm =
            "@chcp 65001 >nul\r\n@echo off\r\n"
            + $"\"{NodePath}\" \"{RunnerPath}\" \"{PnpmEntry}\" %*\r\n";
        await Assert.That(await File.ReadAllTextAsync(Path.Combine(directory, "pnpm.cmd"))).IsEqualTo(expectedPnpm);
        string expectedNode =
            "@chcp 65001 >nul\r\n@echo off\r\n"
            + $"\"{NodePath}\" %*\r\n";
        await Assert.That(await File.ReadAllTextAsync(Path.Combine(directory, "node.cmd"))).IsEqualTo(expectedNode);
    }

    /// <summary>幂等且跟随配置刷新：换 node 路径后重复调用，垫片内容随之更新（不残留旧值）。</summary>
    [Test]
    public async Task Ensure_SecondCallWithOtherNodePath_RefreshesShim()
    {
        CreateToolchain();
        DesktopBinProvisioner.Ensure(DshHome, NodePath, PnpmEntry, RunnerPath);
        string newNode = Path.Combine(_root, "tools2", "node.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(newNode)!);
        File.WriteAllText(newNode, "stub");

        string directory = DesktopBinProvisioner.Ensure(DshHome, newNode, PnpmEntry, RunnerPath);

        string content = await File.ReadAllTextAsync(Path.Combine(directory, "pnpm.cmd"));
        await Assert.That(content).Contains($"\"{newNode}\"");
    }

    /// <summary>缺 vendored pnpm 时按名失败：报错必须点明 pnpmCjsPath，不静默写一个坏垫片。</summary>
    [Test]
    public async Task Ensure_MissingPnpmEntry_Throws()
    {
        CreateToolchain();
        File.Delete(PnpmEntry);

        await Assert.That(() => DesktopBinProvisioner.Ensure(DshHome, NodePath, PnpmEntry, RunnerPath))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("pnpmCjsPath");
    }

    /// <summary>缺 runner 时按名失败：打包漏发 resources\pnpm-runner.mjs 必须立刻暴露。</summary>
    [Test]
    public async Task Ensure_MissingRunner_Throws()
    {
        CreateToolchain();
        File.Delete(RunnerPath);

        await Assert.That(() => DesktopBinProvisioner.Ensure(DshHome, NodePath, PnpmEntry, RunnerPath))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("pnpm-runner.mjs");
    }
}
