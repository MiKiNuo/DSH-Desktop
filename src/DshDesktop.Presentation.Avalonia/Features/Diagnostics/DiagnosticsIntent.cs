using DshDesktop.Domain.Diagnostics;
using MiKiNuo.Mvi.Domain.MVI.Intent;

namespace DshDesktop.Presentation.Avalonia.Features.Diagnostics;

/// <summary>
/// 表示 Diagnostics 意图。
/// </summary>
public abstract partial record DiagnosticsIntent : IMviIntent
{
    /// <summary>
    /// 表示收到一条诊断事件的回流意图。
    /// </summary>
    /// <param name="Event">诊断事件。</param>
    public sealed partial record DiagnosticEventReceived(DiagnosticEvent Event) : DiagnosticsIntent;
}
