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
    /// 先以 `pnpm install --offline` 重置虚拟存储再重试一次。
    /// </summary>
    /// <returns>退出码与输出尾部。</returns>
    public static async Task<(int ExitCode, string OutputTail)> RunAsync(
        string nodePath,
        string toolCjsPath,
        string workingDirectory,
        string[] arguments,
        CancellationToken cancellationToken)
    {
        (int exitCode, string outputTail) = await RunOnceAsync(
            nodePath, toolCjsPath, workingDirectory, arguments, cancellationToken).ConfigureAwait(false);

        if (exitCode != 0 && outputTail.Contains("ERR_PNPM_UNEXPECTED_VIRTUAL_STORE", StringComparison.Ordinal))
        {
            _ = await RunOnceAsync(
                nodePath, toolCjsPath, workingDirectory,
                ["install", "--offline"], cancellationToken).ConfigureAwait(false);
            (exitCode, outputTail) = await RunOnceAsync(
                nodePath, toolCjsPath, workingDirectory, arguments, cancellationToken).ConfigureAwait(false);
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
