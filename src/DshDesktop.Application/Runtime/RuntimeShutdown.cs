using Serilog;

namespace DshDesktop.Application.Runtime;

/// <summary>
/// 表示应用退出时的 Runtime 处置分叉（ADR-0005，Phase 8 Issue 04）。
/// </summary>
public static class RuntimeShutdown
{
    /// <summary>
    /// 执行 Shutdown 分叉：KeepRuntimeOnClose 开 = 关窗只退 Desktop、Runtime 保留；
    /// 关 = Phase 7 现状（关窗停 Runtime）。
    /// </summary>
    /// <param name="supervisor">Runtime 监管器；未初始化为 null。</param>
    /// <param name="keepRuntimeOnClose">"关闭窗口后保持 DSH Runtime"开关当前值。</param>
    /// <param name="logger">结构化日志。</param>
    public static void ShutdownRuntime(IRuntimeSupervisor? supervisor, bool keepRuntimeOnClose, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        if (keepRuntimeOnClose)
        {
            logger.Information("Desktop.Shutdown.KeepRuntime（ADR-0005：Runtime 进程保留，待下次启动重接管）");
            return;
        }

        supervisor?.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
    }
}
