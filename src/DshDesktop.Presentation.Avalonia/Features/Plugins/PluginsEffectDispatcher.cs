using MiKiNuo.Mvi.Application.MVI.Effect;
using MiKiNuo.Mvi.Application.MVI.Mediator;
using MiKiNuo.Mvi.Domain.MVI.Effect;

namespace DshDesktop.Presentation.Avalonia.Features.Plugins;

/// <summary>
/// 表示 Plugins 副作用分发器（§10 桥梁；编排经 Mediator 路由到组合根）。
/// </summary>
public sealed partial class PluginsEffectDispatcher
    : MviEffectDispatcherBase<PluginsIntent, PluginsEffect>
{
    private readonly IMviMediator _mediator;

    /// <summary>
    /// 初始化 Plugins 副作用分发器。
    /// </summary>
    /// <param name="mediator">跨层协调中介者。</param>
    public PluginsEffectDispatcher(IMviMediator mediator)
    {
        ArgumentNullException.ThrowIfNull(mediator);
        _mediator = mediator;
    }

    /// <summary>
    /// 处理加载插件清单副作用。
    /// </summary>
    [MviEffect(typeof(PluginsEffect.LoadPlugins))]
    private async ValueTask HandleLoadPlugins(
        PluginsEffect.LoadPlugins effect,
        CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<DshDesktop.Domain.Plugins.PluginInfo> plugins = await _mediator
                .SendAsync(new GetPluginListRequest(), cancellationToken)
                .ConfigureAwait(false);
            await DispatchIntentAsync(new PluginsIntent.PluginsLoaded(plugins), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await DispatchIntentAsync(new PluginsIntent.PluginOperationFailed(exception.Message), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 处理设置插件启用状态副作用。
    /// </summary>
    [MviEffect(typeof(PluginsEffect.SetPluginEnabled))]
    private async ValueTask HandleSetPluginEnabled(
        PluginsEffect.SetPluginEnabled effect,
        CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<DshDesktop.Domain.Plugins.PluginInfo> plugins = await _mediator
                .SendAsync(new SetPluginEnabledRequest(effect.Name, effect.Enabled), cancellationToken)
                .ConfigureAwait(false);
            await DispatchIntentAsync(new PluginsIntent.PluginsLoaded(plugins), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await DispatchIntentAsync(new PluginsIntent.PluginOperationFailed(exception.Message), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 处理卸载插件副作用。
    /// </summary>
    [MviEffect(typeof(PluginsEffect.UninstallPlugin))]
    private async ValueTask HandleUninstallPlugin(
        PluginsEffect.UninstallPlugin effect,
        CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<DshDesktop.Domain.Plugins.PluginInfo> plugins = await _mediator
                .SendAsync(new UninstallPluginRequest(effect.Name), cancellationToken)
                .ConfigureAwait(false);
            await DispatchIntentAsync(new PluginsIntent.PluginsLoaded(plugins), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await DispatchIntentAsync(new PluginsIntent.PluginOperationFailed(exception.Message), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 处理安装插件副作用（§19 事务，阶段进度由组合根订阅 OperationChanged 回流）。
    /// </summary>
    [MviEffect(typeof(PluginsEffect.InstallPlugin))]
    private async ValueTask HandleInstallPlugin(
        PluginsEffect.InstallPlugin effect,
        CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<DshDesktop.Domain.Plugins.PluginInfo> plugins = await _mediator
                .SendAsync(new InstallPluginRequest(effect.Source), cancellationToken)
                .ConfigureAwait(false);
            await DispatchIntentAsync(new PluginsIntent.PluginsLoaded(plugins), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await DispatchIntentAsync(new PluginsIntent.PluginOperationFailed(exception.Message), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 处理禁用全部第三方插件副作用。
    /// </summary>
    [MviEffect(typeof(PluginsEffect.DisableAllThirdParty))]
    private async ValueTask HandleDisableAllThirdParty(
        PluginsEffect.DisableAllThirdParty effect,
        CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<DshDesktop.Domain.Plugins.PluginInfo> plugins = await _mediator
                .SendAsync(new DisableAllThirdPartyRequest(), cancellationToken)
                .ConfigureAwait(false);
            await DispatchIntentAsync(new PluginsIntent.PluginsLoaded(plugins), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await DispatchIntentAsync(new PluginsIntent.PluginOperationFailed(exception.Message), cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
