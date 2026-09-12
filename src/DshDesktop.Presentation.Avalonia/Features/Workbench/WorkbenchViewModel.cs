using DshDesktop.Domain.Runtime;
using DshDesktop.Presentation.Avalonia.Features.Runtime;
using MiKiNuo.Mvi.Application.MVI.Store;
using MiKiNuo.Mvi.Application.MVI.Threading;
using MiKiNuo.Mvi.Application.MVI.ViewModel;
using MiKiNuo.Mvi.Domain.MVI.Binding;
using MiKiNuo.Mvi.Domain.MVI.Effect;

namespace DshDesktop.Presentation.Avalonia.Features.Workbench;

/// <summary>
/// 表示 Workbench ViewModel。
/// </summary>
/// <remarks>
/// DSH Web UI 地址经 <see cref="MviViewModelBase{TState, TIntent, TEffect}.BindSiblingState"/>
/// 从 Runtime Store 投影（兄弟 Store 只读订阅，§11.2 允许的非父子协作方式之一）。
/// 用户移除页内工具条后，Refresh / GoBack / GoForward 三条命令通道已删除——
/// 本 ViewModel 只剩下投影职责与导航事件上报。
/// </remarks>
public sealed partial class WorkbenchViewModel
    : MviViewModelBase<WorkbenchState, WorkbenchIntent, UnitEffect>
{
    private readonly IMviStore<RuntimeState, RuntimeIntent, RuntimeEffect> _runtimeStore;
    private string? _dshUrl;

    /// <summary>
    /// 初始化 Workbench ViewModel。
    /// </summary>
    /// <param name="store">Workbench 状态存储。</param>
    /// <param name="runtimeStore">Runtime 状态存储（兄弟 Store，只读订阅）。</param>
    /// <param name="uiDispatcher">UI 调度器。</param>
    public WorkbenchViewModel(
        IMviStore<WorkbenchState, WorkbenchIntent, UnitEffect> store,
        IMviStore<RuntimeState, RuntimeIntent, RuntimeEffect> runtimeStore,
        IMviUiDispatcher? uiDispatcher = null)
        : base(store, uiDispatcher)
    {
        ArgumentNullException.ThrowIfNull(runtimeStore);
        _runtimeStore = runtimeStore;
        _ = BindSiblingState(runtimeStore, ApplyRuntimeState);
        ApplyRuntimeState(runtimeStore.CurrentState);
    }

    /// <summary>
    /// 获取 DSH Web UI 完整地址（含 token；Runtime 非 Running 时为 null）。
    /// 这是本页唯一的对外投影：非 null 即导航，为 null 即退回占位层。
    /// </summary>
    public string? DshUrl
    {
        get => _dshUrl;
        private set => SetProperty(ref _dshUrl, value);
    }

    /// <summary>
    /// 获取当前导航地址。
    /// </summary>
    [MviBind(nameof(WorkbenchState.CurrentUrl), BindingMode = MviBindingMode.OneWay)]
    public partial string? CurrentUrl { get; private set; }

    /// <summary>
    /// 获取是否正在加载页面（驱动内容区顶部的加载条）。
    /// </summary>
    [MviBind(nameof(WorkbenchState.Loading), BindingMode = MviBindingMode.OneWay)]
    public partial bool Loading { get; private set; }

    /// <summary>
    /// 上报 WebView 导航开始（View → Intent，§5 规则 1）。
    /// </summary>
    /// <param name="url">目标地址。</param>
    public void NotifyNavigationStarted(string url)
    {
        _ = DispatchAsync(new WorkbenchIntent.NavigationStarted(url));
    }

    /// <summary>
    /// 上报 WebView 导航完成（View → Intent，§5 规则 1）。成功与失败共用此入口——
    /// 失败不再有页内错误条承载，但同样要结束加载，否则加载条永久悬停。
    /// </summary>
    /// <param name="url">完成地址。</param>
    public void NotifyNavigationCompleted(string url)
    {
        _ = DispatchAsync(new WorkbenchIntent.NavigationCompleted(url));
    }

    private void ApplyRuntimeState(RuntimeState runtimeState)
    {
        DshUrl = runtimeState.Lifecycle is RuntimeLifecycle.Running ? runtimeState.Url : null;
    }
}
