using System.IO.Compression;
using DshDesktop.Infrastructure.Runtime;

namespace DshDesktop.Tests;

/// <summary>
/// NodeProvisioner 测试：干净机器（无 Electron 借用安装、PATH 无 node）首启时，
/// 把官方 Node.js zip 下载解压到 &lt;dataRoot&gt;\tools\node 自举（与 tools\pnpm 并列的宿主自持工具链约定）。
/// 通过注入 <see cref="NodeProvisioner.Downloader"/> 假实现避免真实网络下载；
/// zip 用 System.IO.Compression 真实构造，走真实解压路径。
/// </summary>
public sealed class NodeProvisionerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "node-prov-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>
    /// node 可用性判定：空值与失效路径不可用；PATH 裸名 "node" 与存在的文件视为可用。
    /// </summary>
    [Test]
    public async Task IsNodeAvailable_ClassifiesInputs()
    {
        await Assert.That(NodeProvisioner.IsNodeAvailable(null)).IsFalse();
        await Assert.That(NodeProvisioner.IsNodeAvailable("")).IsFalse();
        await Assert.That(NodeProvisioner.IsNodeAvailable("   ")).IsFalse();
        await Assert.That(NodeProvisioner.IsNodeAvailable(Path.Combine(_root, "no-such-node.exe"))).IsFalse();

        await Assert.That(NodeProvisioner.IsNodeAvailable("node")).IsTrue();

        string existing = Path.Combine(_root, "node.exe");
        Directory.CreateDirectory(_root);
        File.WriteAllText(existing, "fake");
        await Assert.That(NodeProvisioner.IsNodeAvailable(existing)).IsTrue();
    }

    /// <summary>
    /// tools\node\node.exe 已存在时必须短路返回，绝不触发下载（正常路径零开销）。
    /// </summary>
    [Test]
    public async Task EnsureAvailableAsync_ExistingNode_ShortCircuitsWithoutDownload()
    {
        string nodeDir = Path.Combine(_root, "tools", "node");
        string nodeExe = Path.Combine(nodeDir, "node.exe");
        Directory.CreateDirectory(nodeDir);
        File.WriteAllText(nodeExe, "fake node");

        int calls = 0;
        NodeProvisioner.Downloader downloader = (_, _, _, _) =>
        {
            calls++;
            return Task.CompletedTask;
        };

        NodeToolchain toolchain = await NodeProvisioner.EnsureAvailableAsync(
            _root, null, CancellationToken.None, downloader);

        await Assert.That(toolchain.NodePath).IsEqualTo(nodeExe);
        await Assert.That(calls).IsEqualTo(0);
    }

    /// <summary>
    /// 无 node 时下载官方 zip 并剥掉顶层目录解压：返回的 NodePath / NpmCjsPath 均真实存在，
    /// 下载用的临时 zip 必须清理。
    /// </summary>
    [Test]
    public async Task EnsureAvailableAsync_NoNode_DownloadsAndExtractsToolchain()
    {
        Directory.CreateDirectory(_root);
        string? capturedUrl = null;
        NodeProvisioner.Downloader downloader = (url, destinationPath, _, _) =>
        {
            capturedUrl = url;
            WriteNodeZip(destinationPath, includeNodeExe: true, includeNpm: true);
            return Task.CompletedTask;
        };

        NodeToolchain toolchain = await NodeProvisioner.EnsureAvailableAsync(
            _root, null, CancellationToken.None, downloader);

        string nodeDir = Path.Combine(_root, "tools", "node");
        await Assert.That(toolchain.NodePath).IsEqualTo(Path.Combine(nodeDir, "node.exe"));
        await Assert.That(File.Exists(toolchain.NodePath)).IsTrue();
        await Assert.That(toolchain.NpmCjsPath).IsEqualTo(
            Path.Combine(nodeDir, "node_modules", "npm", "bin", "npm-cli.js"));
        await Assert.That(File.Exists(toolchain.NpmCjsPath!)).IsTrue();
        // 官方 dist 地址 + 版本锁定（v24.9.0 junction EPERM，v24.19.0 实测正常，勿回退）。
        await Assert.That(capturedUrl).IsEqualTo(
            $"https://nodejs.org/dist/v{NodeProvisioner.NodeVersion}/node-v{NodeProvisioner.NodeVersion}-win-x64.zip");
        // 临时 zip 不残留。
        await Assert.That(Directory.GetFiles(Path.Combine(_root, "tools"), "*.zip")).IsEmpty();
    }

    /// <summary>
    /// zip 内容缺 node.exe（损坏 / 被劫持的错误产物）必须抛 InvalidOperationException，
    /// 且清理半成品目录——否则下次启动会把残目录当成已安装。
    /// </summary>
    [Test]
    public async Task EnsureAvailableAsync_ZipMissingNodeExe_ThrowsAndCleansUp()
    {
        Directory.CreateDirectory(_root);
        NodeProvisioner.Downloader downloader = (_, destinationPath, _, _) =>
        {
            WriteNodeZip(destinationPath, includeNodeExe: false, includeNpm: true);
            return Task.CompletedTask;
        };

        await Assert.That(async () => await NodeProvisioner.EnsureAvailableAsync(
                _root, null, CancellationToken.None, downloader))
            .Throws<InvalidOperationException>();

        await Assert.That(Directory.Exists(Path.Combine(_root, "tools", "node"))).IsFalse();
    }

    /// <summary>
    /// 下载抛错（网络失败）必须转成带中文原因的 InvalidOperationException（弹窗直接展示），
    /// 并清理半成品目录。
    /// </summary>
    [Test]
    public async Task EnsureAvailableAsync_DownloadFails_ThrowsChineseMessageAndCleansUp()
    {
        Directory.CreateDirectory(_root);
        NodeProvisioner.Downloader downloader = (_, _, _, _) =>
            throw new HttpRequestException("simulated network down");

        await Assert.That(async () => await NodeProvisioner.EnsureAvailableAsync(
                _root, null, CancellationToken.None, downloader))
            .Throws<InvalidOperationException>();

        await Assert.That(Directory.Exists(Path.Combine(_root, "tools", "node"))).IsFalse();
    }

    /// <summary>构造与官方 node zip 同构的测试包（顶层目录 node-vX-win-x64/）。</summary>
    private static void WriteNodeZip(string zipPath, bool includeNodeExe, bool includeNpm)
    {
        string top = $"node-v{NodeProvisioner.NodeVersion}-win-x64/";
        using FileStream stream = new(zipPath, FileMode.Create);
        using ZipArchive archive = new(stream, ZipArchiveMode.Create);
        if (includeNodeExe)
        {
            WriteEntry(archive, top + "node.exe");
        }

        if (includeNpm)
        {
            WriteEntry(archive, top + "node_modules/npm/bin/npm-cli.js");
        }
    }

    private static void WriteEntry(ZipArchive archive, string name)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name);
        using Stream writer = entry.Open();
        writer.WriteByte(0x4E); // 'N'
    }
}
