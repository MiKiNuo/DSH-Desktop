using MiKiNuo.Mvi.Application.MVI.Reducer;
using MiKiNuo.Mvi.Domain.DI;
using MiKiNuo.Mvi.Domain.MVI.Reducer;

namespace DshDesktop.Presentation.Avalonia.Features.Settings;

/// <summary>
/// 表示 Settings 规约器。纯函数，禁止 IO（§9）。
/// </summary>
[MviFeature]
public sealed partial class SettingsReducer
    : MviReducerBase<SettingsState, SettingsIntent, SettingsEffect>
{
    /// <summary>
    /// 处理加载设置意图。
    /// </summary>
    [MviReduce(typeof(SettingsIntent.LoadSettings))]
    private MviReduceResult<SettingsState, SettingsEffect> HandleLoadSettings(
        SettingsState state,
        SettingsIntent.LoadSettings intent)
    {
        return WithEffect(
            state with { PendingOperation = "加载设置…", LastError = null },
            new SettingsEffect.LoadSettings());
    }

    /// <summary>
    /// 处理切换安全模式意图（无载荷翻转——View 不推导目标状态，消 ToggleSwitch 双击分歧窗口；乐观更新）。
    /// </summary>
    [MviReduce(typeof(SettingsIntent.ToggleSafeMode))]
    private MviReduceResult<SettingsState, SettingsEffect> HandleToggleSafeMode(
        SettingsState state,
        SettingsIntent.ToggleSafeMode intent)
    {
        bool target = !state.SafeMode;
        return WithEffect(
            state with { SafeMode = target, LastError = null },
            new SettingsEffect.SaveSafeMode(target));
    }

    /// <summary>
    /// 处理切换 Windows 通知意图（无载荷翻转，同 ToggleSafeMode 先例；乐观更新）。
    /// </summary>
    [MviReduce(typeof(SettingsIntent.ToggleNotifications))]
    private MviReduceResult<SettingsState, SettingsEffect> HandleToggleNotifications(
        SettingsState state,
        SettingsIntent.ToggleNotifications intent)
    {
        bool target = !state.NotificationsEnabled;
        return WithEffect(
            state with { NotificationsEnabled = target, LastError = null },
            new SettingsEffect.SaveNotificationsEnabled(target));
    }

    /// <summary>
    /// 处理修改 DSH 更新通道意图（乐观更新）。
    /// </summary>
    [MviReduce(typeof(SettingsIntent.ChangeChannel))]
    private MviReduceResult<SettingsState, SettingsEffect> HandleChangeChannel(
        SettingsState state,
        SettingsIntent.ChangeChannel intent)
    {
        if (state.Channel == intent.Channel)
        {
            return Unchanged(state);
        }

        return WithEffect(
            state with { Channel = intent.Channel, LastError = null },
            new SettingsEffect.SaveChannel(intent.Channel));
    }

    /// <summary>
    /// 处理设置信息已加载回流意图。
    /// </summary>
    [MviReduce(typeof(SettingsIntent.SettingsLoaded))]
    private MviReduceResult<SettingsState, SettingsEffect> HandleSettingsLoaded(
        SettingsState state,
        SettingsIntent.SettingsLoaded intent)
    {
        return Unchanged(state with
        {
            SafeMode = intent.Info.SafeMode,
            NotificationsEnabled = intent.Info.NotificationsEnabled,
            Channel = intent.Info.Channel,
            NodePath = intent.Info.NodePath,
            DshHome = intent.Info.DshHome,
            DataDirectory = intent.Info.DataDirectory,
            PluginsDirectory = intent.Info.PluginsDirectory,
            DshRuntimeDirectory = intent.Info.DshRuntimeDirectory,
            MinimizeToTrayOnClose = intent.Info.MinimizeToTrayOnClose,
            LaunchOnStartup = intent.Info.LaunchOnStartup,
            BackgroundUpdateCheck = intent.Info.BackgroundUpdateCheck,
            AutoDownloadUpdates = intent.Info.AutoDownloadUpdates,
            PendingOperation = null,
            LastError = null,
        });
    }

    /// <summary>
    /// 处理设置操作失败回流意图。
    /// </summary>
    [MviReduce(typeof(SettingsIntent.SettingsOperationFailed))]
    private MviReduceResult<SettingsState, SettingsEffect> HandleSettingsOperationFailed(
        SettingsState state,
        SettingsIntent.SettingsOperationFailed intent)
    {
        return Unchanged(state with { PendingOperation = null, LastError = intent.Error });
    }

    // ===== Phase 8 Issue 05：桌面行为 / 更新策略开关与打开目录（乐观更新，同 ToggleSafeMode 先例） =====

    /// <summary>
    /// 处理切换"关闭窗口最小化到托盘"意图（无载荷翻转）。
    /// </summary>
    [MviReduce(typeof(SettingsIntent.ToggleMinimizeToTrayOnClose))]
    private MviReduceResult<SettingsState, SettingsEffect> HandleToggleMinimizeToTrayOnClose(
        SettingsState state,
        SettingsIntent.ToggleMinimizeToTrayOnClose intent)
    {
        bool target = !state.MinimizeToTrayOnClose;
        return WithEffect(
            state with { MinimizeToTrayOnClose = target, LastError = null },
            new SettingsEffect.SaveMinimizeToTrayOnClose(target));
    }

    /// <summary>
    /// 处理切换"开机自动启动"意图（无载荷翻转）。
    /// </summary>
    [MviReduce(typeof(SettingsIntent.ToggleLaunchOnStartup))]
    private MviReduceResult<SettingsState, SettingsEffect> HandleToggleLaunchOnStartup(
        SettingsState state,
        SettingsIntent.ToggleLaunchOnStartup intent)
    {
        bool target = !state.LaunchOnStartup;
        return WithEffect(
            state with { LaunchOnStartup = target, LastError = null },
            new SettingsEffect.SaveLaunchOnStartup(target));
    }

    /// <summary>
    /// 处理切换"后台检查更新"意图（无载荷翻转）。
    /// </summary>
    [MviReduce(typeof(SettingsIntent.ToggleBackgroundUpdateCheck))]
    private MviReduceResult<SettingsState, SettingsEffect> HandleToggleBackgroundUpdateCheck(
        SettingsState state,
        SettingsIntent.ToggleBackgroundUpdateCheck intent)
    {
        bool target = !state.BackgroundUpdateCheck;
        return WithEffect(
            state with { BackgroundUpdateCheck = target, LastError = null },
            new SettingsEffect.SaveBackgroundUpdateCheck(target));
    }

    /// <summary>
    /// 处理切换"自动下载安装"意图（无载荷翻转）。
    /// </summary>
    [MviReduce(typeof(SettingsIntent.ToggleAutoDownloadUpdates))]
    private MviReduceResult<SettingsState, SettingsEffect> HandleToggleAutoDownloadUpdates(
        SettingsState state,
        SettingsIntent.ToggleAutoDownloadUpdates intent)
    {
        bool target = !state.AutoDownloadUpdates;
        return WithEffect(
            state with { AutoDownloadUpdates = target, LastError = null },
            new SettingsEffect.SaveAutoDownloadUpdates(target));
    }

    /// <summary>
    /// 处理打开目录意图（路径为空 = 设置未加载，不产副作用）。
    /// </summary>
    [MviReduce(typeof(SettingsIntent.OpenDirectory))]
    private MviReduceResult<SettingsState, SettingsEffect> HandleOpenDirectory(
        SettingsState state,
        SettingsIntent.OpenDirectory intent)
    {
        if (string.IsNullOrWhiteSpace(intent.Path))
        {
            return Unchanged(state);
        }

        return WithEffect(state, new SettingsEffect.OpenDirectory(intent.Path));
    }
}
