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
    /// 处理修改安全模式意图（乐观更新，持久化经副作用）。
    /// </summary>
    [MviReduce(typeof(SettingsIntent.ChangeSafeMode))]
    private MviReduceResult<SettingsState, SettingsEffect> HandleChangeSafeMode(
        SettingsState state,
        SettingsIntent.ChangeSafeMode intent)
    {
        if (state.SafeMode == intent.Enabled)
        {
            return Unchanged(state);
        }

        return WithEffect(
            state with { SafeMode = intent.Enabled, LastError = null },
            new SettingsEffect.SaveSafeMode(intent.Enabled));
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
            Channel = intent.Info.Channel,
            NodePath = intent.Info.NodePath,
            DshHome = intent.Info.DshHome,
            DataDirectory = intent.Info.DataDirectory,
            DesktopVersion = intent.Info.DesktopVersion,
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
}
