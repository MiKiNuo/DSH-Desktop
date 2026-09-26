using DshDesktop.Application.Diagnostics;
using DshDesktop.Domain.Runtime;
using Serilog;

namespace DshDesktop.Application.Runtime;

/// <summary>
/// Runtime 生命周期恢复编排（批 2b，独立于 Bootstrapper：引导 ≠ 恢复）：
/// ① ADR-0007 有界自动恢复（Failed 新沿 → 延迟重派 StartRuntime，上限 1 次，Running 沿 Reset）；
/// ② ADR-0004 修订注连续启动失败计数（连续 2 次且开关开 → 经端口进安全模式）；
/// ③ ADR-0005 重接管判定（KeepRuntimeOnClose + 上次 PID/端口谓词 + Adopted/NotFound/Degraded 分支）；
/// ④ Phase 8 Issue 03 指标判定（启动耗时去重 + 首次落盘 + 指标监控启停 + Running 对账派发）。
/// 全部副作用经 <see cref="IRuntimeRecoveryHost"/> / <see cref="IRuntimeRecoveryConfig"/> 端口；
/// Application 不触碰 MVI Store 与 config 写。纯逻辑（<see cref="BoundedRecoveryPlanner"/> /
/// <see cref="StartupFailureTracker"/>）同模块直用，可直测。
/// </summary>
public sealed class RuntimeRecoveryController
{
    // ADR-0007：Failed 后的自动恢复延迟（短暂延迟避开"刚失败即重试"的紧密循环，也给 UI 渲染 Failed）。
    public static readonly TimeSpan RecoveryRetryDelay = TimeSpan.FromSeconds(3);

    private readonly IRuntimeRecoveryHost _host;
    private readonly IRuntimeRecoveryConfig _config;
    private readonly IRuntimeSupervisor _supervisor;
    private readonly RuntimeReattacher _reattacher;
    private readonly CancellationToken _lifetimeToken;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly ILogger _logger;

    private readonly BoundedRecoveryPlanner _recoveryPlanner = new();
    private readonly StartupFailureTracker _failureTracker = new();
    private RuntimeLifecycle _lastLifecycle = RuntimeLifecycle.Stopped;

    // Phase 8 Issue 03：timeline / 启动耗时去重守卫（迁移自组合根）。
    private TimeSpan? _lastTimelineElapsed;
    private TimeSpan? _lastStartupElapsedRecorded;

    /// <summary>
    /// 初始化运行时恢复编排器。
    /// </summary>
    /// <param name="host">副作用端口（派发 / 落盘）。</param>
    /// <param name="config">配置读取端口（只读投影）。</param>
    /// <param name="supervisor">监管器（AdoptRunning / 阶段计时来源）。</param>
    /// <param name="reattacher">重接管探测器（具体实现；探测原语经 <see cref="IRuntimeProbe"/> 注入，可 Fake）。</param>
    /// <param name="lifetimeToken">生命周期取消标记（Shutdown 取消后恢复延迟静默放弃）。</param>
    /// <param name="delay">延迟原语（可注入以控制测试时钟；默认 Task.Delay）。</param>
    /// <param name="logger">结构化日志。</param>
    public RuntimeRecoveryController(
        IRuntimeRecoveryHost host,
        IRuntimeRecoveryConfig config,
        IRuntimeSupervisor supervisor,
        RuntimeReattacher reattacher,
        CancellationToken lifetimeToken,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(supervisor);
        ArgumentNullException.ThrowIfNull(reattacher);

        _host = host;
        _config = config;
        _supervisor = supervisor;
        _reattacher = reattacher;
        _lifetimeToken = lifetimeToken;
        _delay = delay ?? ((delaySpan, ct) => Task.Delay(delaySpan, ct));
        _logger = logger ?? Serilog.Core.Logger.None;
    }

    // ===== ① 恢复环（store.States 订阅转发入口）=====

