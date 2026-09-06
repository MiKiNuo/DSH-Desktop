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

    /// <summary>
    /// 按生命周期取中文状态词（如 "运行中"；托盘 tooltip 与壳状态文本共用此映射）。
    /// </summary>
    /// <param name="lifecycle">Runtime 生命周期。</param>
    /// <returns>中文状态词。</returns>
    public static string StatusText(RuntimeLifecycle lifecycle)
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

    /// <summary>
    /// 格式化启动耗时（原型状态栏 "Startup 1.82s" 的数值部分：两位小数 + s；
    /// Phase 8 评审 F12：自 ShellStatusText 收编，壳状态栏与 Dashboard 统计卡共用）。
    /// </summary>
    /// <param name="elapsed">启动耗时。</param>
    /// <returns>格式化文本（如 "1.82s"）。</returns>
    public static string FormatStartupElapsed(TimeSpan elapsed)
    {
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{elapsed.TotalSeconds:0.00}s");
    }
}
