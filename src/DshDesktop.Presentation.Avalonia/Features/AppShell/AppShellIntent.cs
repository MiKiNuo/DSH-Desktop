using DshDesktop.Domain.Runtime;
using MiKiNuo.Mvi.Domain.MVI.Intent;

namespace DshDesktop.Presentation.Avalonia.Features.AppShell;

/// <summary>
/// 表示应用壳意图。
/// </summary>
public abstract partial record AppShellIntent : IMviIntent
{
    /// <summary>
    /// 表示导航到 Runtime 页意图。
    /// </summary>
    public sealed partial record ShowRuntime : AppShellIntent;

    /// <summary>
    /// 表示导航到 Dashboard 页意图。
    /// </summary>
    public sealed partial record ShowDashboard : AppShellIntent;

    /// <summary>
    /// 表示导航到 Workbench 页意图。
    /// </summary>
    public sealed partial record ShowWorkbench : AppShellIntent;

    /// <summary>
    /// 表示导航到 Diagnostics 页意图。
    /// </summary>
    public sealed partial record ShowDiagnostics : AppShellIntent;

    /// <summary>
    /// 表示导航到 Plugins 页意图。
    /// </summary>
    public sealed partial record ShowPlugins : AppShellIntent;

    /// <summary>
    /// 表示导航到 Updates 页意图。
    /// </summary>
    public sealed partial record ShowUpdates : AppShellIntent;

    /// <summary>
    /// 表示导航到 Settings 页意图。
    /// </summary>
    public sealed partial record ShowSettings : AppShellIntent;

    /// <summary>
    /// 表示 Runtime 生命周期投影变化的回流意图（BindSiblingState 自 RuntimeStore 投影，§11.2）。
    /// </summary>
    /// <param name="Lifecycle">最新 Runtime 生命周期。</param>
    public sealed partial record RuntimeIndicatorChanged(RuntimeLifecycle Lifecycle) : AppShellIntent;

    /// <summary>
    /// 表示可用更新数投影变化的回流意图（BindSiblingState 自 UpdatesStore 投影，§11.2）。
    /// </summary>
    /// <param name="Count">可用更新数。</param>
    public sealed partial record UpdateBadgeChanged(int Count) : AppShellIntent;

    /// <summary>
    /// 表示 Runtime 进程/端口投影变化的回流意图（状态栏 PID / Port；BindSiblingState 自
    /// RuntimeStore 投影，§11.2；未运行均为 null）。
    /// </summary>
    /// <param name="ProcessId">DSH 进程 ID。</param>
    /// <param name="Port">实际监听端口。</param>
    public sealed partial record RuntimeEndpointChanged(int? ProcessId, int? Port) : AppShellIntent;

    /// <summary>
    /// 表示当前 DSH 版本投影变化的回流意图（状态栏 DSH 版本段；BindSiblingState 自
    /// UpdatesStore.CurrentDshVersion 投影，§11.2）。
    /// </summary>
    /// <param name="Version">当前激活的 DSH 版本；未知为 null。</param>
    public sealed partial record DshVersionChanged(string? Version) : AppShellIntent;

    /// <summary>
    /// 表示更新操作进行中投影变化的回流意图（壳全屏遮罩 + 导航锁定；BindSiblingState 自
    /// UpdatesStore.PendingOperation 投影，§11.2；true = 有更新操作未完成）。
    /// </summary>
    /// <param name="InProgress">是否有更新操作进行中。</param>
    public sealed partial record UpdateInProgressChanged(bool InProgress) : AppShellIntent;

    /// <summary>
    /// 表示插件事务进行中投影变化的回流意图（壳全屏遮罩 + 导航锁定的第二个分量；
    /// BindSiblingState 自 PluginsStore.Operation 的非终态阶段，2026-09-17 定位补充）。
    /// </summary>
    /// <param name="InProgress">是否有插件事务尚未到终态。</param>
    public sealed partial record PluginOperationInProgressChanged(bool InProgress) : AppShellIntent;

    /// <summary>
    /// 表示插件安装事务投影变化的回流意图（壳 toast 数据源；BindSiblingState 自
    /// PluginsStore.Operation 投影，2026-09-15 审查 C3：自 MainWindow 直订下沉）。
    /// </summary>
    /// <param name="Operation">最新事务进度；无事务为 null。</param>
    public sealed partial record PluginOperationChanged(DshDesktop.Domain.Plugins.PluginOperation? Operation) : AppShellIntent;

    /// <summary>
    /// 表示更新下载进度与操作描述投影变化的回流意图（更新遮罩的进度条互斥与副标题；
    /// BindSiblingState 自 UpdatesStore 投影，2026-09-15 审查 C3：自 MainWindow 直订下沉）。
    /// </summary>
    /// <param name="Percent">下载进度（0-100）；非 Desktop 下载期间为 null。</param>
    /// <param name="OperationText">进行中的操作描述；空闲为 null。</param>
    public sealed partial record UpdateDownloadChanged(int? Percent, string? OperationText) : AppShellIntent;
}
