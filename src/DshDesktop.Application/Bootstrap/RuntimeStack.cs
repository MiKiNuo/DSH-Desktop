using DshDesktop.Application.Plugins;
using DshDesktop.Application.Runtime;
using DshDesktop.Application.Updates;

namespace DshDesktop.Application.Bootstrap;

/// <summary>
/// 启动编排回流的不可变运行时栈（组合根拆分批 2a）：装配好的全部运行时组件，字段不可空、回流后由组合根
/// 直接取用并接线。持有类型为 Application 自身端口/具体件，不泄漏 Infrastructure 具体类型（Application 不引用 Infrastructure）。
/// </summary>
/// <param name="ProcessHost">DSH 进程宿主（IRuntimeOrchestrator）。</param>
/// <param name="Supervisor">Runtime 监管器（真实状态源）。</param>
/// <param name="RuntimeProbe">进程 / HTTP 探测端口。</param>
/// <param name="Reattacher">重接管探测器。</param>
/// <param name="PluginRepository">Profile 文件级插件仓库。</param>
/// <param name="PluginOrchestrator">插件编排器（安装事务）。</param>
/// <param name="RuntimeRepository">DSH Runtime 仓库（side-by-side 版本管理 + npm 更新源）。</param>
/// <param name="DesktopUpdater">Desktop 自更新适配器。</param>
public sealed record RuntimeStack(
    IRuntimeOrchestrator ProcessHost,
    RuntimeSupervisor Supervisor,
    IRuntimeProbe RuntimeProbe,
    RuntimeReattacher Reattacher,
    IPluginManager PluginRepository,
    IPluginOrchestrator PluginOrchestrator,
    IRuntimeRepository RuntimeRepository,
    IDesktopUpdater DesktopUpdater);

/// <summary>
/// 工具链补全后需重建的栈子集（组合根拆分批 2a）：node/npm/pnpm 路径在构造期固化进
/// 插件仓库 / 编排器 / Runtime 仓库，首启安装补全工具链后仅重建这三件（进程宿主与监管器状态保留）。
/// </summary>
/// <param name="PluginRepository">重建后的插件仓库。</param>
/// <param name="PluginOrchestrator">重建后的插件编排器。</param>
/// <param name="RuntimeRepository">重建后的 Runtime 仓库。</param>
public sealed record ToolchainBoundStack(
    IPluginManager PluginRepository,
    IPluginOrchestrator PluginOrchestrator,
    IRuntimeRepository RuntimeRepository);
