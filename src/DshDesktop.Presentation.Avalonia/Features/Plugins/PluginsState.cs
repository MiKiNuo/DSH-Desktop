using DshDesktop.Domain.Plugins;
using MiKiNuo.Mvi.Domain.MVI.State;

namespace DshDesktop.Presentation.Avalonia.Features.Plugins;

/// <summary>
/// 表示 Plugins 状态。
/// </summary>
/// <param name="Plugins">插件清单。</param>
/// <param name="PendingOperation">进行中的操作描述；null 表示空闲。</param>
/// <param name="Operation">安装事务进度（§20 状态机）；无事务为 null。</param>
/// <param name="LastError">最近一次错误信息。</param>
/// <param name="UpdatablePlugins">可更新插件名投影（BindSiblingState 自 UpdatesStore.PluginUpdates，§11.2；Phase 8 评审 F3）。</param>
public sealed record PluginsState(
    IReadOnlyList<PluginInfo> Plugins,
    string? PendingOperation,
    PluginOperation? Operation,
    string? LastError,
    IReadOnlyList<string> UpdatablePlugins) : IMviState
{
    /// <summary>
    /// 获取初始状态。
    /// </summary>
    public static PluginsState Initial { get; } =
        new(System.Array.Empty<PluginInfo>(), null, null, null, System.Array.Empty<string>());
}
