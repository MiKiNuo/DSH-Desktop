using DshDesktop.Domain.Plugins;
using DshDesktop.Domain.Runtime;

namespace DshDesktop.Presentation.Avalonia.Features.AppShell;

/// <summary>
/// 壳 toast 三判定的纯函数投影器（候选 5：边沿判定自 MainWindow 下沉）。
/// 全部方法无副作用、可单测；文案逐字沿用 MainWindow 现状，不得自行改写。
/// </summary>
public static class ShellToastProjector
{
    /// <summary>
    /// 更新徽标上升沿：current &gt; previous 时返回"发现 {current} 项可用更新"，否则 null。
    /// 持平/下降不弹（首帧 previous==current 亦不弹）。
    /// </summary>
    public static string? ProjectBadgeRisingEdge(int previousBadge, int currentBadge)
    {
        if (currentBadge > previousBadge)
        {
            return $"发现 {currentBadge} 项可用更新";
        }

        return null;
    }

    /// <summary>
    /// Runtime 生命周期迁移：previous==Recovering &amp;&amp; current==Running 时返回"Runtime 已恢复运行"，否则 null。
    /// </summary>
    public static string? ProjectLifecycleRecovered(RuntimeLifecycle previous, RuntimeLifecycle current)
    {
        if (previous is RuntimeLifecycle.Recovering && current is RuntimeLifecycle.Running)
        {
            return "Runtime 已恢复运行";
        }

        return null;
    }

    /// <summary>
    /// 插件终态引用去重：current 与 alreadyNotified 引用不同且 current 为 Completed/Failed 时返回对应文案，否则 null。
    /// Completed→"插件 {name} {verb}完成"；Failed→"插件 {name} {verb}失败：{Error}"。
    /// verb 来自 <see cref="PluginOperationText.Verb"/>，逐字沿用 MainWindow。
    /// 非终态新引用返回 null（不应据此标记为已通知，见 <see cref="AppShellViewModel"/> 的调用约定）。
    /// </summary>
    public static string? ProjectPluginOperationDone(PluginOperation? alreadyNotified, PluginOperation? current)
    {
        if (current is null || ReferenceEquals(current, alreadyNotified))
        {
            return null;
        }

        string verb = PluginOperationText.Verb(current.Kind);
        if (current.Stage is PluginOperationStage.Completed)
        {
            return $"插件 {current.PluginName} {verb}完成";
        }

        if (current.Stage is PluginOperationStage.Failed)
        {
            return $"插件 {current.PluginName} {verb}失败：{current.Error}";
        }

        return null;
    }
}
