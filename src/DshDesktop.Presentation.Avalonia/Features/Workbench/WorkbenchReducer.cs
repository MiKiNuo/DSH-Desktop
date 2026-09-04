using MiKiNuo.Mvi.Application.MVI.Reducer;
using MiKiNuo.Mvi.Domain.DI;
using MiKiNuo.Mvi.Domain.MVI.Effect;
using MiKiNuo.Mvi.Domain.MVI.Reducer;

namespace DshDesktop.Presentation.Avalonia.Features.Workbench;

/// <summary>
/// 表示 Workbench 规约器。Phase 1 无副作用，Effect 通道使用 <see cref="UnitEffect"/>。
/// </summary>
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
        return Unchanged(state with { CurrentUrl = intent.Url });
    }

    /// <summary>
    /// 处理导航完成意图。
    /// </summary>
    [MviReduce(typeof(WorkbenchIntent.NavigationCompleted))]
    private MviReduceResult<WorkbenchState, UnitEffect> HandleNavigationCompleted(
        WorkbenchState state,
        WorkbenchIntent.NavigationCompleted intent)
    {
        return Unchanged(state with { CurrentUrl = intent.Url });
    }

    /// <summary>
    /// 处理导航失败意图。
    /// </summary>
    [MviReduce(typeof(WorkbenchIntent.NavigationFailed))]
    private MviReduceResult<WorkbenchState, UnitEffect> HandleNavigationFailed(
        WorkbenchState state,
        WorkbenchIntent.NavigationFailed intent)
    {
        return Unchanged(state);
    }
}
