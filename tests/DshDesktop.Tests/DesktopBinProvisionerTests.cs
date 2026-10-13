using DshDesktop.Infrastructure.Runtime;

namespace DshDesktop.Tests;

/// <summary>
/// ① 工具垫片（.desktop-bin）：dsh-market 与其拉起的 dsh CLI 都【按名字】执行 pnpm，
/// 而 vendored pnpm 只是 pnpm.cjs（不是可执行名）⇒ 市场探针 probePnpm() 必然失败，
/// 顶部常驻「安装插件前需要先配置 pnpm 环境」且安装被拦停。
/// 落盘 pnpm.cmd 让 pnpm 按名可解析（node.cmd 已删除，见 Ensure_WritesNoNodeShim…：它在 PATH
/// 首位会让 pnpm.cmd 里的 "node" 解析回自己，cmd 的 .cmd 自调用无深度上限 ⇒ 永久挂死）。
/// 内容复刻上游 Electron 壳的 ensureProfilePnpmShim（差值：不写 @set ELECTRON_RUN_AS_NODE=1
/// ——我们的 harness 跑在 vendored 原生 node.exe 上，不是 Electron utility process）。
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
    }

    /// <summary>
    /// 🚨 回归：垫片目录里【不得有 node.cmd】。
    /// <para>
    /// 该目录被 PrependToolPath 前置进 harness PATH 首位。一个内容为 <c>"node" %*</c>
    /// 的 node.cmd 会让 pnpm.cmd 里的 <c>"node"</c> 按 PATH 解析回它自己 ⇒ cmd 的 .cmd
    /// 批处理自调用只 fork 不返回、无深度上限 ⇒ 永久挂死（不报错、不退出、无日志）。
    /// dshmarket 的 pnpm 探针（<c>cmd /d /s /c "pnpm --version"</c>）没有超时，于是
    /// 「正在安装 1/1」永远转下去，而 pnpm 从未被启动 ⇒ 宿主日志与市场日志全都没有记录。
    /// </para>
    /// <para>
    /// node 本身不需要这个垫片：NodePath 所在目录已由 PrependToolPath 前置，系统 PATH 上
    /// 也有真 node.exe，删掉它只是去掉一层遮蔽真 node 的有害间接。
    /// </para>
    /// </summary>
    [Test]
    public async Task Ensure_WritesNoNodeShim_SoNothingOnPathCanResolveBackToIt()
    {
        CreateToolchain();

        string directory = DesktopBinProvisioner.Ensure(DshHome, NodePath, PnpmEntry, RunnerPath);

        await Assert.That(File.Exists(Path.Combine(directory, "node.cmd"))).IsFalse();
        // 该目录会被前置进 PATH ⇒ 里面任何按名可解析的 node 都会成为 pnpm.cmd 的递归入口。
        foreach (string entry in Directory.EnumerateFiles(directory))
        {
            await Assert.That(Path.GetFileName(entry).StartsWith("node.", StringComparison.OrdinalIgnoreCase)).IsFalse();
        }
    }

    /// <summary>
    /// 🚨 升级路径：老版本落盘的 node.cmd 必须被清掉。宿主已装的用户目录里那个文件还在，
    /// 仅仅「不再写」不足以让市场恢复 —— 它会继续自递归挂死。
    /// </summary>
    [Test]
    public async Task Ensure_RemovesNodeShim_LeftByAnEarlierVersion()
    {
        CreateToolchain();
        string directory = Path.Combine(DshHome, ".desktop-bin");
        Directory.CreateDirectory(directory);
        string stale = Path.Combine(directory, "node.cmd");
        await File.WriteAllTextAsync(stale, "@chcp 65001 >nul\r\n@echo off\r\n\"node\" %*\r\n");

        DesktopBinProvisioner.Ensure(DshHome, NodePath, PnpmEntry, RunnerPath);

        await Assert.That(File.Exists(stale)).IsFalse();
        // pnpm.cmd 必须留着 —— 它才是市场按名调 pnpm 的唯一入口。
        await Assert.That(File.Exists(Path.Combine(directory, "pnpm.cmd"))).IsTrue();
    }

    /// <summary>
    /// 🚨 配置给的是 PATH 上的裸名（Detect 在找不到 vendored node 时就写 "node"）时，
    /// pnpm.cmd 里的 node 也必须解析到绝对路径 —— 否则它解析回同目录的 node.cmd，同一个坑。
    /// </summary>
    [Test]
    public async Task Ensure_BareNodeName_ResolvesToAbsolutePathInShim()
    {
        CreateToolchain();

        string directory = DesktopBinProvisioner.Ensure(DshHome, "node", PnpmEntry, RunnerPath);

        string pnpm = await File.ReadAllTextAsync(Path.Combine(directory, "pnpm.cmd"));
        await Assert.That(pnpm).Contains(".exe\"");
        await Assert.That(pnpm).DoesNotContain("\"node\"");
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
