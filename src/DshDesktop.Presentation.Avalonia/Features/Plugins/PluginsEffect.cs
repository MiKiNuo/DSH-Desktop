using MiKiNuo.Mvi.Domain.MVI.Effect;

namespace DshDesktop.Presentation.Avalonia.Features.Plugins;

/// <summary>
/// 表示 Plugins 副作用。
/// </summary>
public abstract partial record PluginsEffect : IMviEffect
{
    /// <summary>
    /// 表示加载插件清单副作用。
    /// </summary>
    public sealed partial record LoadPlugins : PluginsEffect;

    /// <summary>
    /// 表示设置第三方插件启用状态副作用。
    /// </summary>
    /// <param name="Name">插件包名。</param>
    /// <param name="Enabled">目标启用状态。</param>
    public sealed partial record SetPluginEnabled(string Name, bool Enabled) : PluginsEffect;

    /// <summary>
    /// 表示卸载第三方插件副作用。
    /// </summary>
    /// <param name="Name">插件包名。</param>
    public sealed partial record UninstallPlugin(string Name) : PluginsEffect;

    /// <summary>
    /// 表示安装插件副作用（§19 事务）。
    /// </summary>
    /// <param name="Source">npm 包名（可带版本）或本地 .tgz 文件路径。</param>
    public sealed partial record InstallPlugin(string Source) : PluginsEffect;

    /// <summary>
    /// 表示更新插件副作用（走组合根 UpdatePlugin 链路：安装 name@latest 后刷新清单）。
    /// </summary>
    /// <param name="Name">插件包名。</param>
    public sealed partial record UpdatePlugin(string Name) : PluginsEffect;
}
