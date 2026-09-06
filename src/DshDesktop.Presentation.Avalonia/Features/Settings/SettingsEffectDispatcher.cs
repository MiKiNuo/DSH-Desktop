using DshDesktop.Presentation.Avalonia.Features.Runtime;
using MiKiNuo.Mvi.Application.MVI.Effect;
using MiKiNuo.Mvi.Application.MVI.Mediator;
using MiKiNuo.Mvi.Domain.MVI.Effect;
using MiKiNuo.Mvi.Domain.MVI.Mediator;

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
        await PersistPolicyAsync(new SetSafeModeRequest(effect.Enabled), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 处理持久化 Windows 通知开关副作用（State 已乐观更新，失败只回报错误）。
    /// </summary>
    [MviEffect(typeof(SettingsEffect.SaveNotificationsEnabled))]
    private async ValueTask HandleSaveNotificationsEnabled(
        SettingsEffect.SaveNotificationsEnabled effect,
        CancellationToken cancellationToken)
    {
        await PersistPolicyAsync(new SetNotificationsEnabledRequest(effect.Enabled), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 处理持久化 DSH 更新通道副作用（State 已乐观更新，失败只回报错误）。
    /// </summary>
    [MviEffect(typeof(SettingsEffect.SaveChannel))]
    private async ValueTask HandleSaveChannel(
        SettingsEffect.SaveChannel effect,
        CancellationToken cancellationToken)
    {
        await PersistPolicyAsync(new SetDshChannelRequest(effect.Channel), cancellationToken).ConfigureAwait(false);
    }

    // ===== Phase 8 Issue 05：桌面行为 / 更新策略开关与打开目录（State 已乐观更新，失败只回报错误） =====

    /// <summary>
    /// 处理持久化"关闭窗口最小化到托盘"副作用。
    /// </summary>
    [MviEffect(typeof(SettingsEffect.SaveMinimizeToTrayOnClose))]
    private async ValueTask HandleSaveMinimizeToTrayOnClose(
        SettingsEffect.SaveMinimizeToTrayOnClose effect,
        CancellationToken cancellationToken)
    {
        await PersistPolicyAsync(new SetMinimizeToTrayOnCloseRequest(effect.Enabled), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 处理持久化"开机自动启动"副作用（组合根顺带写/删注册表 Run 键）。
    /// </summary>
    [MviEffect(typeof(SettingsEffect.SaveLaunchOnStartup))]
    private async ValueTask HandleSaveLaunchOnStartup(
        SettingsEffect.SaveLaunchOnStartup effect,
        CancellationToken cancellationToken)
    {
        await PersistPolicyAsync(new SetLaunchOnStartupRequest(effect.Enabled), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 处理持久化"后台检查更新"副作用。
    /// </summary>
    [MviEffect(typeof(SettingsEffect.SaveBackgroundUpdateCheck))]
    private async ValueTask HandleSaveBackgroundUpdateCheck(
        SettingsEffect.SaveBackgroundUpdateCheck effect,
        CancellationToken cancellationToken)
    {
        await PersistPolicyAsync(new SetBackgroundUpdateCheckRequest(effect.Enabled), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 处理持久化"自动下载安装"副作用。
    /// </summary>
    [MviEffect(typeof(SettingsEffect.SaveAutoDownloadUpdates))]
    private async ValueTask HandleSaveAutoDownloadUpdates(
        SettingsEffect.SaveAutoDownloadUpdates effect,
        CancellationToken cancellationToken)
    {
        await PersistPolicyAsync(new SetAutoDownloadUpdatesRequest(effect.Enabled), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 处理打开目录副作用（路由到组合根的 IPathOpener 端口，§4.1）。
    /// </summary>
    [MviEffect(typeof(SettingsEffect.OpenDirectory))]
    private async ValueTask HandleOpenDirectory(
        SettingsEffect.OpenDirectory effect,
        CancellationToken cancellationToken)
    {
        await PersistPolicyAsync(new OpenPathRequest(effect.Path), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// try/catch → SendAsync → 失败回流模板（Phase 8 评审 F11，照 RuntimeEffectDispatcher.PersistPolicyAsync 先例）。
    /// </summary>
    private async ValueTask PersistPolicyAsync(IMviRequest<bool> request, CancellationToken cancellationToken)
    {
        try
        {
            _ = await _mediator.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await DispatchIntentAsync(new SettingsIntent.SettingsOperationFailed(exception.Message), cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
