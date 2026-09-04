using DshDesktop.Domain.Plugins;
using MiKiNuo.Mvi.Domain.MVI.Mediator;

namespace DshDesktop.Presentation.Avalonia.Features.Plugins;

/// <summary>
/// 表示获取插件清单的跨层请求（§28 Mediator）。
/// </summary>
public sealed record GetPluginListRequest : IMviRequest<IReadOnlyList<PluginInfo>>;

/// <summary>
/// 表示设置第三方插件启用状态的跨层请求；处理器先停 Runtime 再变更（Q7-A），
/// 响应为变更后的最新清单。
/// </summary>
/// <param name="Name">插件包名。</param>
/// <param name="Enabled">目标启用状态。</param>
public sealed record SetPluginEnabledRequest(string Name, bool Enabled)
    : IMviRequest<IReadOnlyList<PluginInfo>>;

/// <summary>
/// 表示卸载第三方插件的跨层请求；响应为变更后的最新清单。
/// </summary>
/// <param name="Name">插件包名。</param>
public sealed record UninstallPluginRequest(string Name)
    : IMviRequest<IReadOnlyList<PluginInfo>>;

/// <summary>
/// 表示安装插件的跨层请求（§19 事务，经 PluginOrchestrator 执行）；响应为提交后的最新清单。
/// </summary>
/// <param name="Source">npm 包名（可带版本）或本地 .tgz 文件路径。</param>
public sealed record InstallPluginRequest(string Source)
    : IMviRequest<IReadOnlyList<PluginInfo>>;

/// <summary>
/// 表示禁用全部第三方插件的跨层请求（Q6 恢复动作）；响应为变更后的最新清单。
/// </summary>
public sealed record DisableAllThirdPartyRequest
    : IMviRequest<IReadOnlyList<PluginInfo>>;
