using DshDesktop.Application.Runtime;
using DshDesktop.Domain.Plugins;
using DshDesktop.Domain.Runtime;
using Serilog;

namespace DshDesktop.Application.Plugins;

/// <summary>
/// 表示 <see cref="IPluginOrchestrator"/> 的默认实现（§19 安装事务，Q4-A 落点）。
/// </summary>
public sealed class PluginOrchestrator(
    IPluginManager pluginManager,
    IProfileSnapshotter snapshotter,
    IRuntimeSupervisor supervisor,
    Func<RuntimeLaunchOptions> launchOptionsFactory,
    ILogger logger) : IPluginOrchestrator
{
    private static readonly TimeSpan HealthConfirmTimeout = TimeSpan.FromSeconds(20);

    private readonly ILogger _logger = logger.ForContext("Source", "Supervisor");

    /// <inheritdoc />
    public event EventHandler<PluginOperation>? OperationChanged;

    /// <inheritdoc />
    public async Task<string> InstallAsync(string source, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        Publish(PluginOperationStage.Preparing, null, null);
        string? snapshotId = null;
        string? pluginName = null;

        try
        {
            Publish(PluginOperationStage.CreatingSnapshot, null, null);
            snapshotId = await snapshotter.CreateSnapshotAsync(cancellationToken).ConfigureAwait(false);

            Publish(PluginOperationStage.StoppingRuntime, null, null);
            await supervisor.StopAsync(cancellationToken).ConfigureAwait(false);

            Publish(PluginOperationStage.Installing, null, null);
            pluginName = await pluginManager.InstallAsync(source, cancellationToken).ConfigureAwait(false);
            _logger.Information("Plugin.Install.Installed {PluginName}", pluginName);

            Publish(PluginOperationStage.Validating, pluginName, null);
            // 文件级一致性校验（Q3-A）：列表解析必须能找到已安装且启用的插件。
            IReadOnlyList<PluginInfo> plugins = await pluginManager
                .ListPluginsAsync(cancellationToken).ConfigureAwait(false);
            if (!plugins.Any(p => p.Name == pluginName && p.Enabled))
            {
                throw new InvalidOperationException(
                    $"安装后校验失败：{pluginName} 未出现在启用插件清单中。");
            }

            Publish(PluginOperationStage.StartingRuntime, pluginName, null);
            await StartRuntimeWithTransientRetryAsync(cancellationToken).ConfigureAwait(false);

            Publish(PluginOperationStage.HealthChecking, pluginName, null);
            await ConfirmHealthyAsync(cancellationToken).ConfigureAwait(false);

            Publish(PluginOperationStage.Completed, pluginName, null);
            _logger.Information("Plugin.Install.Success {PluginName}", pluginName);
            return pluginName;
        }
        catch (Exception exception)
        {
            _logger.Warning("Plugin.Install.Rollback {PluginName} {Error}", pluginName ?? source, exception.Message);
            await RollbackAsync(snapshotId, pluginName ?? source, exception.Message, cancellationToken)
                .ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task DisableAllThirdPartyAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<PluginInfo> plugins = await pluginManager
            .ListPluginsAsync(cancellationToken).ConfigureAwait(false);
        foreach (PluginInfo plugin in plugins.Where(p => p is { IsCore: false, Enabled: true }))
        {
            await pluginManager.SetEnabledAsync(plugin.Name, false, cancellationToken).ConfigureAwait(false);
            _logger.Information("Plugin.DisableAll.Disabled {PluginName}", plugin.Name);
        }
    }

    /// <summary>
    /// 启动 Runtime，对"pnpm 刚写完 node_modules 后 DSH 建 Junction 遭扫描器瞬时占用"
    /// （EPERM symlink，harness 的 pnpm-runner.mjs 注释记录的同类问题）延迟重试一次。
    /// </summary>
    private async Task StartRuntimeWithTransientRetryAsync(CancellationToken cancellationToken)
    {
        try
        {
            await supervisor.StartAsync(launchOptionsFactory(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception.Message.Contains("EPERM", StringComparison.Ordinal)
            && exception.Message.Contains("symlink", StringComparison.Ordinal))
        {
            _logger.Warning("Runtime.Start.TransientSymlinkRetry");
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            await supervisor.StartAsync(launchOptionsFactory(), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ConfirmHealthyAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(HealthConfirmTimeout);

        while (true)
        {
            RuntimeSnapshot snapshot = supervisor.Current;
            if (snapshot.Lifecycle is not RuntimeLifecycle.Running)
            {
                throw new InvalidOperationException("Runtime 未能保持运行状态。");
            }

            if (snapshot.Health is RuntimeHealth.Healthy)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), timeoutSource.Token).ConfigureAwait(false);
        }
    }

    private async Task RollbackAsync(
        string? snapshotId,
        string pluginName,
        string error,
        CancellationToken cancellationToken)
    {
        Publish(PluginOperationStage.RollingBack, pluginName, null);

        if (snapshotId is not null)
        {
            try
            {
                await snapshotter.RestoreAsync(snapshotId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception restoreException)
            {
                _logger.Error("Plugin.Rollback.RestoreFailed {Error}", restoreException.Message);
                Publish(PluginOperationStage.Failed, pluginName,
                    $"{error}（回滚恢复也失败：{restoreException.Message}）");
                return;
            }
        }

        // 尽力重启之前的 Runtime（§19：Restart Previous Runtime）。
        try
        {
            await supervisor.StartAsync(launchOptionsFactory(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception restartException)
        {
            _logger.Warning("Plugin.Rollback.RestartFailed {Error}", restartException.Message);
        }

        Publish(PluginOperationStage.Failed, pluginName, error);
    }

    private void Publish(PluginOperationStage stage, string? pluginName, string? error)
    {
        OperationChanged?.Invoke(this, new PluginOperation(stage, pluginName, error));
    }
}
