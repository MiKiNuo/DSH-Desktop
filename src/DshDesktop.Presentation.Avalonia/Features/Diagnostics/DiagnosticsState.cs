using DshDesktop.Domain.Diagnostics;
using MiKiNuo.Mvi.Domain.MVI.State;

namespace DshDesktop.Presentation.Avalonia.Features.Diagnostics;

/// <summary>
/// 表示 Diagnostics 状态（§25：UI Store 只保留展示窗口，上限 1000 条）。
/// </summary>
/// <param name="Entries">当前展示的诊断事件（新→旧追加序，超上限从头截断）。</param>
public sealed record DiagnosticsState(
    IReadOnlyList<DiagnosticEvent> Entries) : IMviState
{
    /// <summary>
    /// 获取初始状态。
    /// </summary>
    public static DiagnosticsState Initial { get; } =
        new(System.Array.Empty<DiagnosticEvent>());
}
