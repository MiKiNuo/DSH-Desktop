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
    private readonly IMviStore<RuntimeState, RuntimeIntent, RuntimeEffect> _runtimeStore;
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
        _runtimeStore = runtimeStore;
        _ = BindSiblingState(runtimeStore, ApplyRuntimeState);
        ApplyRuntimeState(runtimeStore.CurrentState);
    }

    /// <summary>
    /// 刷新请求事件：携带从 Runtime Store 当前状态取出的最新 Session URL，由 View 执行 WebView 导航。
    /// </summary>
    public event Action<string>? ReloadRequested;

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

    /// <summary>
    /// 获取是否可后退。
    /// </summary>
    [MviBind(nameof(WorkbenchState.CanGoBack), BindingMode = MviBindingMode.OneWay)]
    public partial bool CanGoBack { get; private set; }

    /// <summary>
    /// 获取是否可前进。
    /// </summary>
    [MviBind(nameof(WorkbenchState.CanGoForward), BindingMode = MviBindingMode.OneWay)]
    public partial bool CanGoForward { get; private set; }

    /// <summary>
    /// 获取是否正在加载页面。
    /// </summary>
    [MviBind(nameof(WorkbenchState.Loading), BindingMode = MviBindingMode.OneWay)]
    public partial bool Loading { get; private set; }

    /// <summary>
    /// 获取最近一次导航错误信息。
    /// </summary>
    [MviBind(nameof(WorkbenchState.Error), BindingMode = MviBindingMode.OneWay)]
    public partial string? Error { get; private set; }

    /// <summary>
    /// 请求后退（View 确认 WebView 可后退后调用；实际历史导航由 View 调 WebView API）。
    /// </summary>
    public void RequestGoBack()
    {
        _ = DispatchAsync(new WorkbenchIntent.NavigateBack());
    }

    /// <summary>
    /// 请求前进（View 确认 WebView 可前进后调用；实际历史导航由 View 调 WebView API）。
    /// </summary>
    public void RequestGoForward()
    {
        _ = DispatchAsync(new WorkbenchIntent.NavigateForward());
    }

    /// <summary>
    /// 请求刷新：从 Runtime Store 当前状态取最新 Session URL（token 一次性，禁止缓存旧 URL，
    /// §21 Phase 6 修订），经 <see cref="ReloadRequested"/> 通知 View 导航。
    /// </summary>
    public void RequestReload()
    {
        RuntimeState current = _runtimeStore.CurrentState;
        if (current.Lifecycle is not RuntimeLifecycle.Running || current.Url is not { } url)
        {
            return;
        }

        _ = DispatchAsync(new WorkbenchIntent.Reload());
        ReloadRequested?.Invoke(url);
    }

    /// <summary>
    /// 上报 WebView 导航开始（View → Intent，§5 规则 1）。
    /// </summary>
    /// <param name="url">目标地址。</param>
    public void NotifyNavigationStarted(string url)
    {
        _ = DispatchAsync(new WorkbenchIntent.NavigationStarted(url));
    }

    /// <summary>
    /// 上报 WebView 导航完成（View → Intent，§5 规则 1）。
    /// </summary>
    /// <param name="url">完成地址。</param>
    /// <param name="canGoBack">完成时 WebView 是否可后退。</param>
    /// <param name="canGoForward">完成时 WebView 是否可前进。</param>
    public void NotifyNavigationCompleted(string url, bool canGoBack, bool canGoForward)
    {
        _ = DispatchAsync(new WorkbenchIntent.NavigationCompleted(url, canGoBack, canGoForward));
    }

    /// <summary>
    /// 上报 WebView 导航失败（View → Intent，§5 规则 1）。
    /// </summary>
    /// <param name="error">错误信息。</param>
    public void NotifyNavigationFailed(string error)
    {
        _ = DispatchAsync(new WorkbenchIntent.NavigationFailed(error));
    }

    private void ApplyRuntimeState(RuntimeState runtimeState)
    {
        bool running = runtimeState.Lifecycle is RuntimeLifecycle.Running;
        RuntimeReady = running;
        DshUrl = running ? runtimeState.Url : null;
    }
}
