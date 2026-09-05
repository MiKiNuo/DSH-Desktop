using DshDesktop.Presentation.Avalonia.Features.Runtime;
using MiKiNuo.Mvi.Application.MVI.Effect;
using MiKiNuo.Mvi.Application.MVI.Mediator;
using MiKiNuo.Mvi.Domain.MVI.Effect;

namespace DshDesktop.Presentation.Avalonia.Features.Settings;

/// <summary>
/// 表示 Settings 副作用分发器（§10 桥梁；持久化经 Mediator 路由到组合根）。
/// </summary>
public sealed partial class SettingsEffectDispatcher
    : MviEffectDispatcherBase<SettingsIntent, SettingsEffect>
{
    private readonly IMviMediator _mediator;

    /// <summary>
    /// 初始化 Settings 副作用分发器。
    /// </summary>
    /// <param name="mediator">跨层协调中介者。</param>
    public SettingsEffectDispatcher(IMviMediator mediator)
    {
        ArgumentNullException.ThrowIfNull(mediator);
        _mediator = mediator;
    }

    /// <summary>
    /// 处理加载设置信息副作用。
    /// </summary>
    [MviEffect(typeof(SettingsEffect.LoadSettings))]
    private async ValueTask HandleLoadSettings(
        SettingsEffect.LoadSettings effect,
        CancellationToken cancellationToken)
    {
        try
        {
            SettingsInfo info = await _mediator
                .SendAsync(new GetSettingsInfoRequest(), cancellationToken)
                .ConfigureAwait(false);
            await DispatchIntentAsync(new SettingsIntent.SettingsLoaded(info), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await DispatchIntentAsync(new SettingsIntent.SettingsOperationFailed(exception.Message), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 处理持久化安全模式副作用（State 已乐观更新，失败只回报错误）。
    /// </summary>
    [MviEffect(typeof(SettingsEffect.SaveSafeMode))]
    private async ValueTask HandleSaveSafeMode(
        SettingsEffect.SaveSafeMode effect,
        CancellationToken cancellationToken)
    {
        try
        {
            _ = await _mediator
                .SendAsync(new SetSafeModeRequest(effect.Enabled), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await DispatchIntentAsync(new SettingsIntent.SettingsOperationFailed(exception.Message), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 处理持久化 DSH 更新通道副作用（State 已乐观更新，失败只回报错误）。
    /// </summary>
    [MviEffect(typeof(SettingsEffect.SaveChannel))]
    private async ValueTask HandleSaveChannel(
        SettingsEffect.SaveChannel effect,
        CancellationToken cancellationToken)
    {
        try
        {
            _ = await _mediator
                .SendAsync(new SetDshChannelRequest(effect.Channel), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await DispatchIntentAsync(new SettingsIntent.SettingsOperationFailed(exception.Message), cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
