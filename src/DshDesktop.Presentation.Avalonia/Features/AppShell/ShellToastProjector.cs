using DshDesktop.Domain.Runtime;

namespace DshDesktop.Presentation.Avalonia.Features.AppShell;

/// <summary>
/// 壳 toast 判定的纯函数投影器（候选 5：边沿判定自 MainWindow 下沉）。
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
}
