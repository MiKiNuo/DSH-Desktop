using System.Text;

namespace DshDesktop.Infrastructure.Runtime;

/// <summary>
/// 工具垫片目录（&lt;dshHome&gt;\.desktop-bin）：落盘 pnpm.cmd，让 pnpm 按名字可解析。
/// </summary>
/// <remarks>
/// 背景（2026-09-15 实机定位）：vendored pnpm 只以 pnpm.cjs（JS 入口，非可执行名）存在，而 DSH
/// 工作台内的 dsh-market 与其拉起的 dsh CLI 都【按名字】执行 pnpm ⇒ 探针 probePnpm() 必然失败，
/// 市场顶部常驻「安装插件前需要先配置 pnpm 环境」并拦停安装（宿主自己的插件管理不走按名解析，
/// 故能装）。上游 Electron 壳用 ensureProfilePnpmShim 落盘 pnpm.cmd / node.cmd 两个文件解决。
/// 本类只保留 pnpm.cmd：node.cmd 见 <see cref="RemoveLegacyNodeShim"/>，它在 PATH 首位会自递归挂死。
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
    /// <param name="nodePath">node.exe 路径。为 PATH 上的裸名时会被解析成绝对路径。</param>
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

        string node = ResolveNode(nodePath);

        // cmd 需要 CRLF；@chcp 65001 使后续 pnpm 输出的 UTF-8 中文不乱码。
        // 不写上游的 @set ELECTRON_RUN_AS_NODE=1：我们的 harness 跑在 vendored 原生 node.exe 上，非 Electron utility process。
        Write(
            Path.Combine(directory, "pnpm.cmd"),
            $"@chcp 65001 >nul\r\n@echo off\r\n\"{node}\" \"{runnerPath}\" \"{pnpmEntryPath}\" %*\r\n");
        RemoveLegacyNodeShim(directory);
        return directory;
    }

    /// <summary>
    /// 清掉历史版本落盘的 <c>node.cmd</c>。它必须消失，而不是被改写。
    /// </summary>
    /// <remarks>
    /// 2026-10-13 实机定位：老版本在这里写过 <c>node.cmd</c>，内容是 <c>"node" %*</c>。本目录由
    /// <c>DshProcessHost.PrependToolPath</c> 前置进 harness PATH 的<b>首位</b>，于是 pnpm.cmd 里的
    /// <c>"node"</c> 按名解析回它自己。cmd 对 <c>.cmd</c> 批处理的自调用只 fork 不返回、没有深度上限
    /// （实测逐层 1→12 持续下去）⇒ <c>pnpm --version</c> 永久挂死：不报错、不退出、<b>不写任何日志</b>。
    /// dshmarket 的 pnpm 探针（<c>cmd /d /s /c "pnpm --version"</c>）没有超时，于是「正在安装 1/1」
    /// 永远转下去，而 pnpm 从未被启动 —— 宿主日志、.dsh-market/log.ndjson、.plugin-manager/logs 三处全空。
    /// <para>
    /// node 本身不需要这个垫片：NodePath 所在目录已由 PrependToolPath 前置，系统 PATH 上也有真 node.exe，
    /// 留着它只是多一层遮蔽真 node 的有害间接。仅「不再写」救不了已装用户，故此处主动删除。
    /// </para>
    /// </remarks>
    private static void RemoveLegacyNodeShim(string directory)
    {
        foreach (string stale in Directory.EnumerateFiles(directory, "node.*"))
        {
            File.Delete(stale);
        }
    }

    /// <summary>
    /// 垫片里的 node 必须是绝对路径：裸名会被 PATH 首位解析回本目录（见 <see cref="RemoveLegacyNodeShim"/>）。
    /// 能按名解析时就地解析，解析不到则原样透传 —— 启动路径另有校验，垫片不该因此拒绝服务。
    /// </summary>
    private static string ResolveNode(string nodePath)
    {
        if (!string.IsNullOrWhiteSpace(nodePath) && !Path.IsPathRooted(nodePath))
        {
            foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                foreach (string extension in new[] { ".exe", ".cmd", ".bat" })
                {
                    string candidate = Path.Combine(directory.Trim('"'), nodePath + extension);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
        }

        return nodePath;
    }

    private static void Write(string path, string content) => File.WriteAllText(path, content, Utf8NoBom);
}
