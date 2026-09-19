using DshDesktop.Application.Diagnostics;
using DshDesktop.Application.Runtime;
using DshDesktop.Domain.Plugins;
using DshDesktop.Domain.Runtime;
using Serilog;

namespace DshDesktop.Application.Plugins;

/// <summary>
/// 表示 <see cref="IPluginOrchestrator"/> 的默认实现（§19 安装事务，Q4-A 落点）。
/// 安装/更新/卸载/启停共用同一套事务管线（<see cref="RunTransactionAsync"/>）。
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
    public async Task<string> InstallAsync(
        string source,
        PluginOperationKind kind,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        string pluginName = await RunTransactionAsync(
            kind,
            pluginName: null,
            displayName: source,
            mutateStage: PluginOperationStage.Installing,
            mutate: async ct =>
            {
                string installed = await pluginManager.InstallAsync(source, ct).ConfigureAwait(false);
                _logger.Information("Plugin.Install.Installed {PluginName}", installed);
                return installed;
            },
            validate: ValidateInstalledAsync,
            cancellationToken).ConfigureAwait(false);
        _logger.Information("Plugin.Install.Success {PluginName}", pluginName);
        return pluginName;
    }

    /// <inheritdoc />
    public async Task UninstallAsync(string name, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        _ = await RunTransactionAsync(
            PluginOperationKind.Uninstall,
            pluginName: name,
            displayName: name,
            mutateStage: PluginOperationStage.Uninstalling,
            mutate: async ct =>
            {
                await pluginManager.UninstallAsync(name, ct).ConfigureAwait(false);
                return name;
            },
            validate: ValidateUninstalledAsync,
            cancellationToken).ConfigureAwait(false);
        _logger.Information("Plugin.Uninstall.Success {PluginName}", name);
    }

    /// <inheritdoc />
    public async Task SetEnabledAsync(string name, bool enabled, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        PluginOperationKind kind = enabled ? PluginOperationKind.Enable : PluginOperationKind.Disable;
        _ = await RunTransactionAsync(
            kind,
            pluginName: name,
            displayName: name,
            mutateStage: PluginOperationStage.Applying,
            mutate: async ct =>
            {
                await pluginManager.SetEnabledAsync(name, enabled, ct).ConfigureAwait(false);
                return name;
            },
            validate: (n, ct) => ValidateEnabledStateAsync(n, enabled, ct),
            cancellationToken).ConfigureAwait(false);
        _logger.Information("Plugin.SetEnabled.Success {PluginName} {Enabled}", name, enabled);
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
    /// 事务管线共享段：快照 → 停 Runtime → 变更 → 校验 → 启动 → 健康检查 → 提交；
    /// 任何失败 → 回滚（恢复快照 + 尽力重启 Runtime）→ Failed → 抛原异常。
    /// </summary>
    /// <param name="kind">操作种类（贯穿各阶段，供下游反馈文案区分）。</param>
    /// <param name="pluginName">目标插件名；安装前未知传 null，安装完成后由 <paramref name="mutate"/> 解析。</param>
    /// <param name="displayName">失败日志/回滚发布用的兜底显示名（安装场景为 source）。</param>
    /// <param name="mutateStage">变更阶段（Installing / Uninstalling / Applying）。</param>
    /// <param name="mutate">实际变更，返回解析后的插件名。</param>
    /// <param name="validate">变更后校验，失败抛异常即触发回滚。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>解析后的插件名。</returns>
    private async Task<string> RunTransactionAsync(
        PluginOperationKind kind,
        string? pluginName,
        string displayName,
        PluginOperationStage mutateStage,
        Func<CancellationToken, Task<string>> mutate,
        Func<string, CancellationToken, Task> validate,
        CancellationToken cancellationToken)
    {
        Publish(PluginOperationStage.Preparing, pluginName, null, kind);
        string? snapshotId = null;
        string? resolvedName = pluginName;

        try
        {
            Publish(PluginOperationStage.CreatingSnapshot, resolvedName, null, kind);
            snapshotId = await snapshotter.CreateSnapshotAsync(cancellationToken).ConfigureAwait(false);

            Publish(PluginOperationStage.StoppingRuntime, resolvedName, null, kind);
            await supervisor.StopAsync(cancellationToken).ConfigureAwait(false);

            Publish(mutateStage, resolvedName, null, kind);
            resolvedName = await mutate(cancellationToken).ConfigureAwait(false);

            Publish(PluginOperationStage.Validating, resolvedName, null, kind);
            await validate(resolvedName, cancellationToken).ConfigureAwait(false);

            Publish(PluginOperationStage.StartingRuntime, resolvedName, null, kind);
            await StartRuntimeWithTransientRetryAsync(cancellationToken).ConfigureAwait(false);

            Publish(PluginOperationStage.HealthChecking, resolvedName, null, kind);
            await ConfirmHealthyAsync(cancellationToken).ConfigureAwait(false);

            Publish(PluginOperationStage.Completed, resolvedName, null, kind);
            return resolvedName;
        }
        catch (Exception exception)
        {
            _logger.Warning(
                DiagnosticEventNames.PluginInstallRollback + " {PluginName} {Error}",
                resolvedName ?? displayName, exception.Message);
            await RollbackAsync(snapshotId, resolvedName ?? displayName, exception.Message, kind, cancellationToken)
                .ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// 文件级一致性校验（Q3-A）：列表解析必须能找到已安装、启用且磁盘可解析的插件。
    /// 仅断言 manifest 的 Enabled 不够——声明-but-未物化（悬空 junction / 中断安装残留）的插件
    /// 会让 DSH 启动期 resolveBundleDir 抛错（2026-09-14 实机），必须以磁盘可解析为准。
    /// </summary>
    private async Task ValidateInstalledAsync(string pluginName, CancellationToken cancellationToken)
    {
        IReadOnlyList<PluginInfo> plugins = await pluginManager
            .ListPluginsAsync(cancellationToken).ConfigureAwait(false);
        if (!plugins.Any(p => p.Name == pluginName && p.Enabled && p.IsResolvable))
        {
            throw new InvalidOperationException(
                $"安装后校验失败：{pluginName} 未出现在启用且可解析的插件清单中。");
        }
    }

    /// <summary>卸载后校验：清单中不得再出现目标插件（声明残留同样视为失败）。</summary>
    private async Task ValidateUninstalledAsync(string pluginName, CancellationToken cancellationToken)
    {
        IReadOnlyList<PluginInfo> plugins = await pluginManager
            .ListPluginsAsync(cancellationToken).ConfigureAwait(false);
        if (plugins.Any(p => p.Name == pluginName))
        {
            throw new InvalidOperationException(
                $"卸载后校验失败：{pluginName} 仍出现在插件清单中。");
        }
    }

    /// <summary>启停后校验：清单中目标插件必须处于目标启用状态。</summary>
    private async Task ValidateEnabledStateAsync(
        string pluginName,
        bool enabled,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<PluginInfo> plugins = await pluginManager
            .ListPluginsAsync(cancellationToken).ConfigureAwait(false);
        if (!plugins.Any(p => p.Name == pluginName && p.Enabled == enabled))
        {
            throw new InvalidOperationException(
                $"启用状态校验失败：{pluginName} 未处于目标状态（Enabled={enabled}）。");
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
        PluginOperationKind kind,
        CancellationToken cancellationToken)
    {
        Publish(PluginOperationStage.RollingBack, pluginName, null, kind);

        // 快照恢复失败也要继续「尽力重启 Runtime」——早退会让被事务杀掉的 Runtime 无人拉起。
        // restoreFailure 保留真实原因，并入下方唯一的 Failed 发布（保证 Failed 只发一次）。
        string? restoreFailure = null;
        if (snapshotId is not null)
        {
            try
            {
                await snapshotter.RestoreAsync(snapshotId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception restoreException)
            {
                _logger.Error(
                    DiagnosticEventNames.PluginRollbackPrefix + "RestoreFailed {Error}",
                    restoreException.Message);
                restoreFailure = $"{error}（回滚恢复也失败：{restoreException.Message}）";
            }
        }

        // 尽力重启之前的 Runtime（§19：Restart Previous Runtime）。
        try
        {
            await supervisor.StartAsync(launchOptionsFactory(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception restartException)
        {
            _logger.Warning(
                DiagnosticEventNames.PluginRollbackPrefix + "RestartFailed {Error}",
                restartException.Message);
        }

        Publish(PluginOperationStage.Failed, pluginName, restoreFailure ?? error, kind);
    }

    private void Publish(
        PluginOperationStage stage,
        string? pluginName,
        string? error,
        PluginOperationKind kind)
    {
        OperationChanged?.Invoke(this, new PluginOperation(stage, pluginName, error, kind));
    }
}
