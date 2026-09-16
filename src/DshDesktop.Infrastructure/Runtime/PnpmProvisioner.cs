using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DshDesktop.Application.Diagnostics;
using DshDesktop.Infrastructure.Plugins;
using Serilog;

namespace DshDesktop.Infrastructure.Runtime;

/// <summary>
/// pnpm 自举器：宿主 pnpm.cjs 缺失（上游 Electron 安装更新后 node_modules 只剩 node）时，
/// 用宿主 npm 把 pnpm 安装到 &lt;dataRoot&gt;\tools\pnpm，使宿主插件页 / profile 回滚重建可用。
/// 设计原则与本仓一致：「装不了插件 ≠ 应用起不来」——任何失败都返回 null 且留可观测日志，绝不抛异常
/// （除参数校验）。
/// 目标布局与宿主自持工具链的既有约定一致（<c>&lt;dataRoot&gt;\tools\node</c> + <c>&lt;dataRoot&gt;\tools\pnpm</c>），
/// 勿落在 <c>.desktop-bin</c>（那是 DesktopBinProvisioner 写 pnpm.cmd / node.cmd 垫片的地盘）。
/// </summary>
public static class PnpmProvisioner
{
    // profile 的 pnpm-lock.yaml 是 lockfileVersion '9.0'、.modules.yaml 是 layoutVersion 5，
    // 对应 pnpm 9.x–10.x；取 10 与宿主自持工具链的既有安装保持一致。
    // 版本写死为常量、不暴露可配置项（ponytail：值永远不变就不该是可配置项）。
    private const string PnpmMajorVersion = "10";

    // 目标目录名（相对数据根）：与 node 自持目录 tools\node 并列。
    private const string ToolsDirName = "tools";
    private const string PnpmDirName = "pnpm";

    /// <summary>
    /// 测试注入缝：与 ProfileSeeder.DependencyInstaller 同一既有范式。缺省走真实 NodeJsToolRunner。
    /// </summary>
    public delegate Task<(int ExitCode, string OutputTail)> ToolRunner(
        string nodePath, string toolCjsPath, string workingDirectory,
        string[] arguments, CancellationToken cancellationToken);

    /// <summary>
    /// 返回可用的 pnpm.cjs 绝对路径；无法确保可用时返回 null（绝不抛异常）。
    /// 行为按序短路：
    /// 1) currentPnpmCjsPath 已存在 ⇒ 直接返回（正常路径零开销）；
    /// 2) 目标目录已装过 ⇒ 直接返回；
    /// 3) npm 不可用 ⇒ 记 Warning 返回 null；
    /// 4) 用宿主 npm 安装 pnpm@10；
    /// 5) 安装后产物存在 ⇒ 返回，否则记 Error 返回 null。
    /// </summary>
    /// <param name="dataRoot">数据根；产物落在 &lt;dataRoot&gt;\tools\pnpm。</param>
    /// <param name="nodePath">node.exe 路径（可为 PATH 上的裸名）。</param>
    /// <param name="npmCjsPath">npm-cli.js 路径；不可用时无法自举。</param>
    /// <param name="currentPnpmCjsPath">配置中现有的 pnpm.cjs 路径（优先复用）。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <param name="runner">测试注入缝；缺省走真实 NodeJsToolRunner。</param>
    public static async Task<string?> EnsureAvailableAsync(
        string dataRoot,
        string nodePath,
        string? npmCjsPath,
        string? currentPnpmCjsPath,
        CancellationToken cancellationToken = default,
        ToolRunner? runner = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodePath);

        // 1. 现成可用：正常路径零开销短路，不做任何事（不建目录、不摸 npm）。
        if (!string.IsNullOrWhiteSpace(currentPnpmCjsPath) && File.Exists(currentPnpmCjsPath))
        {
            return currentPnpmCjsPath;
        }

        string target = Path.Combine(dataRoot, ToolsDirName, PnpmDirName);
        string resolved = Path.Combine(target, "node_modules", "pnpm", "bin", "pnpm.cjs");

        // 2. 上次已装：直接返回，不重复安装。
        if (File.Exists(resolved))
        {
            return resolved;
        }

        // 3. 无 npm 无法自举：记 Warning 返回 null（非致命）。
        if (string.IsNullOrWhiteSpace(npmCjsPath) || !File.Exists(npmCjsPath))
        {
            Log.Logger.Warning(
                DiagnosticEventNames.PnpmProvisionFailed,
                "无法自举 pnpm：宿主 npm 不可用（NpmCjsPath={NpmCjsPath}），本运行插件安装/更新与 profile 回滚重建将不可用。",
                npmCjsPath ?? "<null>");
            return null;
        }

        // 4. 用宿主 npm 安装 pnpm@9（参数纪律照抄 RuntimeRepository.InstallAsync 既有用法：
        // 不用 InstallWithOfflineFallbackAsync——它会拼 --no-frozen-lockfile --offline，npm 不认）。
        Directory.CreateDirectory(target);
        (int exitCode, string outputTail) = await (runner ?? DefaultRunner)(
            nodePath, npmCjsPath, target,
            ["install", $"pnpm@{PnpmMajorVersion}", "--no-audit", "--no-fund"],
            cancellationToken).ConfigureAwait(false);

        // 5. 安装后校验产物存在再返回；否则记 Error（含 npm 退出码与输出尾部）返回 null。
        if (File.Exists(resolved))
        {
            return resolved;
        }

        Log.Logger.Error(
            DiagnosticEventNames.PnpmProvisionFailed,
            "自举 pnpm 失败（npm 退出码 {ExitCode}）：{OutputTail}",
            exitCode,
            outputTail);
        return null;
    }

    private static Task<(int ExitCode, string OutputTail)> DefaultRunner(
        string nodePath, string toolCjsPath, string workingDirectory,
        string[] arguments, CancellationToken cancellationToken) =>
        NodeJsToolRunner.RunAsync(nodePath, toolCjsPath, workingDirectory, arguments, cancellationToken);
}
