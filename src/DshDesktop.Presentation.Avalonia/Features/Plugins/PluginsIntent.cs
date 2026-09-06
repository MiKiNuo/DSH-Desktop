using MiKiNuo.Mvi.Domain.MVI.Intent;

namespace DshDesktop.Presentation.Avalonia.Features.Plugins;

/// <summary>
/// 表示 Plugins 意图（业务语义命名，§7）。
/// </summary>
public abstract partial record PluginsIntent : IMviIntent
{
    /// <summary>
    /// 表示加载插件清单意图。
    /// </summary>
    public sealed partial record LoadPlugins : PluginsIntent;

    /// <summary>
    /// 表示启用第三方插件意图。
    /// </summary>
    /// <param name="Name">插件包名。</param>
    public sealed partial record EnablePlugin(string Name) : PluginsIntent;

    /// <summary>
    /// 表示禁用第三方插件意图。
    /// </summary>
    /// <param name="Name">插件包名。</param>
    public sealed partial record DisablePlugin(string Name) : PluginsIntent;

    /// <summary>
    /// 表示卸载第三方插件意图。
    /// </summary>
    /// <param name="Name">插件包名。</param>
    public sealed partial record UninstallPlugin(string Name) : PluginsIntent;

    /// <summary>
    /// 表示安装插件意图（§19 事务）。
    /// </summary>
    /// <param name="Source">npm 包名（可带版本）或本地 .tgz 文件路径。</param>
    public sealed partial record InstallPlugin(string Source) : PluginsIntent;

    /// <summary>
    /// 表示更新插件意图（Phase 8 评审 F3：行内"更新"按钮，走现有 UpdatePlugin 链路）。
    /// </summary>
    /// <param name="Name">插件包名。</param>
    public sealed partial record UpdatePlugin(string Name) : PluginsIntent;

    /// <summary>
    /// 表示可更新插件名投影变化的回流意图（BindSiblingState 自 UpdatesStore.PluginUpdates，§11.2）。
    /// </summary>
    /// <param name="Names">可更新插件包名列表。</param>
    public sealed partial record UpdatablePluginsChanged(IReadOnlyList<string> Names) : PluginsIntent;

    /// <summary>
    /// 表示安装事务阶段推进的回流意图（§20 状态机）。
    /// </summary>
    /// <param name="Operation">事务进度快照。</param>
    public sealed partial record PluginOperationChanged(
        DshDesktop.Domain.Plugins.PluginOperation Operation) : PluginsIntent;

    /// <summary>
    /// 表示插件清单已刷新（加载或变更成功）的回流意图。
    /// </summary>
    /// <param name="Plugins">最新插件清单。</param>
    public sealed partial record PluginsLoaded(IReadOnlyList<DshDesktop.Domain.Plugins.PluginInfo> Plugins) : PluginsIntent;

    /// <summary>
    /// 表示插件操作失败的回流意图。
    /// </summary>
    /// <param name="Error">错误信息。</param>
    public sealed partial record PluginOperationFailed(string Error) : PluginsIntent;
}
