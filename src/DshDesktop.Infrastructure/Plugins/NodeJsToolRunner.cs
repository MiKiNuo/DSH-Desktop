using System.Diagnostics;
using System.Text;

namespace DshDesktop.Infrastructure.Plugins;

/// <summary>
/// 表示 Node 工具（pnpm.cjs / npm-cli.js）的统一调用入口（CI=true 免 TTY 确认，双流尾部进错误信息）。
/// </summary>
internal static class NodeJsToolRunner
{
    /// <summary>
    /// 在指定工作目录执行工具。
    /// 若报 ERR_PNPM_UNEXPECTED_VIRTUAL_STORE（种子/复制 profile 的虚拟存储指向旧位置），
    /// 以 `pnpm install --force --no-frozen-lockfile` 真正重置虚拟存储：--force 才能绕过
    /// checkCompatibility 的 virtualStoreDir 校验、purge 后重建；--no-frozen-lockfile 规避
    /// CI=true 默认的冻结安装撞上 lockfile 漂移。先 --offline 后联网最多两步修复，再重试原命令。
    /// 修复步骤的结果不再被丢弃：最终重试仍失败时，其退出码与输出尾部附加进返回的 outputTail，
    /// 让上层能判断是修复环节本身坏了。runOnce 可注入以便测试，缺省走真实 RunOnceAsync。
    /// 保留 CI=true（免 TTY purge 确认所必需）。
    /// </summary>
    /// <returns>退出码与输出尾部。</returns>
    public static async Task<(int ExitCode, string OutputTail)> RunAsync(
        string nodePath,
        string toolCjsPath,
        string workingDirectory,
        string[] arguments,
        CancellationToken cancellationToken,
        Func<string, string, string, string[], CancellationToken, Task<(int ExitCode, string OutputTail)>>? runOnce = null)
    {
        Func<string, string, string, string[], CancellationToken, Task<(int ExitCode, string OutputTail)>> exec =
            runOnce ?? RunOnceAsync;

        (int exitCode, string outputTail) = await exec(
            nodePath, toolCjsPath, workingDirectory, arguments, cancellationToken).ConfigureAwait(false);

        if (exitCode != 0 && outputTail.Contains("ERR_PNPM_UNEXPECTED_VIRTUAL_STORE", StringComparison.Ordinal))
        {
            // 修复步骤：--force 绕过 virtualStoreDir 校验并 purge 重建；--no-frozen-lockfile 规避
            // CI=true 默认的冻结安装撞 lockfile 漂移。先 --offline（离线优先），失败再去掉 --offline 联网重试。
            (int repairExit, string repairTail) = await exec(
                nodePath, toolCjsPath, workingDirectory,
                ["install", "--force", "--no-frozen-lockfile", "--offline"], cancellationToken).ConfigureAwait(false);
            if (repairExit != 0)
            {
                (repairExit, repairTail) = await exec(
                    nodePath, toolCjsPath, workingDirectory,
                    ["install", "--force", "--no-frozen-lockfile"], cancellationToken).ConfigureAwait(false);
            }

            (exitCode, outputTail) = await exec(
                nodePath, toolCjsPath, workingDirectory, arguments, cancellationToken).ConfigureAwait(false);

            // 修复失败且最终重试仍失败：把修复环节的退出码与输出附加进去，避免「吞掉退出码」。
            if (repairExit != 0 && exitCode != 0)
            {
                outputTail = $"{outputTail}\n[修复步骤失败（退出码 {repairExit}）] {repairTail}";
            }
        }

        return (exitCode, outputTail);
    }

    /// <summary>
    /// 离线优先的 <c>pnpm install --no-frozen-lockfile</c> 重建（§34 Offline First）：
    /// 先带 <c>--offline</c> 执行，失败则去掉 <c>--offline</c> 联网重试一次，返回最后一次结果。
    /// 2026-09-15 架构审查收编：ProfileSeeder / PluginProfileRepository / ProfileSnapshotter
    /// 三处同构实现曾各写一份，pnpm 参数纪律（--force / --no-frozen-lockfile）只能单点演化。
    /// </summary>
    /// <returns>最后一次执行的退出码与输出尾部。</returns>
    public static async Task<(int ExitCode, string OutputTail)> InstallWithOfflineFallbackAsync(
        string nodePath,
        string toolCjsPath,
        string workingDirectory,
        CancellationToken cancellationToken,
        Func<string, string, string, string[], CancellationToken, Task<(int ExitCode, string OutputTail)>>? runOnce = null)
    {
        (int exitCode, string outputTail) = await RunAsync(
            nodePath, toolCjsPath, workingDirectory,
            ["install", "--no-frozen-lockfile", "--offline"], cancellationToken, runOnce).ConfigureAwait(false);
        if (exitCode != 0)
        {
            (exitCode, outputTail) = await RunAsync(
                nodePath, toolCjsPath, workingDirectory,
                ["install", "--no-frozen-lockfile"], cancellationToken, runOnce).ConfigureAwait(false);
        }

        return (exitCode, outputTail);
    }

    /// <summary>
    /// 在指定工作目录执行工具并捕获完整 stdout（供 npm view 等查询命令解析输出）。
    /// </summary>
    /// <returns>退出码与完整 stdout。</returns>
    public static async Task<(int ExitCode, string Stdout)> RunCaptureAsync(
        string nodePath,
        string toolCjsPath,
        string workingDirectory,
        string[] arguments,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = BuildStartInfo(nodePath, toolCjsPath, workingDirectory, arguments);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动工具进程。");
        string stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        _ = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return (process.ExitCode, stdout.Trim());
    }

    private static async Task<(int ExitCode, string OutputTail)> RunOnceAsync(
        string nodePath,
        string toolCjsPath,
        string workingDirectory,
        string[] arguments,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = BuildStartInfo(nodePath, toolCjsPath, workingDirectory, arguments);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动工具进程。");
        StringBuilder stderr = new();
        StringBuilder stdout = new();
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is { } line && stderr.Length < 4096)
            {
                stderr.AppendLine(line);
            }
        };
        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is { } line && stdout.Length < 4096)
            {
                stdout.AppendLine(line);
            }
        };
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        // pnpm 部分错误（如 ERR_PNPM_* 详情）走 stdout，合并两者供诊断。
        string tail = stderr.ToString().Trim();
        if (tail.Length == 0)
        {
            tail = stdout.ToString().Trim();
        }

        return (process.ExitCode, tail);
    }

    private static ProcessStartInfo BuildStartInfo(
        string nodePath,
        string toolCjsPath,
        string workingDirectory,
        string[] arguments)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = nodePath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add(toolCjsPath);
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        // pnpm 无 TTY 时拒绝 purge modules 目录（ERR_PNPM_ABORTED_REMOVE_MODULES_DIR_NO_TTY）。
        startInfo.Environment["CI"] = "true";
        return startInfo;
    }
}
