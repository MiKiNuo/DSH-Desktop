using MiKiNuo.Mvi.Domain.MVI.Effect;

namespace DshDesktop.Presentation.Avalonia.Features.Settings;

/// <summary>
/// 表示 Settings 副作用。
/// </summary>
public abstract partial record SettingsEffect : IMviEffect
{
    /// <summary>
    /// 表示加载设置信息副作用。
    /// </summary>
    public sealed partial record LoadSettings : SettingsEffect;

    /// <summary>
    /// 表示持久化安全模式副作用。
    /// </summary>
    /// <param name="Enabled">目标安全模式状态。</param>
    public sealed partial record SaveSafeMode(bool Enabled) : SettingsEffect;

    /// <summary>
    /// 表示持久化 DSH 更新通道副作用。
    /// </summary>
    /// <param name="Channel">目标通道。</param>
    public sealed partial record SaveChannel(string Channel) : SettingsEffect;
}
