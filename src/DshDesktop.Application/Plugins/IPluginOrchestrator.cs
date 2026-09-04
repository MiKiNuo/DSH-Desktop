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
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>安装的插件包名。</returns>
    Task<string> InstallAsync(string source, CancellationToken cancellationToken);

    /// <summary>
    /// 禁用全部第三方插件（Q6 恢复动作；调用方负责随后启动 Runtime）。
    /// </summary>
    /// <param name="cancellationToken">取消标记。</param>
    Task DisableAllThirdPartyAsync(CancellationToken cancellationToken);
}
