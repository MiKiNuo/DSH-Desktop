using DshDesktop.Domain.Runtime;
using DshDesktop.Presentation.Avalonia.Features.Updates;
using MiKiNuo.Mvi.Application.MVI.Command;
using MiKiNuo.Mvi.Application.MVI.Store;
using MiKiNuo.Mvi.Application.MVI.Threading;
using MiKiNuo.Mvi.Application.MVI.ViewModel;
using MiKiNuo.Mvi.Domain.MVI.Binding;

namespace DshDesktop.Presentation.Avalonia.Features.Runtime;

/// <summary>
/// 表示 Runtime ViewModel（State 投影 + 命令，不持有业务真实状态）。
/// Phase 8 Issue 04：六态 pills / PID·URL 副文案 / 运行环境 KV / 三策略开关的展示模型；
/// DSH 版本经 <see cref="MviViewModelBase{TState, TIntent, TEffect}.BindSiblingState"/>
/// 只读投影 Updates Store（§11.2，同 Dashboard 先例）。
/// </summary>
public sealed partial class RuntimeViewModel
    : MviViewModelBase<RuntimeState, RuntimeIntent, RuntimeEffect>
{
    /// <summary>
    /// 初始化 Runtime ViewModel。
    /// </summary>
    /// <param name="store">Runtime 状态存储。</param>
    /// <param name="updatesStore">Updates 状态存储（兄弟 Store，只读订阅 DSH 版本投影）。</param>
    /// <param name="uiDispatcher">UI 调度器。</param>
    public RuntimeViewModel(
        IMviStore<RuntimeState, RuntimeIntent, RuntimeEffect> store,
        IMviStore<UpdatesState, UpdatesIntent, UpdatesEffect> updatesStore,
        IMviUiDispatcher? uiDispatcher = null)
        : base(store, uiDispatcher)
    {
        ArgumentNullException.ThrowIfNull(updatesStore);

        _ = BindSiblingState(updatesStore, ApplyUpdatesState);
        ApplyUpdatesState(updatesStore.CurrentState);

        // 派生投影跟随状态属性联动刷新（同 Dashboard 先例）。
        PropertyChanged += (_, args) =>
        {
            switch (args.PropertyName)
            {
                case nameof(Lifecycle):
                case nameof(ProcessId):
                case nameof(Url):
                case nameof(LastError):
                    OnPropertyChanged(nameof(StatusSubtitle));
                    break;
                case nameof(Environment):
                    OnPropertyChanged(nameof(NodeVersionText));
                    OnPropertyChanged(nameof(WebView2VersionText));
                    OnPropertyChanged(nameof(DshHomeText));
                    OnPropertyChanged(nameof(ProfileNameText));
                    break;
            }
        };
    }

    /// <summary>
    /// 获取 Runtime 生命周期状态。
    /// </summary>
    [MviBind(nameof(RuntimeState.Lifecycle), BindingMode = MviBindingMode.OneWay)]
    public partial RuntimeLifecycle Lifecycle { get; private set; }

    /// <summary>
    /// 获取 DSH 进程 ID（未运行为 null）。
    /// </summary>
    [MviBind(nameof(RuntimeState.ProcessId), BindingMode = MviBindingMode.OneWay)]
    public partial int? ProcessId { get; private set; }

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
    /// 获取运行环境信息（KV 卡数据源；未加载为 null）。
    /// </summary>
    [MviBind(nameof(RuntimeState.Environment), BindingMode = MviBindingMode.OneWay)]
    public partial RuntimeEnvironmentInfo? Environment { get; private set; }

    /// <summary>
    /// 获取当前 DSH 版本投影（未知为 null）。
    /// </summary>
    [MviBind(nameof(RuntimeState.DshVersion), BindingMode = MviBindingMode.OneWay)]
    public partial string? DshVersion { get; private set; }

    /// <summary>
    /// 获取"关闭窗口后保持 DSH Runtime"开关（ADR-0005）。
    /// </summary>
    [MviBind(nameof(RuntimeState.KeepRuntimeOnClose), BindingMode = MviBindingMode.OneWay)]
    public partial bool KeepRuntimeOnClose { get; private set; }

    /// <summary>
    /// 获取"异常启动自动进入安全模式"开关（ADR-0004 修订注）。
    /// </summary>
    [MviBind(nameof(RuntimeState.AutoSafeModeOnFailure), BindingMode = MviBindingMode.OneWay)]
    public partial bool AutoSafeModeOnFailure { get; private set; }

    /// <summary>
    /// 获取"启动时检查网络更新"开关（§34 修订注）。
    /// </summary>
    [MviBind(nameof(RuntimeState.CheckUpdatesOnStartup), BindingMode = MviBindingMode.OneWay)]
    public partial bool CheckUpdatesOnStartup { get; private set; }

    // ===== 派生投影（纯函数推导） =====

    /// <summary>获取状态行副文案（原型：PID 16428 · http://127.0.0.1:3080；不含 token）。</summary>
    public string StatusSubtitle
    {
        get
        {
            if (Lifecycle is RuntimeLifecycle.Failed)
            {
                return LastError ?? "启动失败";
            }

            if (ProcessId is { } pid && Url is { } url)
            {
                // Session URL 含一次性 token，副文案只显示基址。
                string baseUrl = url.Split('?')[0];
                return $"PID {pid} · {baseUrl}";
            }

            return Lifecycle switch
            {
                RuntimeLifecycle.Starting => "启动中…",
                RuntimeLifecycle.Stopping => "停止中…",
                RuntimeLifecycle.Recovering => "恢复中…",
                _ => "未运行",
            };
        }
    }

    /// <summary>获取 Node 运行时版本文本。</summary>
    public string NodeVersionText => Environment?.NodeVersion ?? "—";

    /// <summary>获取 WebView2 版本文本（未安装显示"未安装"）。</summary>
    public string WebView2VersionText => Environment?.WebView2Version ?? "未安装";

    /// <summary>获取 DSH_HOME 文本。</summary>
    public string DshHomeText => Environment?.DshHome ?? "—";

    /// <summary>获取 Profile 名文本。</summary>
    public string ProfileNameText => Environment?.ProfileName ?? "—";

    /// <summary>获取 Desktop 版本（编译期常量）。</summary>
    public string DesktopVersion => DesktopInfo.Version;

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

    /// <summary>
    /// 获取切换"关闭窗口后保持 DSH Runtime"命令。
    /// </summary>
    [MviCommand(typeof(RuntimeIntent.ToggleKeepRuntimeOnClose))]
    public partial IMviAsyncCommand ToggleKeepRuntimeOnCloseCommand { get; private set; }

    /// <summary>
    /// 获取切换"异常启动自动进入安全模式"命令。
    /// </summary>
    [MviCommand(typeof(RuntimeIntent.ToggleAutoSafeModeOnFailure))]
    public partial IMviAsyncCommand ToggleAutoSafeModeOnFailureCommand { get; private set; }

    /// <summary>
    /// 获取切换"启动时检查网络更新"命令。
    /// </summary>
    [MviCommand(typeof(RuntimeIntent.ToggleCheckUpdatesOnStartup))]
    public partial IMviAsyncCommand ToggleCheckUpdatesOnStartupCommand { get; private set; }

    private void ApplyUpdatesState(UpdatesState updatesState)
    {
        if (updatesState.CurrentDshVersion != Store.CurrentState.DshVersion)
        {
            _ = DispatchAsync(new RuntimeIntent.DshVersionChanged(updatesState.CurrentDshVersion));
        }
    }
}
