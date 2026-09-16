using DshDesktop.Domain.Plugins;
using DshDesktop.Domain.Runtime;
using MiKiNuo.Mvi.Domain.MVI.State;

namespace DshDesktop.Presentation.Avalonia.Features.AppShell;

/// <summary>
/// 表示应用壳状态（架构文档 §14：只管理 Desktop Shell，不保存业务数据）。
/// </summary>
/// <param name="CurrentPage">当前页面。</param>
/// <param name="RuntimeIndicator">Runtime 生命周期投影（BindSiblingState 自 RuntimeStore，§11.2）。</param>
/// <param name="UpdateBadge">可用更新数投影（BindSiblingState 自 UpdatesStore，§11.2；0 表示无可用更新）。</param>
/// <param name="RuntimeProcessId">DSH 进程 ID 投影（状态栏 PID；未运行为 null，Phase 8 Issue 02）。</param>
/// <param name="RuntimePort">实际监听端口投影（状态栏 Port；未运行为 null，Phase 8 Issue 02）。</param>
/// <param name="DshVersion">当前 DSH 版本投影（状态栏 DSH 版本段；自 UpdatesStore.CurrentDshVersion，未知为 null）。</param>
/// <param name="UpdateInProgress">是否有更新操作进行中投影（BindSiblingState 自 UpdatesStore.PendingOperation，§11.2；true 时壳显示全屏遮罩并锁定导航）。</param>
/// <param name="PluginOperation">插件安装事务投影（BindSiblingState 自 PluginsStore.Operation；壳 toast 数据源，2026-09-15 审查 C3：自 MainWindow 直订下沉）。</param>
/// <param name="UpdateDownloadPercent">Desktop 更新下载进度投影（自 UpdatesStore.DesktopDownloadProgress；遮罩「旋转图标 ↔ 确定进度条」互斥依据）。</param>
/// <param name="UpdateOperationText">进行中更新操作描述投影（自 UpdatesStore.PendingOperation；遮罩副标题）。</param>
public sealed record AppShellState(
    ShellPage CurrentPage,
    RuntimeLifecycle RuntimeIndicator,
    int UpdateBadge,
    int? RuntimeProcessId,
    int? RuntimePort,
    string? DshVersion,
    bool UpdateInProgress,
    PluginOperation? PluginOperation,
    int? UpdateDownloadPercent,
    string? UpdateOperationText) : IMviState
{
    /// <summary>
    /// 获取初始状态（顶部导航改造：默认页 = 工作台，即 DSH 官方 Web UI）。
    /// </summary>
    public static AppShellState Initial { get; } = new(
        ShellPage.Workbench, RuntimeLifecycle.Stopped, 0, null, null, null, false, null, null, null);
}
