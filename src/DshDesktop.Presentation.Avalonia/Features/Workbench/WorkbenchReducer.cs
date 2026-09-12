using MiKiNuo.Mvi.Application.MVI.Reducer;
using MiKiNuo.Mvi.Domain.DI;
using MiKiNuo.Mvi.Domain.MVI.Effect;
using MiKiNuo.Mvi.Domain.MVI.Reducer;

namespace DshDesktop.Presentation.Avalonia.Features.Workbench;

/// <summary>
/// 表示 Workbench 规约器（纯函数；WebView 导航是 View 侧 IO，不产生副作用，
/// Effect 通道使用 <see cref="UnitEffect"/>）。
/// </summary>
/// <remarks>
/// 用户移除页内工具条（后退 / 前进 / 刷新）与错误条后，对应分支整链删除。
/// 规则收敛为两条：导航开始 → 标记加载中；导航完成 → 结束加载。
/// 导航失败的视觉提示已退回日志与诊断中心（用户明确接受的缩减）。
/// </remarks>
[MviFeature]
public sealed partial class WorkbenchReducer
    : MviReducerBase<WorkbenchState, WorkbenchIntent, UnitEffect>
{
    /// <summary>
    /// 处理导航开始意图。
    /// </summary>
    [MviReduce(typeof(WorkbenchIntent.NavigationStarted))]
    private MviReduceResult<WorkbenchState, UnitEffect> HandleNavigationStarted(
        WorkbenchState state,
        WorkbenchIntent.NavigationStarted intent)
    {
        return Unchanged(state with { CurrentUrl = intent.Url, Loading = true });
    }

    /// <summary>
    /// 处理导航完成意图：只结束加载并记录落点地址。
    /// </summary>
    [MviReduce(typeof(WorkbenchIntent.NavigationCompleted))]
    private MviReduceResult<WorkbenchState, UnitEffect> HandleNavigationCompleted(
        WorkbenchState state,
        WorkbenchIntent.NavigationCompleted intent)
    {
        return Unchanged(state with { CurrentUrl = intent.Url, Loading = false });
    }
}
