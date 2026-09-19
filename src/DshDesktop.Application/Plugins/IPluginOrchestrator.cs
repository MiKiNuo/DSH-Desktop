using DshDesktop.Application.Runtime;
using DshDesktop.Domain.Plugins;
using DshDesktop.Domain.Runtime;
using Serilog;

namespace DshDesktop.Application.Plugins;

/// <summary>
/// 表示插件编排端口（§4.2 / §19：安装事务的业务工作流编排）。
/// </summary>
public interface IPluginOrchestrator
{
    /// <summary>
    /// 插件操作阶段变化时触发。
    /// </summary>
    event EventHandler<PluginOperation>? OperationChanged;

    /// <summary>
    /// 执行插件安装事务（§19）：快照 → 停 Runtime → 安装 → 校验 → 启动 → 健康检查 → 提交；
    /// 任何失败 → 回滚（恢复快照 + 重建）→ 尽力重启 Runtime → Failed。
    /// </summary>
    /// <param name="source">npm 包名（可带版本）或本地 .tgz 文件路径。</param>
    /// <param name="kind">操作种类（安装 / 更新），供下游反馈文案区分。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>安装的插件包名。</returns>
    Task<string> InstallAsync(string source, PluginOperationKind kind, CancellationToken cancellationToken);

    /// <summary>
    /// 执行插件卸载事务（与安装同一套管线）：快照 → 停 Runtime → 卸载 → 校验清单已移除 →
    /// 启动 → 健康检查 → 提交；任何失败 → 回滚（恢复快照）→ 尽力重启 Runtime → Failed。
    /// 目标插件名全程已知，各阶段 Operation.PluginName 自始携带。
    /// </summary>
    /// <param name="name">插件包名。</param>
    /// <param name="cancellationToken">取消标记。</param>
    Task UninstallAsync(string name, CancellationToken cancellationToken);

    /// <summary>
    /// 执行插件启用/禁用事务（与安装同一套管线）：快照 → 停 Runtime → 应用变更 →
    /// 校验清单中目标状态 → 启动 → 健康检查 → 提交；任何失败 → 回滚 → 尽力重启 Runtime → Failed。
    /// 操作种类按目标态记为 <see cref="PluginOperationKind.Enable"/> / <see cref="PluginOperationKind.Disable"/>。
    /// </summary>
    /// <param name="name">插件包名。</param>
    /// <param name="enabled">目标启用状态。</param>
    /// <param name="cancellationToken">取消标记。</param>
    Task SetEnabledAsync(string name, bool enabled, CancellationToken cancellationToken);

    /// <summary>
    /// 禁用全部第三方插件（Q6 恢复动作；调用方负责随后启动 Runtime）。
    /// </summary>
    /// <param name="cancellationToken">取消标记。</param>
    Task DisableAllThirdPartyAsync(CancellationToken cancellationToken);
}
