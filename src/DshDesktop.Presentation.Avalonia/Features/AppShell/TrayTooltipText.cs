using DshDesktop.Domain.Runtime;

namespace DshDesktop.Presentation.Avalonia.Features.AppShell;

/// <summary>
/// 表示托盘 tooltip 状态文本（Phase 7 Issue 04）：Runtime 生命周期 → 中文状态词的纯映射。
/// 与 <see cref="Runtime.RuntimeLifecycleBrushes"/> 同级：表现层共享映射，App 托盘直接复用。
/// </summary>
public static class TrayTooltipText
{
    /// <summary>
    /// 生成完整 tooltip 文本（如 "DSH Desktop — 运行中"）。
    /// </summary>
    /// <param name="lifecycle">Runtime 生命周期。</param>
    /// <returns>托盘 tooltip 文本。</returns>
    public static string Format(RuntimeLifecycle lifecycle)
    {
        return $"DSH Desktop — {StatusText(lifecycle)}";
    }

    private static string StatusText(RuntimeLifecycle lifecycle)
    {
        return lifecycle switch
        {
            RuntimeLifecycle.Stopped => "已停止",
            RuntimeLifecycle.Starting => "启动中",
            RuntimeLifecycle.Running => "运行中",
            RuntimeLifecycle.Stopping => "停止中",
            RuntimeLifecycle.Recovering => "恢复中",
            RuntimeLifecycle.Failed => "失败",
            _ => "已停止",
        };
    }
}
