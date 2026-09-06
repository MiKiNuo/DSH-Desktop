using Serilog;

namespace DshDesktop.Application.Runtime;

/// <summary>
/// 表示重接管探测的结论（ADR-0005）。
/// </summary>
public enum ReattachOutcome
{
    /// <summary>未探测到存活 Runtime（进程已死或 HTTP 不可达）→ 回退正常启动链。</summary>
    NotFound,

    /// <summary>探测到存活 Runtime 且 Session URL 可恢复 → 接管其监管，不拉起新进程。</summary>
    Adopted,

    /// <summary>Runtime 存活但 Session URL 不可恢复（一次性 token，禁止落盘）→ 已杀旧进程，退化重启。</summary>
    DegradedToRestart,
}

/// <summary>
/// 表示 Runtime 重接管探测器（ADR-0005，Phase 8 Issue 04）：
/// 按上次记录的 PID + 端口探测存活 Runtime（进程存活 + HTTP 健康检查）；
/// 探测原语经 <see cref="IRuntimeProbe"/> 端口注入（Phase 8 评审 F9），真实实现落 Infrastructure。
/// </summary>
public sealed class RuntimeReattacher
{
    private readonly IRuntimeProbe _probe;
    private readonly ILogger _logger;

    /// <summary>
    /// 初始化重接管探测器。
    /// </summary>
    /// <param name="probe">进程 / HTTP 探测端口。</param>
    /// <param name="logger">结构化日志。</param>
    public RuntimeReattacher(IRuntimeProbe probe, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(logger);
        _probe = probe;
        _logger = logger.ForContext("Source", "Supervisor");
    }

    /// <summary>
    /// 尝试重接管上次关窗时保留的 Runtime。
    /// </summary>
    /// <param name="host">监听地址。</param>
    /// <param name="processId">上次记录的 Runtime 进程 ID。</param>
    /// <param name="port">上次记录的监听端口。</param>
    /// <param name="canRestoreSessionUrl">DSH 是否支持无 token 重连（Session URL 一次性且禁止落盘）。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>接管结论。</returns>
    public async Task<ReattachOutcome> TryReattachAsync(
        string host,
        int processId,
        int port,
        bool canRestoreSessionUrl,
        CancellationToken cancellationToken)
    {
        if (!_probe.IsProcessAlive(processId))
        {
            _logger.Information("Runtime.Reattach.NotFound Reason=ProcessDead Pid={ProcessId}", processId);
            return ReattachOutcome.NotFound;
        }

        if (!await _probe.IsHttpAliveAsync(host, port, cancellationToken).ConfigureAwait(false))
        {
            _logger.Information(
                "Runtime.Reattach.NotFound Reason=HttpUnreachable Pid={ProcessId} Port={Port}", processId, port);
            return ReattachOutcome.NotFound;
        }

        if (canRestoreSessionUrl)
        {
            _logger.Information("Runtime.Reattach.Adopted Pid={ProcessId} Port={Port}", processId, port);
            return ReattachOutcome.Adopted;
        }

        // ADR-0005：Session URL 无法跨进程保留 → 接管后工作台无法恢复会话，退化重启并记录。
        _logger.Warning(
            "Runtime.Reattach.DegradeToRestart Pid={ProcessId} Port={Port}（Session URL 不可恢复）",
            processId, port);
        try
        {
            _probe.KillProcessTree(processId);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            // 探测与杀进程之间的竞态：进程刚死，按未找到处理。
            _logger.Information("Runtime.Reattach.NotFound Reason=ProcessExitedDuringKill Pid={ProcessId}", processId);
            return ReattachOutcome.NotFound;
        }

        return ReattachOutcome.DegradedToRestart;
    }
}
