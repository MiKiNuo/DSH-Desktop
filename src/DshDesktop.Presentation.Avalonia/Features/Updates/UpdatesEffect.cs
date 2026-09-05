using MiKiNuo.Mvi.Domain.MVI.Effect;

namespace DshDesktop.Presentation.Avalonia.Features.Updates;

/// <summary>
/// 表示 Updates 副作用。
/// </summary>
public abstract partial record UpdatesEffect : IMviEffect
{
    /// <summary>
    /// 表示检查更新副作用。
    /// </summary>
    public sealed partial record CheckUpdates : UpdatesEffect;

    /// <summary>
    /// 表示安装指定版本 DSH Runtime 副作用。
    /// </summary>
    /// <param name="Version">目标版本。</param>
    public sealed partial record InstallDshRuntime(string Version) : UpdatesEffect;

    /// <summary>
    /// 表示激活指定 Runtime 副作用。
    /// </summary>
    /// <param name="Version">版本目录名，空字符串表示借用。</param>
    public sealed partial record ActivateDshRuntime(string Version) : UpdatesEffect;

    /// <summary>
    /// 表示更新插件副作用（走 §19 安装事务）。
    /// </summary>
    /// <param name="Name">插件包名。</param>
    public sealed partial record UpdatePlugin(string Name) : UpdatesEffect;

    /// <summary>
    /// 表示下载并应用 Desktop 更新副作用。
    /// </summary>
    public sealed partial record DownloadAndApplyDesktopUpdate : UpdatesEffect;
}