    /// <summary>
    /// Runtime 生命周期变化回调（ADR-0007）：Running 时重置恢复计数；「新进入 Failed」这一沿
    /// 放行一次有界自动重试。只在沿上触发，避免同一 Failed 的重复通知反复计数。
    /// </summary>
    public void OnState(RuntimeLifecycle current)
    {
        RuntimeLifecycle previous = _lastLifecycle;
        _lastLifecycle = current;

        if (current is RuntimeLifecycle.Running)
        {
            _recoveryPlanner.Reset();
            return;
        }

        if (current is not RuntimeLifecycle.Failed || previous is RuntimeLifecycle.Failed)
        {
            return;
        }

        if (!_recoveryPlanner.TryBeginAttempt())
        {
            return;
        }

        _logger.Warning(
            DiagnosticEventNames.RuntimeAutoRecoveryAttempted + " Attempt={Attempt}",
            BoundedRecoveryPlanner.MaxAttemptsPerFailure);

        _ = RetryStartAfterDelayAsync(_lifetimeToken);
    }

    /// <summary>
    /// 延迟后派发一次启动意图（ADR-0007）。短暂延迟避开「刚失败即重试」的紧密循环，
    /// 也留给用户界面把 Failed 渲染出来。取消（应用退出）时静默放弃。
    /// </summary>
    private async Task RetryStartAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _delay(RecoveryRetryDelay, cancellationToken).ConfigureAwait(false);
            await _host.RedispatchStartRuntimeAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 应用退出：放弃本次自动恢复（用户可见的 Failed 面板仍在，可手动重试）。
        }
        catch (Exception exception)
        {
            // fire-and-forget：任何意外都必须自己吞掉，绝不让恢复逻辑拖垮宿主。
            _logger.Warning(
                DiagnosticEventNames.RuntimeAutoRecoveryAttempted + " DispatchFailed {Error}",
                exception.Message);
        }
    }

    // ===== ② 失败计数（由 TrackStartupAsync 委托）=====

    /// <summary>记录一次成功启动：清零连续失败计数。</summary>
    public void RecordSuccess() => _failureTracker.RecordSuccess();

    /// <summary>
    /// 记录一次启动失败：连续 2 次且开关开启 → 经端口进入安全模式（落盘 + SafeModeChanged 派发）。
    /// </summary>
    public async Task RecordFailureAsync(CancellationToken cancellationToken)
    {
        if (!_failureTracker.RecordFailure(_config.AutoSafeModeOnFailure))
        {
            return;
        }

        _logger.Error(
            DiagnosticEventNames.RuntimeAutoSafeModeEntered + " ConsecutiveFailures={Count}",
            _failureTracker.ConsecutiveFailures);
        await _host.EnterSafeModeAsync(cancellationToken).ConfigureAwait(false);
    }

    // ===== ③ 重接管判定（ADR-0005，由 AutoStartRuntimeAsync 委托）=====

    /// <summary>
    /// 按上次记录的 PID + 端口探测存活 Runtime（进程存活 + HTTP 健康检查）。
    /// </summary>
    /// <returns>已接管返回 true；否则（未找到 / 已退化清场）返回 false，走正常启动链。</returns>
    public async Task<bool> TryReattachAsync(CancellationToken cancellationToken)
    {
        if (!_config.KeepRuntimeOnClose
            || _config.LastRuntimePid is not { } pid
            || _config.LastRuntimePort is not { } port)
        {
            return false;
        }

        // Session URL 结论：dsh web 的 token 一次性且禁止落盘，DSH 不支持无 token 重连
        // （Workbench 刷新亦需重取最新 URL）→ 按 ADR-0005 恒退化重启；canRestoreSessionUrl 恒 false。
        ReattachOutcome outcome = await _reattacher
            .TryReattachAsync(_config.Host, pid, port, canRestoreSessionUrl: false, cancellationToken)
            .ConfigureAwait(false);

        if (outcome is ReattachOutcome.Adopted)
        {
            // 当前不可达（canRestoreSessionUrl: false）；DSH 若支持无 token 重连，置 true 启用接管主路径。
            RuntimeSnapshot adopted = _supervisor.AdoptRunning(pid, port, _config.Host);
            _host.DispatchRuntimeStarted(adopted.ProcessId, adopted.Port, adopted.Url ?? string.Empty);
            return true;
        }

        // NotFound / DegradedToRestart：清除陈旧记录，回退正常启动链。
        await _host.ClearReattachRecordAsync(cancellationToken).ConfigureAwait(false);
        return false;
    }

    // ===== ④ 指标判定（由 OnRuntimeSnapshotChanged 转发入口）=====

    /// <summary>
    /// 处理 supervisor 快照：指标监控启停 + 启动耗时去重派发/落盘 + 重接管目标落盘 + Running 对账派发。
    /// <paramref name="storeLifecycle"/> 为 MVI Store 当前生命周期（对账判定来源，避免重复派发）。
    /// </summary>
    public void OnSnapshot(RuntimeSnapshot snapshot, RuntimeLifecycle storeLifecycle)
    {
        UpdateMetricsMonitor(snapshot);
        RecordStartupMetrics(snapshot);

        // Phase 8 Issue 04（ADR-0005）：Running 快照的 PID/端口写入 config，作下次启动重接管探测依据
        // （PID/端口非 Session 数据，允许落盘；Session URL 仍禁止落盘）。
        if (snapshot is { Lifecycle: RuntimeLifecycle.Running, ProcessId: { } pid, Port: { } port }
            && (_config.LastRuntimePid != pid || _config.LastRuntimePort != port))
        {
            _ = _host.PersistReattachTargetAsync(pid, port, CancellationToken.None);
        }

        // 事实对账：supervisor 是真实状态源。插件链路直连 supervisor 启动且不经 MVI 启动链，
        // 可能导致进程已 Running 但 MVI 仍停在 Stopped/Failed 等。此处把状态与事实对齐，补发 RuntimeStarted。
        if (snapshot.Lifecycle is RuntimeLifecycle.Running
            && storeLifecycle is not RuntimeLifecycle.Running)
        {
            _host.DispatchRuntimeStarted(snapshot.ProcessId, snapshot.Port, snapshot.Url ?? string.Empty);
        }
    }

    /// <summary>按生命周期开关进程指标采样（仅 Running 且已知 PID 时采样；其余状态停止）。</summary>
    private void UpdateMetricsMonitor(RuntimeSnapshot snapshot)
    {
        if (snapshot.Lifecycle is RuntimeLifecycle.Running && snapshot.ProcessId is { } processId)
        {
            _host.StartMetricsMonitor(processId);
        }
        else
        {
            _host.StopMetricsMonitor();
        }
    }

    /// <summary>
    /// Runtime Ready 时：timeline 阶段计时投影 Dashboard；启动耗时写 config（旧值先回流为"上次"基准）。
    /// </summary>
    private void RecordStartupMetrics(RuntimeSnapshot snapshot)
    {
        if (snapshot.Lifecycle is RuntimeLifecycle.Starting)
        {
            // 新一次启动开始：重置去重守卫。
            _lastTimelineElapsed = null;
            _lastStartupElapsedRecorded = null;
            return;
        }

        if (snapshot.StartupStage is not RuntimeStartupStage.Ready
            || snapshot.StartupElapsed is not { } elapsed)
        {
            return;
        }

        if (_lastTimelineElapsed != elapsed)
        {
            _lastTimelineElapsed = elapsed;
            _host.DispatchTimeline(_supervisor.LastStartupStageTimings);
        }

        if (_lastStartupElapsedRecorded != elapsed)
        {
            _lastStartupElapsedRecorded = elapsed;
            _host.DispatchStartupElapsed(_config.LastStartupElapsedMs);
            _ = _host.PersistStartupElapsedAsync((long)elapsed.TotalMilliseconds, CancellationToken.None);
        }
    }
}
