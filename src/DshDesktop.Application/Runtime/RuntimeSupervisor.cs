using System.Diagnostics;
using DshDesktop.Application.Diagnostics;
using DshDesktop.Domain.Runtime;
using Serilog;

namespace DshDesktop.Application.Runtime;

/// <summary>
/// 表示 <see cref="IRuntimeSupervisor"/> 的默认实现（§16 修订版，Q5/Q6/Q7 决策）。
/// </summary>
public sealed class RuntimeSupervisor : IRuntimeSupervisor
{
    private static readonly TimeSpan HealthPollInterval = TimeSpan.FromSeconds(5);
    private const int HealthFailureThreshold = 3;

    private readonly IRuntimeOrchestrator _orchestrator;
    private readonly ILogger _logger;
    private readonly HttpClient _httpClient = new();
    private readonly object _sync = new();

    private RuntimeSnapshot _current = new(
        RuntimeLifecycle.Stopped,
        RuntimeHealth.Unknown,
        RuntimeStartupStage.None,
        null, null, null, null);

    private CancellationTokenSource? _healthLoopSource;

    // §46 阶段计时的结构化副本（Phase 8 Issue 03：Dashboard 启动 timeline 数据源）。
    private readonly List<StartupStageTiming> _stageTimings = [];

    /// <summary>
    /// 获取最近一次启动的阶段累计计时（自 Start.Begin 起算，单调不减）。
    /// </summary>
    public IReadOnlyList<StartupStageTiming> LastStartupStageTimings
    {
        get { lock (_sync) { return [.. _stageTimings]; } }
    }

    /// <summary>
    /// 初始化 Runtime 监管器。
    /// </summary>
    /// <param name="orchestrator">进程编排器（保持进程原语）。</param>
    /// <param name="logger">结构化日志（Source=Supervisor）。</param>
    public RuntimeSupervisor(IRuntimeOrchestrator orchestrator, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(orchestrator);
        ArgumentNullException.ThrowIfNull(logger);
        _orchestrator = orchestrator;
        _logger = logger.ForContext("Source", "Supervisor");
        _orchestrator.Exited += OnOrchestratorExited;
    }

    /// <inheritdoc />
    public RuntimeSnapshot Current
    {
        get { lock (_sync) { return _current; } }
    }

    /// <inheritdoc />
    public event EventHandler<RuntimeSnapshot>? SnapshotChanged;

    /// <inheritdoc />
    public event EventHandler<RuntimeExitedEventArgs>? Exited;

    /// <inheritdoc />
    public async Task<RuntimeSnapshot> StartAsync(
        RuntimeLaunchOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        Stopwatch stopwatch = Stopwatch.StartNew();
        _logger.Information("Runtime.Start.Begin");
        lock (_sync)
        {
            _stageTimings.Clear();
        }

        // 同步 IProgress（替代 Progress<T> 的线程池投递）：结构化阶段计时必须与 Ready 标记严格有序，
        // 异步投递会让 Ready 抢跑在 Spawning/WaitingReady 之前（Phase 8 Issue 03 实测竞态）。
        IProgress<RuntimeStartupSignal> progress = new SynchronousStageProgress(signal =>
        {
            // §46：阶段切换带相对耗时（自 Start.Begin 起算）；同步保留结构化副本供 Dashboard 投影。
            _logger.Debug("Runtime.Start.Stage {Stage} ElapsedMs={ElapsedMs}", signal, (long)stopwatch.ElapsedMilliseconds);
            lock (_sync)
            {
                _stageTimings.Add(new StartupStageTiming(signal, stopwatch.Elapsed));
            }

            // HttpProbing 是纯计时标记（Phase 8 Issue 03；F16 起为 Application 信号，不再是 Domain 阶段），
            // 不进入快照状态机（Runtime 页阶段语义不变）。
            if (signal is not RuntimeStartupSignal.HttpProbing)
            {
                Publish(Current with { StartupStage = ToStage(signal) });
            }
        });

        Publish(Current with
        {
            Lifecycle = RuntimeLifecycle.Starting,
            Health = RuntimeHealth.Unknown,
            StartupStage = RuntimeStartupStage.Validating,
            StartupElapsed = null,
        });

        RuntimeLaunchOptions instrumented = options with { Progress = progress };
        RuntimeStartResult result = await _orchestrator
            .StartAsync(instrumented, cancellationToken)
            .ConfigureAwait(false);

        stopwatch.Stop();
        _logger.Information(
            "Runtime.Start.Ready ElapsedMs={ElapsedMs} Port={Port}",
            (long)stopwatch.ElapsedMilliseconds,
            result.Port);
        lock (_sync)
        {
            _stageTimings.Add(new StartupStageTiming(RuntimeStartupSignal.Ready, stopwatch.Elapsed));
        }
        RuntimeSnapshot ready = Current with
        {
            Lifecycle = RuntimeLifecycle.Running,
            Health = RuntimeHealth.Healthy,
            StartupStage = RuntimeStartupStage.Ready,
            StartupElapsed = stopwatch.Elapsed,
            ProcessId = result.ProcessId,
            Port = result.Port,
            Url = result.Url,
        };
        Publish(ready);

        StartHealthLoop();
        return ready;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        StopHealthLoop();

        // Running 与 Starting 中的停止都是编排停止（如应用退出时 Shutdown 无条件 StopAsync），
        // 先切 Stopping，编排器杀进程触发的退出事件才不会被崩溃守卫误报。
        if (Current.Lifecycle is RuntimeLifecycle.Running or RuntimeLifecycle.Starting)
        {
            Publish(Current with { Lifecycle = RuntimeLifecycle.Stopping });
        }

        await _orchestrator.StopAsync(cancellationToken).ConfigureAwait(false);

        Publish(new RuntimeSnapshot(
            RuntimeLifecycle.Stopped,
            RuntimeHealth.Unknown,
            RuntimeStartupStage.None,
            null, null, null, null));
    }

