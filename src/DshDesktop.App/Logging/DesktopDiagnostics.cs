using System.IO;
using DshDesktop.Application.Diagnostics;
using DshDesktop.Application.Runtime;
using DshDesktop.Domain.Diagnostics;
using DshDesktop.Domain.Runtime;
using DshDesktop.Infrastructure.Config;
using R3;
using Serilog;
using Serilog.Events;

namespace DshDesktop.App.Logging;

/// <summary>
/// 组合根拆分的日志 / 诊断管道模块（批 1 宿主侧）：持有诊断 hub 与 stdout/stderr 子 logger，
/// 负责旧数据根一次性迁移、日志目录确立、进程输出落盘与诊断事件回流 Live 控制台。
/// 根不重复保有 hub / 子 logger 状态——订阅与处理统一收口在此，避免与根分裂同一份状态。
/// </summary>
internal sealed class DesktopDiagnostics
{
    private readonly DiagnosticsHub _hub = new();
    private Serilog.ILogger _stdoutLogger = null!;
    private Serilog.ILogger _stderrLogger = null!;

    /// <summary>诊断事件 hub（托盘订阅、诊断命令发布与 Live 控制台回流共用）。</summary>
    public DiagnosticsHub Hub => _hub;

    /// <summary>
    /// ADR-0009 旧数据根一次性迁移 + 日志目录确立。迁移必须先于日志目录（迁移把旧根 logs 一并搬走，
    /// 且迁移后日志须落新根），故本方法先迁移再创建 logger。返回迁移结果供调用方在订阅后补记日志。
    /// </summary>
    public LegacyDataRootMigrationOutcome InitializeLogging()
    {
        // 迁移必须先于日志目录确立：迁移把旧根 logs 一并搬走，且日志必须落在**新**根。
        // 失败不阻断——旧根还在，启动链按新根继续。
        LegacyDataRootMigrationOutcome migrationOutcome = DshDesktopConfigStore.MigrateLegacyDataRootIfNeeded();

        // 日志目录统一走数据根（ADR-0003：Velopack 安装后落到 &lt;安装根&gt;\data\logs）。
        string logDirectory = Path.Combine(DshDesktopConfigStore.DataRoot, "logs");
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                Path.Combine(logDirectory, "dsh-desktop-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14)
            .WriteTo.Sink(new DiagnosticsSink(_hub))
            .CreateLogger();
        return migrationOutcome;
    }

    /// <summary>
    /// 在容器就绪后创建 stdout/stderr 子 logger（按诊断源分流进程输出），须在
    /// <see cref="SubscribeToStore"/> 之前完成，使事件回流前日志管线已就绪。
    /// </summary>
    public void InitializeSubLoggers()
    {
        _stdoutLogger = Log.Logger.ForContext("Source", nameof(DiagnosticSource.DshStdout));
        _stderrLogger = Log.Logger.ForContext("Source", nameof(DiagnosticSource.DshStderr));
    }

    /// <summary>
    /// 容器就绪后把诊断 hub 事件回流到 Diagnostics Store（Live 控制台投影）。
    /// 须晚于 <see cref="InitializeLogging"/>，确保 Desktop.Startup 与迁移结果日志先落盘并被订阅捕获。
    /// </summary>
    public void SubscribeToStore(Action<DiagnosticEvent> onDiagnosticEvent)
        => _hub.Events.Subscribe(onDiagnosticEvent);

    public void LogStartup(LegacyDataRootMigrationOutcome migrationOutcome)
    {
        Log.Logger.Information("Desktop.Startup");
        if (!migrationOutcome.Attempted)
        {
            return;
        }

        if (migrationOutcome.Error is null)
        {
            Log.Logger.Information("Desktop.DataRoot.Migrated {DataRoot}", DshDesktopConfigStore.DataRoot);
        }
        else
        {
            Log.Logger.Warning(
                "Desktop.DataRoot.MigrationFailed {Error}（迁移未完成，按新根继续初始化）",
                migrationOutcome.Error);
        }

        if (migrationOutcome.Error is null && migrationOutcome.CleanupError is { } cleanupError)
        {
            Log.Logger.Warning(
                "Desktop.DataRoot.LegacyRootNotRemoved {Error}（内容已并入新根，旧根残留可手工删除）",
                cleanupError);
        }

        if (migrationOutcome.SafeModeCleared)
        {
            Log.Logger.Information("Desktop.DataRoot.InheritedSafeModeCleared");
        }
    }

    /// <summary>
    /// 进程输出落盘（Session token 禁止落盘，规则全仓单源 Domain）：stderr 记 Warning、stdout 记 Information。
    /// </summary>
    public void OnProcessOutputReceived(object? sender, ProcessOutputLineEventArgs args)
    {
        string line = SessionUrlRedactor.Redact(args.Line)!;
        Serilog.ILogger logger = args.IsError ? _stderrLogger : _stdoutLogger;
        logger.Write(args.IsError ? LogEventLevel.Warning : LogEventLevel.Information, "{Line}", line);
    }
}
