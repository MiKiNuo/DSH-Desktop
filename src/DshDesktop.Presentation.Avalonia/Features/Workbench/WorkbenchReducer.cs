using MiKiNuo.Mvi.Application.MVI.Reducer;
using MiKiNuo.Mvi.Domain.DI;
using MiKiNuo.Mvi.Domain.MVI.Effect;
using MiKiNuo.Mvi.Domain.MVI.Reducer;

namespace DshDesktop.Presentation.Avalonia.Features.Workbench;

/// <summary>
/// 表示 Workbench 规约器（纯函数；WebView 导航是 View 侧 IO，不产生副作用，
/// Effect 通道使用 <see cref="UnitEffect"/>）。
/// </summary>
[MviFeature]
public sealed partial class WorkbenchReducer
    : MviReducerBase<WorkbenchState, WorkbenchIntent, UnitEffect>
{
    /// <summary>
    /// 处理后退意图：标记加载中，实际历史导航由 View 调 WebView API。
    /// </summary>
    [MviReduce(typeof(WorkbenchIntent.NavigateBack))]
    private MviReduceResult<WorkbenchState, UnitEffect> HandleNavigateBack(
        WorkbenchState state,
        WorkbenchIntent.NavigateBack intent)
    {
        return MarkNavigating(state);
    }

    /// <summary>
    /// 处理前进意图：标记加载中，实际历史导航由 View 调 WebView API。
    /// </summary>
    [MviReduce(typeof(WorkbenchIntent.NavigateForward))]
    private MviReduceResult<WorkbenchState, UnitEffect> HandleNavigateForward(
        WorkbenchState state,
        WorkbenchIntent.NavigateForward intent)
    {
        return MarkNavigating(state);
    }

    /// <summary>
    /// 处理刷新意图：标记加载中，最新 Session URL 的导航由 ViewModel/View 完成。
    /// </summary>
    [MviReduce(typeof(WorkbenchIntent.Reload))]
    private MviReduceResult<WorkbenchState, UnitEffect> HandleReload(
        WorkbenchState state,
        WorkbenchIntent.Reload intent)
    {
        return MarkNavigating(state);
    }

    // 后退 / 前进 / 刷新的状态迁移同体（§21：Intent 类型保留业务语义，规约共享实现）。
    private MviReduceResult<WorkbenchState, UnitEffect> MarkNavigating(WorkbenchState state)
    {
        return Unchanged(state with { Loading = true, Error = null });
    }

    /// <summary>
    /// 处理导航开始意图。
    /// </summary>
    [MviReduce(typeof(WorkbenchIntent.NavigationStarted))]
    private MviReduceResult<WorkbenchState, UnitEffect> HandleNavigationStarted(
        WorkbenchState state,
        WorkbenchIntent.NavigationStarted intent)
    {
        return Unchanged(state with { CurrentUrl = intent.Url, Loading = true, Error = null });
    }

    /// <summary>
    /// 处理导航完成意图：结束加载并回流 WebView 历史标志。
    /// </summary>
    [MviReduce(typeof(WorkbenchIntent.NavigationCompleted))]
    private MviReduceResult<WorkbenchState, UnitEffect> HandleNavigationCompleted(
        WorkbenchState state,
        WorkbenchIntent.NavigationCompleted intent)
    {
        return Unchanged(state with
        {
            CurrentUrl = intent.Url,
            Loading = false,
            Error = null,
            CanGoBack = intent.CanGoBack,
            CanGoForward = intent.CanGoForward,
        });
    }

    /// <summary>
    /// 处理导航失败意图：结束加载并记录错误（页内错误条 + 重试，§21 Phase 6 修订）。
    /// </summary>
    [MviReduce(typeof(WorkbenchIntent.NavigationFailed))]
    private MviReduceResult<WorkbenchState, UnitEffect> HandleNavigationFailed(
        WorkbenchState state,
        WorkbenchIntent.NavigationFailed intent)
    {
        return Unchanged(state with { Loading = false, Error = intent.Error });
    }
}