    /// <inheritdoc />
    public async Task<RuntimeSnapshot> RestartAsync(
        RuntimeLaunchOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        _logger.Information("Runtime.Restart.Begin");
        await StopAsync(cancellationToken).ConfigureAwait(false);
        return await StartAsync(options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 重接管存活的 Runtime（ADR-0005，Phase 8 Issue 04）：不拉起新进程，
    /// 直接发布 Running 快照并接管健康监管（5s 轮询）。
    /// </summary>
    /// <remarks>
    /// 接管后无进程句柄退出事件（进程非本实例拉起），崩溃检测退化为健康轮询无响应；
    /// Url 为无 token 基址，仅作健康探测，不可用于工作台导航。
    /// </remarks>
    /// <param name="processId">存活 Runtime 进程 ID。</param>
    /// <param name="port">监听端口。</param>
    /// <param name="host">监听地址。</param>
    /// <returns>接管后的快照。</returns>
    public RuntimeSnapshot AdoptRunning(int processId, int port, string host)
    {
        _logger.Information("Runtime.Reattach.Adopted Pid={ProcessId} Port={Port}", processId, port);
        RuntimeSnapshot adopted = new(
            RuntimeLifecycle.Running,
            RuntimeHealth.Healthy,
            RuntimeStartupStage.Ready,
            null,
            processId,
            port,
            $"http://{host}:{port}/");
        Publish(adopted);
        StartHealthLoop();
        return adopted;
    }

    private void OnOrchestratorExited(object? sender, RuntimeExitedEventArgs args)
    {
        StopHealthLoop();
        _logger.Warning("Runtime.Exit ExitCode={ExitCode}", args.ExitCode);

        // 崩溃结构化事件（Phase 7 Issue 03）：Running/Starting 中收到退出 = 非编排退出。
        // 主动停止（StopAsync）已先把生命周期切到 Stopping/Stopped，不会误报。
        if (Current.Lifecycle is RuntimeLifecycle.Running or RuntimeLifecycle.Starting)
        {
            _logger.Error(DiagnosticEventNames.RuntimeCrashDetected + " ExitCode={ExitCode}", args.ExitCode);
        }

        // 事实层：进程已不存在。崩溃语义（Failed vs Stopped）由 Reducer 依据
        // MVI 侧生命周期上下文判定（Q7），Supervisor 不重复决策。
        Publish(new RuntimeSnapshot(
            RuntimeLifecycle.Stopped,
            RuntimeHealth.Unknown,
            RuntimeStartupStage.None,
            Current.StartupElapsed,
            null, null, null));

        Exited?.Invoke(this, args);
    }

    private void StartHealthLoop()
    {
        StopHealthLoop();
        string? url = Current.Url;
        if (url is null)
        {
            return;
        }

        _healthLoopSource = new CancellationTokenSource();
        _ = RunHealthLoopAsync(url, _healthLoopSource.Token);
    }

    private void StopHealthLoop()
    {
        _healthLoopSource?.Cancel();
        _healthLoopSource?.Dispose();
        _healthLoopSource = null;
    }

    private async Task RunHealthLoopAsync(string url, CancellationToken cancellationToken)
    {
        int consecutiveFailures = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(HealthPollInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            bool alive;
            try
            {
                using HttpResponseMessage response = await _httpClient
                    .GetAsync(url, cancellationToken)
                    .ConfigureAwait(false);
                alive = (int)response.StatusCode < 500;
            }
            catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
            {
                alive = false;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            consecutiveFailures = alive ? 0 : consecutiveFailures + 1;
            RuntimeHealth health = consecutiveFailures >= HealthFailureThreshold
                ? RuntimeHealth.Unresponsive
                : RuntimeHealth.Healthy;

            if (health != Current.Health && Current.Lifecycle is RuntimeLifecycle.Running)
            {
                _logger.Warning("Runtime.Health.Changed Health={Health}", health);
                Publish(Current with { Health = health });
            }
        }
    }

    private void Publish(RuntimeSnapshot snapshot)
    {
        lock (_sync)
        {
            _current = snapshot;
        }

        SnapshotChanged?.Invoke(this, snapshot);
    }

    /// <summary>
    /// 进度信号 → Domain 状态机阶段（HttpProbing 无对应阶段，调用方已先排除）。
    /// </summary>
    private static RuntimeStartupStage ToStage(RuntimeStartupSignal signal)
    {
        return signal switch
        {
            RuntimeStartupSignal.Validating => RuntimeStartupStage.Validating,
            RuntimeStartupSignal.Spawning => RuntimeStartupStage.Spawning,
            RuntimeStartupSignal.WaitingReady => RuntimeStartupStage.WaitingReady,
            _ => RuntimeStartupStage.Ready,
        };
    }

    /// <summary>
    /// 表示同步回传的阶段进度（保证计时记录与 Publish 按 Report 顺序立即执行）。
    /// </summary>
    private sealed class SynchronousStageProgress(Action<RuntimeStartupSignal> onReport)
        : IProgress<RuntimeStartupSignal>
    {
        /// <inheritdoc />
        public void Report(RuntimeStartupSignal value)
        {
            onReport(value);
        }
    }
}
