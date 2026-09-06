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
    /// 表示切换侧边栏折叠状态意图。
    /// </summary>
    public sealed partial record ToggleSidebar : AppShellIntent;

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
    /// 表示当前 DSH 版本投影变化的回流意图（侧栏 runtime-mini；BindSiblingState 自
    /// UpdatesStore.CurrentDshVersion 投影，§11.2）。
    /// </summary>
    /// <param name="Version">当前激活的 DSH 版本；未知为 null。</param>
    public sealed partial record DshVersionChanged(string? Version) : AppShellIntent;
}
