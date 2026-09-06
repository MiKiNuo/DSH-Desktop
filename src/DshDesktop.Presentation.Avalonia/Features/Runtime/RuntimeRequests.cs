using DshDesktop.Domain.Runtime;
using MiKiNuo.Mvi.Domain.MVI.Mediator;

namespace DshDesktop.Presentation.Avalonia.Features.Runtime;

/// <summary>
/// 表示启动 Runtime 的跨层请求（§28 Mediator：Request / Response，禁止 Store 直依赖）。
/// 由组合根（App）路由到 IRuntimeSupervisor，Presentation 不引用 Infrastructure。
/// </summary>
public sealed record StartRuntimeRequest : IMviRequest<RuntimeSnapshot>;

/// <summary>
/// 表示停止 Runtime 的跨层请求。
/// </summary>
public sealed record StopRuntimeRequest : IMviRequest<bool>;

/// <summary>
/// 表示重启 Runtime 的跨层请求（ADR-0004：Stop+Start 原子编排）。
/// </summary>
public sealed record RestartRuntimeRequest : IMviRequest<RuntimeSnapshot>;

/// <summary>
/// 表示持久化安全模式状态的跨层请求。
/// </summary>
/// <param name="Enabled">目标安全模式状态。</param>
public sealed record SetSafeModeRequest(bool Enabled) : IMviRequest<bool>;

/// <summary>
/// 表示持久化"关闭窗口后保持 DSH Runtime"开关的跨层请求（ADR-0005）。
/// </summary>
/// <param name="Enabled">目标开关状态。</param>
public sealed record SetKeepRuntimeOnCloseRequest(bool Enabled) : IMviRequest<bool>;

/// <summary>
/// 表示持久化"异常启动自动进入安全模式"开关的跨层请求（ADR-0004 修订注）。
/// </summary>
/// <param name="Enabled">目标开关状态。</param>
public sealed record SetAutoSafeModeOnFailureRequest(bool Enabled) : IMviRequest<bool>;

/// <summary>
/// 表示持久化"启动时检查网络更新"开关的跨层请求（§34 修订注）。
/// </summary>
/// <param name="Enabled">目标开关状态。</param>
public sealed record SetCheckUpdatesOnStartupRequest(bool Enabled) : IMviRequest<bool>;
