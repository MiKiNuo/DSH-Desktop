namespace DshDesktop.Application.Updates;

/// <summary>
/// 表示更新检查时机计划（Phase 8 评审 F2，Spec c.2）：两个开关语义独立——
/// 启动时开 = 启动早期即检查（§34 知情破例）；后台开 = UI Ready 后检查；都关 = 不检查。
/// </summary>
/// <param name="AtStartup">启动早期即检查（不等 Runtime bootstrap）。</param>
/// <param name="AfterUiReady">UI Ready 后后台检查（启动早期已检查时为 false，同开不重复）。</param>
public sealed record UpdateCheckPlan(bool AtStartup, bool AfterUiReady);

/// <summary>
/// 推导更新检查时机（App 引导按计划在两个时机点发起后台检查）。
/// </summary>
public static class UpdateCheckSchedule
{
    /// <summary>
    /// 计算本次启动的检查时机。
    /// </summary>
    /// <param name="checkOnStartup">"启动时检查网络更新"开关（默认关）。</param>
    /// <param name="backgroundCheck">"后台检查更新"开关（默认开）。</param>
    /// <returns>检查时机计划。</returns>
    public static UpdateCheckPlan Plan(bool checkOnStartup, bool backgroundCheck)
    {
        return new UpdateCheckPlan(checkOnStartup, backgroundCheck && !checkOnStartup);
    }
}
