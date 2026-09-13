using Avalonia.Media;
using DshDesktop.Domain.Runtime;

namespace DshDesktop.Presentation.Avalonia.Features.Runtime;

/// <summary>
/// 表示一条生命周期投影：图标键 + 描边色 + 半透明底色。
/// 三者的取值口径由 <see cref="RuntimeLifecycleProjection"/> 唯一决定。
/// </summary>
/// <param name="IconKey">主题字典中的图标资源键（如 <c>IconCheck</c>）。</param>
/// <param name="Color">描边 / 强调色画刷。</param>
/// <param name="Tint">半透明底色画刷。</param>
public readonly record struct LifecycleProjection(string IconKey, IBrush Color, IBrush Tint);

/// <summary>
/// 表示 Runtime 生命周期到「图标 + 着色 + 底色」的唯一投影（CONTEXT.md: Runtime Lifecycle）。
///
/// 收敛前该映射散落在 <c>DashboardView.ApplyLifecycleIndicator</c> 与
/// <c>RuntimeView.ApplyIndicators</c> 两处 switch，各自演化后对同一状态给出了不同图标
/// （Stopped：IconInfo vs IconPower；Starting：IconActivity vs IconRefresh）。
/// 颜色侧早已由 <see cref="RuntimeLifecycleBrushes"/> 共享，唯独图标键缺席——本类补上这一项，
/// 使三项取值出自同一个口。
///
/// 投影是纯函数且无状态：View 每次属性变更都会重新求值，不做缓存。
/// </summary>
public static class RuntimeLifecycleProjection
{
    /// <summary>
    /// 按生命周期取投影。过渡态（Starting / Stopping / Recovering）共用一个映射，
    /// 与 <see cref="RuntimeLifecycleBrushes.For"/> 的分组保持一致。
    /// </summary>
    /// <param name="lifecycle">生命周期状态。</param>
    /// <returns>该状态对应的图标键、着色与底色。</returns>
    public static LifecycleProjection For(RuntimeLifecycle lifecycle)
    {
        return lifecycle switch
        {
            RuntimeLifecycle.Running => new(
                "IconCheck", RuntimeLifecycleBrushes.Running, RuntimeLifecycleBrushes.TintRunning),
            RuntimeLifecycle.Failed => new(
                "IconAlert", RuntimeLifecycleBrushes.Failed, RuntimeLifecycleBrushes.TintFailed),
            RuntimeLifecycle.Stopped => new(
                "IconPower", RuntimeLifecycleBrushes.Stopped, RuntimeLifecycleBrushes.TintStopped),
            _ => new(
                "IconRefresh", RuntimeLifecycleBrushes.Transition, RuntimeLifecycleBrushes.TintTransition),
        };
    }
}
