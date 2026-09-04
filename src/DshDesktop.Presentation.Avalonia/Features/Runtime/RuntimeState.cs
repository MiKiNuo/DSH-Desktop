using DshDesktop.Domain.Runtime;
using MiKiNuo.Mvi.Domain.MVI.State;

namespace DshDesktop.Presentation.Avalonia.Features.Runtime;

/// <summary>
/// 表示 Runtime Feature 状态（架构文档 §15.1）。
/// </summary>
/// <param name="Lifecycle">Runtime 生命周期状态机。</param>
/// <param name="Health">健康状态（Supervisor 5s HTTP 轮询）。</param>
/// <param name="StartupStage">启动阶段（§17 分级启动链）。</param>
/// <param name="StartupElapsed">本次启动耗时。</param>
/// <param name="Port">实际监听端口（ADR-0001：启动时探测，Running 后才可知）。</param>
/// <param name="Url">Session URL（含 token，仅存内存）。</param>
/// <param name="LastError">最近一次错误信息。</param>
/// <param name="SafeMode">是否处于安全模式（抑制自动启动，仅管理界面）。</param>
public sealed record RuntimeState(
    RuntimeLifecycle Lifecycle,
    RuntimeHealth Health,
    RuntimeStartupStage StartupStage,
    TimeSpan? StartupElapsed,
    int? Port,
    string? Url,
    string? LastError,
    bool SafeMode) : IMviState
{
    /// <summary>
    /// 获取初始状态。
    /// </summary>
    public static RuntimeState Initial { get; } = new(
        RuntimeLifecycle.Stopped,
        RuntimeHealth.Unknown,
        RuntimeStartupStage.None,
        null, null, null, null, false);
}
