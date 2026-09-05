using System.Diagnostics;
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

        Progress<RuntimeStartupStage> progress = new(stage =>
        {
            // §46：阶段切换带相对耗时（自 Start.Begin 起算）。
            _logger.Debug("Runtime.Start.Stage {Stage} ElapsedMs={ElapsedMs}", stage, (long)stopwatch.ElapsedMilliseconds);
            Publish(Current with { StartupStage = stage });
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

        if (Current.Lifecycle is RuntimeLifecycle.Running)
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

    private void OnOrchestratorExited(object? sender, RuntimeExitedEventArgs args)
    {
        StopHealthLoop();
        _logger.Warning("Runtime.Exit ExitCode={ExitCode}", args.ExitCode);

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
}
