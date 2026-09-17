using DshDesktop.Domain.Updates;
using MiKiNuo.Mvi.Application.MVI.Effect;
using MiKiNuo.Mvi.Application.MVI.Mediator;
using MiKiNuo.Mvi.Domain.MVI.Effect;

namespace DshDesktop.Presentation.Avalonia.Features.Updates;

/// <summary>
/// 表示 Updates 副作用分发器（§10 桥梁；编排经 Mediator 路由到组合根）。
/// </summary>
public sealed partial class UpdatesEffectDispatcher
    : MviEffectDispatcherBase<UpdatesIntent, UpdatesEffect>
{
    private readonly IMviMediator _mediator;

    /// <summary>
    /// 初始化 Updates 副作用分发器。
    /// </summary>
    /// <param name="mediator">跨层协调中介者。</param>
    public UpdatesEffectDispatcher(IMviMediator mediator)
    {
        ArgumentNullException.ThrowIfNull(mediator);
        _mediator = mediator;
    }

    /// <summary>
    /// 处理检查更新副作用。
    /// </summary>
    [MviEffect(typeof(UpdatesEffect.CheckUpdates))]
    private async ValueTask HandleCheckUpdates(
        UpdatesEffect.CheckUpdates effect,
        CancellationToken cancellationToken)
    {
        try
        {
            CheckUpdatesResponse result = await _mediator
                .SendAsync(new CheckUpdatesRequest(), cancellationToken)
                .ConfigureAwait(false);
            await DispatchIntentAsync(new UpdatesIntent.CheckUpdatesCompleted(result), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await DispatchIntentAsync(new UpdatesIntent.UpdatesOperationFailed(exception.Message), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 处理安装 DSH Runtime 副作用。
    /// </summary>
    [MviEffect(typeof(UpdatesEffect.InstallDshRuntime))]
    private async ValueTask HandleInstallDshRuntime(
        UpdatesEffect.InstallDshRuntime effect,
        CancellationToken cancellationToken)
    {
        try
        {
            System.Collections.Generic.IReadOnlyList<DshRuntimeInfo> runtimes =
                await _mediator
                    .SendAsync(new InstallDshRuntimeRequest(effect.Version), cancellationToken)
                    .ConfigureAwait(false);
            await DispatchIntentAsync(new UpdatesIntent.RuntimeListChanged(runtimes), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await DispatchIntentAsync(new UpdatesIntent.UpdatesOperationFailed(exception.Message), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 处理激活 Runtime 副作用。
    /// </summary>
    [MviEffect(typeof(UpdatesEffect.ActivateDshRuntime))]
    private async ValueTask HandleActivateDshRuntime(
        UpdatesEffect.ActivateDshRuntime effect,
        CancellationToken cancellationToken)
    {
        try
        {
            System.Collections.Generic.IReadOnlyList<DshRuntimeInfo> runtimes = await _mediator
                .SendAsync(new ActivateDshRuntimeRequest(effect.Version), cancellationToken)
                .ConfigureAwait(false);
            await DispatchIntentAsync(new UpdatesIntent.RuntimeListChanged(runtimes), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await DispatchIntentAsync(new UpdatesIntent.UpdatesOperationFailed(exception.Message), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 处理更新插件副作用。成功后的清单刷新不在此处发起——事务 Completed 由组合根统一广播
    /// 两侧刷新（见 DshCompositionRoot），否则同一入口会连发两次网络检查。
    /// </summary>
    [MviEffect(typeof(UpdatesEffect.UpdatePlugin))]
    private async ValueTask HandleUpdatePlugin(
        UpdatesEffect.UpdatePlugin effect,
        CancellationToken cancellationToken)
    {
        try
        {
            _ = await _mediator
                .SendAsync(new UpdatePluginRequest(effect.Name), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await DispatchIntentAsync(new UpdatesIntent.UpdatesOperationFailed(exception.Message), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 处理下载并应用 Desktop 更新副作用（应用后进程重启，无完成回流）。
    /// </summary>
    [MviEffect(typeof(UpdatesEffect.DownloadAndApplyDesktopUpdate))]
    private async ValueTask HandleDownloadAndApplyDesktopUpdate(
        UpdatesEffect.DownloadAndApplyDesktopUpdate effect,
        CancellationToken cancellationToken)
    {
        try
        {
            _ = await _mediator
                .SendAsync(new DownloadAndApplyDesktopUpdateRequest(), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await DispatchIntentAsync(new UpdatesIntent.UpdatesOperationFailed(exception.Message), cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
