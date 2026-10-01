using DshDesktop.Domain.Updates;
using DshDesktop.Presentation.Avalonia.Features.Plugins;
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
    private string? _installToActivateVersion;

    /// <summary>请求确认激活本次安装成功的 Runtime。</summary>
    public event EventHandler<string>? ActivationConfirmationRequested;

    /// <summary>安装最新版 Runtime，并在安装成功后请求激活确认。</summary>
    public void InstallLatestRuntime()
    {
        if (LatestDshVersion is { Length: > 0 } latest)
        {
            _installToActivateVersion = latest;
            InstallDshRuntimeCommand.Execute(latest);
        }
    }

    /// <summary>
    /// 初始化 Updates ViewModel。
    /// </summary>
    /// <param name="store">Updates 状态存储。</param>
    /// <param name="pluginsStore">Plugins 状态存储（兄弟 Store，只读订阅；候选 3 后插件更新的在飞与文案改由 PluginsState.Operation 承载，页内反馈须自此处投影，§11.2）。</param>
    /// <param name="uiDispatcher">UI 调度器。</param>
    public UpdatesViewModel(
        IMviStore<UpdatesState, UpdatesIntent, UpdatesEffect> store,
        IMviStore<PluginsState, PluginsIntent, PluginsEffect> pluginsStore,
        IMviUiDispatcher? uiDispatcher = null)
        : base(store, uiDispatcher)
    {
        ArgumentNullException.ThrowIfNull(pluginsStore);

        _ = BindSiblingState(pluginsStore, ApplyPluginsState);
        ApplyPluginsState(pluginsStore.CurrentState);

        // 派生投影跟随状态属性联动刷新（同 Runtime/Dashboard 先例）。
        PropertyChanged += (_, args) =>
        {
            switch (args.PropertyName)
            {
                case nameof(CurrentDshVersion):
                case nameof(LatestDshVersion):
                case nameof(Runtimes):
                    OnPropertyChanged(nameof(DshStage));
                    break;
                case nameof(PendingOperation):
                    OnPropertyChanged(nameof(BusyNotice));
                    break;
                case nameof(Operation):
                    RequestActivationAfterInstall();
                    break;
            }
        };
    }

    /// <summary>
    /// 获取 DSH Runtime 更新卡的展示阶段（派生自状态，视图 badge 与主按钮的唯一判定口径）。
    /// </summary>
    public DshRuntimeStage DshStage => Store.CurrentState.DshStage;

    private void RequestActivationAfterInstall()
    {
        if (Operation is not
            { Kind: UpdatesOperationKind.RuntimeInstall, Phase: not UpdatesOperationPhase.Running } operation
            || !string.Equals(operation.Version, _installToActivateVersion, StringComparison.Ordinal))
        {
            return;
        }

        _installToActivateVersion = null;
        if (operation.Phase is UpdatesOperationPhase.Completed
            && Store.CurrentState.Runtimes.Any(runtime => !runtime.IsActive && !runtime.IsBorrowed
                && string.Equals(runtime.Version, operation.Version, StringComparison.Ordinal)))
        {
            ActivationConfirmationRequested?.Invoke(this, operation.Version);
        }
    }

    /// <summary>
    /// 获取插件事务在飞文本投影（兄弟 Store PluginsState.PendingOperation；候选 3 后插件更新的页内反馈唯一来源）。
    /// </summary>
    public string? PluginOperationText => _pluginOperationText;

    /// <summary>
    /// 获取在飞操作提示：Updates 自有操作优先（Desktop 下载 / Runtime 安装），回落到插件事务文本。
    /// 页内警告块（UpdatesView 顶部）绑定此属性，使插件更新期间恢复页内反馈（🟡1）。
    /// </summary>
    public string? BusyNotice => PendingOperation ?? _pluginOperationText;

    /// <summary>
    /// 最近一次投影的 Plugins 状态在飞文本（页内反馈数据源）。
    /// </summary>
    private string? _pluginOperationText;

    private void ApplyPluginsState(PluginsState pluginsState)
    {
        string? text = pluginsState.PendingOperation;
        if (!string.Equals(text, _pluginOperationText, StringComparison.Ordinal))
        {
            _pluginOperationText = text;
            OnPropertyChanged(nameof(PluginOperationText));
            OnPropertyChanged(nameof(BusyNotice));
        }
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

    /// <summary>获取 Updates 自有操作事实。</summary>
    [MviBind(nameof(UpdatesState.Operation), BindingMode = MviBindingMode.OneWay)]
    public partial UpdatesOperation? Operation { get; private set; }

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
    /// 获取 Desktop 当前版本（编译期常量，非状态；§50 三套版本展示）。
    /// </summary>
    public string DesktopVersion => DesktopInfo.Version;

    /// <summary>
    /// 获取最新 Desktop 版本；无更新或未安装形态为 null。
    /// </summary>
    [MviBind(nameof(UpdatesState.LatestDesktopVersion), BindingMode = MviBindingMode.OneWay)]
    public partial string? LatestDesktopVersion { get; private set; }

    /// <summary>
    /// 获取 Desktop 更新下载进度（0-100）；未在下载为 null。
    /// </summary>
    [MviBind(nameof(UpdatesState.DesktopDownloadProgress), BindingMode = MviBindingMode.OneWay)]
    public partial int? DesktopDownloadProgress { get; private set; }

    /// <summary>
    /// 获取下载并应用 Desktop 更新命令。
    /// </summary>
    [MviCommand(typeof(UpdatesIntent.DownloadAndApplyDesktopUpdate))]
    public partial IMviAsyncCommand DownloadAndApplyDesktopUpdateCommand { get; private set; }
}
