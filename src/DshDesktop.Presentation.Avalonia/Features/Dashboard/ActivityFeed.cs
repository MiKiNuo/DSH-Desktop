using DshDesktop.Domain.Diagnostics;

namespace DshDesktop.Presentation.Avalonia.Features.Dashboard;

/// <summary>
/// 表示最近活动 feed 推导（Phase 8 Issue 03，纯函数）：
/// 从诊断事件流过滤结构化活动事件（App / Supervisor 且 Info 以上；
/// DSH 进程原始 stdout/stderr 行与 Debug 级计时不是"活动"），并截断到最新 N 条。
/// </summary>
public static class ActivityFeed
{
    /// <summary>活动 feed 展示窗口上限（原型 3 条示例；取区间上限 8 条）。</summary>
    public const int MaxEntries = 8;

    /// <summary>
    /// 判定事件是否为"活动"（结构化应用 / 监管事件，Info 级以上）。
    /// </summary>
    /// <param name="diagnosticEvent">诊断事件。</param>
    /// <returns>是否进入活动 feed。</returns>
    public static bool IsActivity(DiagnosticEvent diagnosticEvent)
    {
        return diagnosticEvent.Level >= DiagnosticLevel.Info
            && diagnosticEvent.Source is DiagnosticSource.App or DiagnosticSource.Supervisor;
    }

    /// <summary>
    /// 过滤并截断到最新 <see cref="MaxEntries"/> 条（输入为时间升序，输出保持升序）。
    /// </summary>
    /// <param name="entries">诊断事件窗口。</param>
    /// <returns>投影后的活动事件。</returns>
    public static IReadOnlyList<DiagnosticEvent> Project(IEnumerable<DiagnosticEvent> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return entries.Where(IsActivity).TakeLast(MaxEntries).ToArray();
    }
}
