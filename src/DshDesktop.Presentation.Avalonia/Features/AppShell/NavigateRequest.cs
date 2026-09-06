using MiKiNuo.Mvi.Domain.MVI.Mediator;

namespace DshDesktop.Presentation.Avalonia.Features.AppShell;

/// <summary>
/// 表示跨 Feature 导航请求（§28 Mediator：Request / Response，禁止 Store 直依赖）。
/// 由组合根路由到 AppShell Store 的导航意图（Phase 8 Issue 03：Dashboard hero / timeline 按钮）。
/// </summary>
/// <param name="Page">目标页面。</param>
public sealed record NavigateRequest(ShellPage Page) : IMviRequest<bool>;
