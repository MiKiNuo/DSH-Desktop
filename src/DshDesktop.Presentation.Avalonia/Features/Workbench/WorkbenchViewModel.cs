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
/// </remarks>
public sealed partial class WorkbenchViewModel
    : MviViewModelBase<WorkbenchState, WorkbenchIntent, UnitEffect>
{
    private string? _dshUrl;
    private bool _runtimeReady;

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
        _ = BindSiblingState(runtimeStore, ApplyRuntimeState);
        ApplyRuntimeState(runtimeStore.CurrentState);
    }

    /// <summary>
    /// 获取 Runtime 是否就绪（决定 WebView 是否可导航）。
    /// </summary>
    public bool RuntimeReady
    {
        get => _runtimeReady;
        private set => SetProperty(ref _runtimeReady, value);
    }

    /// <summary>
    /// 获取 DSH Web UI 完整地址（含 token；Runtime 非 Running 时为 null）。
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

    private void ApplyRuntimeState(RuntimeState runtimeState)
    {
        bool running = runtimeState.Lifecycle is RuntimeLifecycle.Running;
        RuntimeReady = running;
        DshUrl = running ? runtimeState.Url : null;
    }

    /// <summary>
    /// 上报 WebView 导航完成（View → Intent，§5 规则 1）。
    /// </summary>
    /// <param name="url">完成地址。</param>
    public void NotifyNavigationCompleted(string url)
    {
        _ = DispatchAsync(new WorkbenchIntent.NavigationCompleted(url));
    }
}
