using DshDesktop.Domain.Updates;
using MiKiNuo.Mvi.Application.MVI.Command;
using MiKiNuo.Mvi.Application.MVI.Store;
using MiKiNuo.Mvi.Application.MVI.Threading;
using MiKiNuo.Mvi.Application.MVI.ViewModel;
using MiKiNuo.Mvi.Domain.MVI.Binding;

namespace DshDesktop.Presentation.Avalonia.Features.Updates;

/// <summary>
/// 表示 Updates ViewModel。
/// </summary>
public sealed partial class UpdatesViewModel
    : MviViewModelBase<UpdatesState, UpdatesIntent, UpdatesEffect>
{
    /// <summary>
    /// 初始化 Updates ViewModel。
    /// </summary>
    /// <param name="store">Updates 状态存储。</param>
    /// <param name="uiDispatcher">UI 调度器。</param>
    public UpdatesViewModel(
        IMviStore<UpdatesState, UpdatesIntent, UpdatesEffect> store,
        IMviUiDispatcher? uiDispatcher = null)
        : base(store, uiDispatcher)
    {
    }

    /// <summary>
    /// 获取更新检查状态。
    /// </summary>
    [MviBind(nameof(UpdatesState.Status), BindingMode = MviBindingMode.OneWay)]
    public partial UpdateStatus Status { get; private set; }

    /// <summary>
    /// 获取 DSH 更新通道。
    /// </summary>
    [MviBind(nameof(UpdatesState.Channel), BindingMode = MviBindingMode.OneWay)]
    public partial string Channel { get; private set; }

    /// <summary>
    /// 获取当前激活的 DSH 版本。
    /// </summary>
    [MviBind(nameof(UpdatesState.CurrentDshVersion), BindingMode = MviBindingMode.OneWay)]
    public partial string? CurrentDshVersion { get; private set; }

    /// <summary>
    /// 获取通道最新 DSH 版本。
    /// </summary>
    [MviBind(nameof(UpdatesState.LatestDshVersion), BindingMode = MviBindingMode.OneWay)]
    public partial string? LatestDshVersion { get; private set; }

    /// <summary>
    /// 获取可用 Runtime 列表。
    /// </summary>
    [MviBind(nameof(UpdatesState.Runtimes), BindingMode = MviBindingMode.OneWay)]
    public partial IReadOnlyList<DshRuntimeInfo> Runtimes { get; private set; }

    /// <summary>
    /// 获取可更新插件列表。
    /// </summary>
    [MviBind(nameof(UpdatesState.PluginUpdates), BindingMode = MviBindingMode.OneWay)]
    public partial IReadOnlyList<PluginUpdateInfo> PluginUpdates { get; private set; }

    /// <summary>
    /// 获取进行中的操作描述。
    /// </summary>
    [MviBind(nameof(UpdatesState.PendingOperation), BindingMode = MviBindingMode.OneWay)]
    public partial string? PendingOperation { get; private set; }

    /// <summary>
    /// 获取最近一次错误信息。
    /// </summary>
    [MviBind(nameof(UpdatesState.LastError), BindingMode = MviBindingMode.OneWay)]
    public partial string? LastError { get; private set; }

    /// <summary>
    /// 获取检查更新命令。
    /// </summary>
    [MviCommand(typeof(UpdatesIntent.CheckUpdates))]
    public partial IMviAsyncCommand CheckUpdatesCommand { get; private set; }

    /// <summary>
    /// 获取安装 DSH Runtime 命令（载荷：版本号）。
    /// </summary>
    [MviCommand(typeof(UpdatesIntent.InstallDshRuntime), PayloadType = typeof(string))]
    public partial IMviAsyncCommand InstallDshRuntimeCommand { get; private set; }

    /// <summary>
    /// 获取激活 Runtime 命令（载荷：版本目录名，空字符串 = 借用）。
    /// </summary>
    [MviCommand(typeof(UpdatesIntent.ActivateDshRuntime), PayloadType = typeof(string))]
    public partial IMviAsyncCommand ActivateDshRuntimeCommand { get; private set; }

    /// <summary>
    /// 获取更新插件命令（载荷：插件包名）。
    /// </summary>
    [MviCommand(typeof(UpdatesIntent.UpdatePlugin), PayloadType = typeof(string))]
    public partial IMviAsyncCommand UpdatePluginCommand { get; private set; }

    /// <summary>
    /// 获取 Desktop 当前版本（编译期常量，非状态；§50 三套版本展示，最新版 5b 接通）。
    /// </summary>
    public string DesktopVersion => DesktopInfo.Version;
}
