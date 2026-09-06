namespace DshDesktop.Presentation.Avalonia.Features.AppShell;

/// <summary>
/// 表示关窗处置策略（Phase 8 Issue 05，纯函数便于直测；ADR-0005 正交语义）：
/// "最小化到托盘"只决定关窗是否拦截为隐藏；托盘菜单的显式退出意图永不拦截。
/// </summary>
public static class WindowClosePolicy
{
    /// <summary>
    /// 判定本次关窗是否应拦截为隐藏到托盘。
    /// </summary>
    /// <param name="minimizeToTrayOnClose">"关闭窗口最小化到托盘"开关当前值。</param>
    /// <param name="exitRequested">是否托盘菜单发起的真实退出。</param>
    /// <returns>拦截关闭并隐藏窗口返回 true；否则走现状退出链路。</returns>
    public static bool ShouldHideToTray(bool minimizeToTrayOnClose, bool exitRequested)
    {
        return minimizeToTrayOnClose && !exitRequested;
    }
}
