using DshDesktop.Application.Runtime;

namespace DshDesktop.Application.Bootstrap;

/// <summary>
/// 运行时栈装配工厂端口（组合根拆分批 2a）：把"构造期固化路径的具体件"创建工作收口到工厂，组合根实现工厂
/// （new Infrastructure 具体件——App→Infrastructure 引用合法，Application 不得新增对 Infrastructure 引用）。
/// 编排器经此端口拿到装配好的运行时栈，不直接 new Infrastructure/Avalonia 之外的具体件。
/// </summary>
public interface IRuntimeStackFactory
{
    /// <summary>按已加载配置装配运行时栈（ProcessHost → Supervisor → Probe → Reattacher → 插件栈 → RuntimeRepository → DesktopUpdater）。</summary>
    /// <param name="config">已加载的配置（路径字段在编排期可能已被自举回写）。</param>
    /// <returns>装配好的运行时栈。</returns>
    RuntimeStack Create(IRuntimeConfig config);

    /// <summary>工具链补全后重建构造期固化路径的栈子集（插件栈 + Runtime 仓库）；监管器等状态件保留。</summary>
    /// <param name="config">已补全工具链路径的配置。</param>
    /// <param name="supervisor">在役 Runtime 监管器（插件编排器构造期依赖）。</param>
    /// <returns>重建后的栈子集。</returns>
    ToolchainBoundStack RebuildToolchainBound(IRuntimeConfig config, RuntimeSupervisor supervisor);
}
