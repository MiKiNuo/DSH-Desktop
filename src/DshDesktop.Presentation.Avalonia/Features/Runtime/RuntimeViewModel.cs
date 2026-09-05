using DshDesktop.Domain.Runtime;
using MiKiNuo.Mvi.Application.MVI.Command;
using MiKiNuo.Mvi.Application.MVI.Store;
using MiKiNuo.Mvi.Application.MVI.Threading;
using MiKiNuo.Mvi.Application.MVI.ViewModel;
using MiKiNuo.Mvi.Domain.MVI.Binding;

namespace DshDesktop.Presentation.Avalonia.Features.Runtime;

/// <summary>
/// 表示 Runtime ViewModel（State 投影 + 命令，不持有业务真实状态）。
/// </summary>
public sealed partial class RuntimeViewModel
    : MviViewModelBase<RuntimeState, RuntimeIntent, RuntimeEffect>
{
    /// <summary>
    /// 初始化 Runtime ViewModel。
    /// </summary>
    /// <param name="store">Runtime 状态存储。</param>
    /// <param name="uiDispatcher">UI 调度器。</param>
    public RuntimeViewModel(
        IMviStore<RuntimeState, RuntimeIntent, RuntimeEffect> store,
        IMviUiDispatcher? uiDispatcher = null)
        : base(store, uiDispatcher)
    {
    }

    /// <summary>
    /// 获取 Runtime 生命周期状态。
    /// </summary>
    [MviBind(nameof(RuntimeState.Lifecycle), BindingMode = MviBindingMode.OneWay)]
    public partial RuntimeLifecycle Lifecycle { get; private set; }

    /// <summary>
    /// 获取健康状态。
    /// </summary>
    [MviBind(nameof(RuntimeState.Health), BindingMode = MviBindingMode.OneWay)]
    public partial RuntimeHealth Health { get; private set; }

    /// <summary>
    /// 获取启动阶段。
    /// </summary>
    [MviBind(nameof(RuntimeState.StartupStage), BindingMode = MviBindingMode.OneWay)]
    public partial RuntimeStartupStage StartupStage { get; private set; }

    /// <summary>
    /// 获取本次启动耗时。
    /// </summary>
    [MviBind(nameof(RuntimeState.StartupElapsed), BindingMode = MviBindingMode.OneWay)]
    public partial TimeSpan? StartupElapsed { get; private set; }

    /// <summary>
    /// 获取实际监听端口。
    /// </summary>
    [MviBind(nameof(RuntimeState.Port), BindingMode = MviBindingMode.OneWay)]
    public partial int? Port { get; private set; }

    /// <summary>
    /// 获取 Session URL（含 token）。
    /// </summary>
    [MviBind(nameof(RuntimeState.Url), BindingMode = MviBindingMode.OneWay)]
    public partial string? Url { get; private set; }

    /// <summary>
    /// 获取最近一次错误信息。
    /// </summary>
    [MviBind(nameof(RuntimeState.LastError), BindingMode = MviBindingMode.OneWay)]
    public partial string? LastError { get; private set; }

    /// <summary>
    /// 获取是否处于安全模式。
    /// </summary>
    [MviBind(nameof(RuntimeState.SafeMode), BindingMode = MviBindingMode.OneWay)]
    public partial bool SafeMode { get; private set; }

    /// <summary>
    /// 获取启动 Runtime 命令。
    /// </summary>
    [MviCommand(typeof(RuntimeIntent.StartRuntime))]
    public partial IMviAsyncCommand StartRuntimeCommand { get; private set; }

    /// <summary>
    /// 获取停止 Runtime 命令。
    /// </summary>
    [MviCommand(typeof(RuntimeIntent.StopRuntime))]
    public partial IMviAsyncCommand StopRuntimeCommand { get; private set; }

    /// <summary>
    /// 获取重启 Runtime 命令（Running / Failed 可用）。
    /// </summary>
    [MviCommand(typeof(RuntimeIntent.RestartRuntime))]
    public partial IMviAsyncCommand RestartRuntimeCommand { get; private set; }

    /// <summary>
    /// 获取禁用插件后恢复 Runtime 命令（Failed 可用）。
    /// </summary>
    [MviCommand(typeof(RuntimeIntent.RecoverRuntime))]
    public partial IMviAsyncCommand RecoverRuntimeCommand { get; private set; }

    /// <summary>
    /// 获取进入安全模式命令。
    /// </summary>
    [MviCommand(typeof(RuntimeIntent.EnterSafeMode))]
    public partial IMviAsyncCommand EnterSafeModeCommand { get; private set; }

    /// <summary>
    /// 获取退出安全模式命令。
    /// </summary>
    [MviCommand(typeof(RuntimeIntent.ExitSafeMode))]
    public partial IMviAsyncCommand ExitSafeModeCommand { get; private set; }
}
