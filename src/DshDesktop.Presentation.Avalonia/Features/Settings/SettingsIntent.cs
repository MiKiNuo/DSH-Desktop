using MiKiNuo.Mvi.Domain.MVI.Intent;

namespace DshDesktop.Presentation.Avalonia.Features.Settings;

/// <summary>
/// 表示 Settings 意图（业务语义命名，§7）。
/// </summary>
public abstract partial record SettingsIntent : IMviIntent
{
    /// <summary>
    /// 表示加载设置意图（进入 Settings 页时发起）。
    /// </summary>
    public sealed partial record LoadSettings : SettingsIntent;

    /// <summary>
    /// 表示切换安全模式意图（无载荷翻转，与 AppShell ToggleSidebar 同先例）。
    /// </summary>
    public sealed partial record ToggleSafeMode : SettingsIntent;

    /// <summary>
    /// 表示修改 DSH 更新通道意图（乐观更新）。
    /// </summary>
    /// <param name="Channel">目标通道（latest / alpha）。</param>
    public sealed partial record ChangeChannel(string Channel) : SettingsIntent;

    /// <summary>
    /// 表示设置信息已加载的回流意图。
    /// </summary>
    /// <param name="Info">设置信息快照。</param>
    public sealed partial record SettingsLoaded(SettingsInfo Info) : SettingsIntent;

    /// <summary>
    /// 表示设置操作失败的回流意图。
    /// </summary>
    /// <param name="Error">错误信息。</param>
    public sealed partial record SettingsOperationFailed(string Error) : SettingsIntent;
}
