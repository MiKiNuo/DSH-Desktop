using System.IO.Compression;
using System.Net.Http;

namespace DshDesktop.Infrastructure.Runtime;

/// <summary>
/// node 自举器：干净机器（无 Electron 借用安装、PATH 无 node）首启安装 DSH Runtime 前，
/// 把官方 Node.js zip 下载解压到 &lt;dataRoot&gt;\tools\node（与 tools\pnpm 并列的宿主自持工具链约定）。
/// 与 PnpmProvisioner 的「装不了 ≠ 起不来」不同：本组件只在用户显式点「下载并安装」后运行，
/// 失败必须抛带中文原因的异常让弹窗如实展示（静默失败会让用户以为装好了）。
/// </summary>
public static class NodeProvisioner
{
    /// <summary>
    /// 锁定的 Node.js 版本：v24.9.0 实机 EPERM（fs.symlink junction），v24.19.0 实测正常，勿回退。
    /// </summary>
    public const string NodeVersion = "24.19.0";

    /// <summary>官方 dist 下载地址（Windows x64 zip 包，内含 node.exe 与 npm）。</summary>
    private const string DownloadUrl =
        "https://nodejs.org/dist/v" + NodeVersion + "/node-v" + NodeVersion + "-win-x64.zip";

    /// <summary>
    /// 测试注入缝：与 PnpmProvisioner.ToolRunner 同一既有范式。缺省走真实 HttpClient 下载。
    /// </summary>
    public delegate Task Downloader(
        string url, string destinationPath, IProgress<int>? progress, CancellationToken cancellationToken);

    /// <summary>下载超时不受 HttpClient 默认 100s 限制（30MB+ 包在慢网络下必然超时）；取消走 CancellationToken。</summary>
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>
    /// 判定 node 是否可用：空白或失效文件路径不可用；PATH 裸名 "node" 无法 File.Exists 校验，按可用处理
    /// （与 HealRuntimePaths 的判定口径一致）；真实路径须文件存在。
    /// </summary>
    public static bool IsNodeAvailable(string? nodePath) =>
        !string.IsNullOrWhiteSpace(nodePath)
        && (string.Equals(nodePath, "node", StringComparison.OrdinalIgnoreCase) || File.Exists(nodePath));

    /// <summary>
    /// 确保 &lt;dataRoot&gt;\tools\node 下 node 可用，返回工具链路径。
    /// 已装过（node.exe 存在）直接短路返回，不触发下载。
    /// </summary>
    /// <param name="dataRoot">数据根；产物落在 &lt;dataRoot&gt;\tools\node。</param>
    /// <param name="progress">下载进度百分比（0-100）；可为 null。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <param name="downloader">测试注入缝；缺省走真实 HttpClient 下载。</param>
    /// <returns>node.exe 与随包 npm-cli.js 的路径。</returns>
    /// <exception cref="InvalidOperationException">下载失败或产物不完整（消息为中文，供弹窗直接展示）。</exception>
    public static async Task<NodeToolchain> EnsureAvailableAsync(
        string dataRoot,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default,
        Downloader? downloader = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);

        string nodeDir = Path.Combine(dataRoot, "tools", "node");
        string nodeExe = Path.Combine(nodeDir, "node.exe");
        if (File.Exists(nodeExe))
        {
            return new NodeToolchain(nodeExe, DeriveBundledNpm(nodeDir));
        }

        string zipPath = Path.Combine(dataRoot, "tools", $"node-v{NodeVersion}-win-x64.zip");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
            await (downloader ?? DownloadAsync)(DownloadUrl, zipPath, progress, cancellationToken)
                .ConfigureAwait(false);

            // 清掉历史失败残留再解压（半成品目录不得与新产物混合）。
            if (Directory.Exists(nodeDir))
            {
                Directory.Delete(nodeDir, recursive: true);
            }

            ExtractStrippingTopDirectory(zipPath, nodeDir);

            if (!File.Exists(nodeExe))
            {
                throw new InvalidOperationException(
                    $"Node.js 安装包内容不完整：解压后找不到 node.exe（{nodeExe}）。");
            }

            return new NodeToolchain(nodeExe, DeriveBundledNpm(nodeDir));
        }
        catch (Exception exception)
        {
            // 失败必须清场：残留半目录会被下次启动的短路判据误认为已安装。
            TryDeleteDirectory(nodeDir);
            throw exception is InvalidOperationException or OperationCanceledException
                ? exception
                : new InvalidOperationException($"下载 Node.js 失败：{exception.Message}", exception);
        }
        finally
        {
            TryDeleteFile(zipPath);
        }
    }

    /// <summary>官方 zip 布局：npm 随包内置于 node_modules\npm\bin\npm-cli.js；缺失返回 null（调用方诚实报错）。</summary>
    private static string? DeriveBundledNpm(string nodeDir)
    {
        string candidate = Path.Combine(nodeDir, "node_modules", "npm", "bin", "npm-cli.js");
        return File.Exists(candidate) ? candidate : null;
    }

    /// <summary>剥掉官方 zip 的顶层目录（node-vX-win-x64/）解压到目标目录。</summary>
    private static void ExtractStrippingTopDirectory(string zipPath, string targetDir)
    {
        using ZipArchive archive = ZipFile.OpenRead(zipPath);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string relative = entry.FullName;
            int slash = relative.IndexOf('/');
            if (slash < 0)
            {
                continue;
            }

            relative = relative[(slash + 1)..];
            // 目录条目（以 / 结尾）与路径穿越条目跳过。
            if (relative.Length == 0 || relative.EndsWith('/')
                || relative.Contains("..", StringComparison.Ordinal))
            {
                continue;
            }

            string destination = Path.Combine(targetDir, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);
        }
    }

    private static async Task DownloadAsync(
        string url, string destinationPath, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await Http
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        long? total = response.Content.Headers.ContentLength;
        await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using FileStream target = new(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);

        byte[] buffer = new byte[81920];
        long copied = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            copied += read;
            if (total is > 0)
            {
                progress?.Report((int)(copied * 100 / total.Value));
            }
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // 清理失败不影响报错主流程；下次自举前会重删。
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // 残留 zip 无害（下次下载同路径覆盖）。
        }
    }
}

/// <summary>node 自举产物：node.exe 路径与随包 npm-cli.js 路径（npm 缺失为 null）。</summary>
/// <param name="NodePath">node.exe 绝对路径。</param>
/// <param name="NpmCjsPath">npm-cli.js 绝对路径；官方包内置，异常缺失时为 null。</param>
public sealed record NodeToolchain(string NodePath, string? NpmCjsPath);
