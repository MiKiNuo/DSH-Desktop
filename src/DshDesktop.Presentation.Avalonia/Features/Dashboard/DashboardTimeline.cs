using System.Globalization;
using DshDesktop.Application.Runtime;

namespace DshDesktop.Presentation.Avalonia.Features.Dashboard;

/// <summary>
/// 表示启动 timeline 的一行（Phase 8 Issue 03：条形图投影模型）。
/// </summary>
/// <param name="Name">段名。</param>
/// <param name="DurationMs">段耗时（毫秒）。</param>
/// <param name="WidthPercent">条形宽度（段耗时 / 总耗时的相对比例，0-100）。</param>
/// <param name="DurationText">耗时文本（&lt;1s 为 "N ms"，否则 "N.NN s"）。</param>
public sealed record DashboardTimelineRow(string Name, double DurationMs, double WidthPercent, string DurationText);

/// <summary>
/// 表示启动 timeline 推导（纯函数）：§46 Runtime.Start.Stage 累计计时 → 分段条形行。
/// 偏离说明：原型第 5 行 "WebView Ready" 使用进程入口时钟（StartupTimer.SinceProcessStart），
/// 与 Runtime.Start.Begin 时钟不同源，混合展示不诚实——本实现只投影 Runtime 启动链路的真实分段。
/// </summary>
public static class DashboardTimeline
{
    /// <summary>
    /// 由累计阶段计时构建分段行（宽度按占总耗时的相对比例）。
    /// </summary>
    /// <param name="timings">阶段累计计时（时间升序，单调不减）。</param>
    /// <returns>分段行列表。</returns>
    public static IReadOnlyList<DashboardTimelineRow> Build(IReadOnlyList<StartupStageTiming> timings)
    {
        if (timings.Count == 0)
        {
            return [];
        }

        double totalMs = timings[^1].Elapsed.TotalMilliseconds;
        List<DashboardTimelineRow> rows = new(timings.Count);
        TimeSpan previous = TimeSpan.Zero;
        foreach (StartupStageTiming timing in timings)
        {
            double durationMs = Math.Max(0, (timing.Elapsed - previous).TotalMilliseconds);
            rows.Add(new DashboardTimelineRow(
                SegmentName(timing.Stage),
                durationMs,
                totalMs > 0 ? durationMs / totalMs * 100.0 : 0,
                FormatDuration(durationMs)));
            previous = timing.Elapsed;
        }

        return rows;
    }

    private static string SegmentName(RuntimeStartupSignal signal)
    {
        return signal switch
        {
            RuntimeStartupSignal.Spawning => "环境校验",
            RuntimeStartupSignal.WaitingReady => "拉起 DSH 进程",
            RuntimeStartupSignal.HttpProbing => "Node / Profile / Plugins",
            RuntimeStartupSignal.Ready => "HTTP Ready",
            _ => signal.ToString(),
        };
    }

    private static string FormatDuration(double durationMs)
    {
        return durationMs < 1000
            ? string.Create(CultureInfo.InvariantCulture, $"{durationMs:0} ms")
            : string.Create(CultureInfo.InvariantCulture, $"{durationMs / 1000.0:0.00} s");
    }
}
