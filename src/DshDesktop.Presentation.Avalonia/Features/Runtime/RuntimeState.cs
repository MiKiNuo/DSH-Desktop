using DshDesktop.Domain.Runtime;
using MiKiNuo.Mvi.Domain.MVI.State;

namespace DshDesktop.Presentation.Avalonia.Features.Runtime;

/// <summary>
/// 表示运行环境信息（Runtime 页"运行环境"KV 卡数据源；初始化时由组合根回流一次）。
/// </summary>
/// <param name="NodeVersion">Node 运行时版本；未探测到为 null。</param>
/// <param name="WebView2Version">WebView2 Runtime 版本；未安装为 null。</param>
/// <param name="DshHome">DSH_HOME 数据根目录。</param>
/// <param name="ProfileName">当前 Profile 名。</param>
public sealed record RuntimeEnvironmentInfo(
    string? NodeVersion,
    string? WebView2Version,
    string? DshHome,
    string? ProfileName);

/// <summary>
/// 表示 Runtime Feature 状态（架构文档 §15.1）。
/// </summary>
/// <param name="Lifecycle">Runtime 生命周期状态机。</param>
/// <param name="Health">健康状态（Supervisor 5s HTTP 轮询）。</param>
/// <param name="StartupStage">启动阶段（§17 分级启动链）。</param>
/// <param name="StartupElapsed">本次启动耗时。</param>
/// <param name="ProcessId">DSH 进程 ID（Phase 8 Issue 02：状态栏 PID 投影源；未运行为 null）。</param>
/// <param name="Port">实际监听端口（ADR-0001：启动时探测，Running 后才可知）。</param>
/// <param name="Url">Session URL（含 token，仅存内存）。</param>
/// <param name="LastError">最近一次错误信息。</param>
/// <param name="SafeMode">是否处于安全模式（抑制自动启动，仅管理界面）。</param>
/// <param name="Environment">运行环境信息（KV 卡；组合根初始化回流）。</param>
/// <param name="DshVersion">当前 DSH 版本投影（自 UpdatesStore.CurrentDshVersion，§11.2；未知为 null）。</param>
/// <param name="KeepRuntimeOnClose">关闭窗口后保持 DSH Runtime（ADR-0005，默认关）。</param>
/// <param name="AutoSafeModeOnFailure">异常启动自动进入安全模式（ADR-0004 修订注，默认开）。</param>
/// <param name="CheckUpdatesOnStartup">启动时检查网络更新（§34 修订注，默认关）。</param>
public sealed record RuntimeState(
    RuntimeLifecycle Lifecycle,
    RuntimeHealth Health,
    RuntimeStartupStage StartupStage,
    TimeSpan? StartupElapsed,
    int? ProcessId,
    int? Port,
    string? Url,
    string? LastError,
    bool SafeMode,
    RuntimeEnvironmentInfo? Environment,
    string? DshVersion,
    bool KeepRuntimeOnClose,
    bool AutoSafeModeOnFailure,
    bool CheckUpdatesOnStartup) : IMviState
{
    /// <summary>
    /// 获取初始状态。
    /// </summary>
    public static RuntimeState Initial { get; } = new(
        RuntimeLifecycle.Stopped,
        RuntimeHealth.Unknown,
        RuntimeStartupStage.None,
        null, null, null, null, null, false,
        null, null, false, true, false);
}
