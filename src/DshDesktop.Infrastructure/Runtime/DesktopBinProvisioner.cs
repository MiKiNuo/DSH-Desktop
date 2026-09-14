using System.Text;

namespace DshDesktop.Infrastructure.Runtime;

/// <summary>
/// 工具垫片目录（&lt;dshHome&gt;\.desktop-bin）：落盘 pnpm.cmd / node.cmd，让 pnpm 按名字可解析。
/// </summary>
/// <remarks>
/// 背景（2026-09-15 实机定位）：vendored pnpm 只以 pnpm.cjs（JS 入口，非可执行名）存在，而 DSH
/// 工作台内的 dsh-market 与其拉起的 dsh CLI 都【按名字】执行 pnpm ⇒ 探针 probePnpm() 必然失败，
/// 市场顶部常驻「安装插件前需要先配置 pnpm 环境」并拦停安装（宿主自己的插件管理不走按名解析，
/// 故能装）。上游 Electron 壳用 ensureProfilePnpmShim 落盘同样两个文件解决，本类是其等价物。
/// </remarks>
public static class DesktopBinProvisioner
{
    private const string DirectoryName = ".desktop-bin";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// 解析随包分发的 pnpm 运行器垫片路径（&lt;exe 旁&gt;\resources\pnpm-runner.mjs，
    /// 与 harness-node-entry.mjs 同目录同约定）。
    /// </summary>
    /// <param name="baseDirectory">exe 所在目录（生产传 AppContext.BaseDirectory）。</param>
    /// <returns>垫片路径（不保证存在）。</returns>
    public static string ResolveRunnerPath(string baseDirectory) =>
        Path.Combine(baseDirectory, "resources", "pnpm-runner.mjs");

    /// <summary>
    /// 确保垫片存在并返回垫片目录；幂等（每次覆盖写，跟随配置里的 node 路径变化）。
    /// </summary>
    /// <param name="dshHome">DSH 数据根；垫片随数据根迁移。</param>
    /// <param name="nodePath">node.exe 路径（可为 PATH 上的裸名，故不校验存在性——runtime 侧已有校验）。</param>
    /// <param name="pnpmEntryPath">vendored pnpm 入口（pnpm.cjs）。</param>
    /// <param name="runnerPath">pnpm 运行器垫片 pnpm-runner.mjs（随包分发）。</param>
    /// <returns>垫片目录绝对路径。</returns>
    public static string Ensure(string dshHome, string nodePath, string pnpmEntryPath, string runnerPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dshHome);
        if (string.IsNullOrWhiteSpace(pnpmEntryPath) || !File.Exists(pnpmEntryPath))
        {
            throw new InvalidOperationException(
                "找不到 vendored pnpm（pnpmCjsPath），无法生成 pnpm 垫片。请检查 dsh-desktop.config.json。");
        }

        if (string.IsNullOrWhiteSpace(runnerPath) || !File.Exists(runnerPath))
        {
            throw new InvalidOperationException(
                $"找不到 pnpm 运行器垫片 pnpm-runner.mjs（{runnerPath}），工作台内的插件市场将无法安装插件。"
                + "请检查安装包完整性（resources\\pnpm-runner.mjs 应随包分发）。");
        }

        string directory = Path.Combine(dshHome, DirectoryName);
        Directory.CreateDirectory(directory);

        // cmd 需要 CRLF；@chcp 65001 使后续 pnpm 输出的 UTF-8 中文不乱码。
        // 不写上游的 @set ELECTRON_RUN_AS_NODE=1：我们的 harness 跑在 vendored 原生 node.exe 上，非 Electron utility process。
        Write(
            Path.Combine(directory, "pnpm.cmd"),
            $"@chcp 65001 >nul\r\n@echo off\r\n\"{nodePath}\" \"{runnerPath}\" \"{pnpmEntryPath}\" %*\r\n");
        Write(
            Path.Combine(directory, "node.cmd"),
            $"@chcp 65001 >nul\r\n@echo off\r\n\"{nodePath}\" %*\r\n");
        return directory;
    }

    private static void Write(string path, string content) => File.WriteAllText(path, content, Utf8NoBom);
}
