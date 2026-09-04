using DshDesktop.Domain.Diagnostics;
using MiKiNuo.Mvi.Application.MVI.Reducer;
using MiKiNuo.Mvi.Domain.DI;
using MiKiNuo.Mvi.Domain.MVI.Effect;
using MiKiNuo.Mvi.Domain.MVI.Reducer;

namespace DshDesktop.Presentation.Avalonia.Features.Diagnostics;

/// <summary>
/// 表示 Diagnostics 规约器。无副作用，Effect 通道使用 <see cref="UnitEffect"/>。
/// </summary>
[MviFeature]
public sealed partial class DiagnosticsReducer
    : MviReducerBase<DiagnosticsState, DiagnosticsIntent, UnitEffect>
{
    /// <summary>
    /// UI Store 容量上限（§25：完整日志在 data/logs/ 文件，Store 只保留展示窗口）。
    /// </summary>
    private const int MaxEntries = 1000;

    /// <summary>
    /// 处理诊断事件回流：追加并按上限从头截断。
    /// </summary>
    [MviReduce(typeof(DiagnosticsIntent.DiagnosticEventReceived))]
    private MviReduceResult<DiagnosticsState, UnitEffect> HandleDiagnosticEventReceived(
        DiagnosticsState state,
        DiagnosticsIntent.DiagnosticEventReceived intent)
    {
        IReadOnlyList<DiagnosticEvent> entries = state.Entries;
        IReadOnlyList<DiagnosticEvent> next = entries.Count >= MaxEntries
            ? [.. entries.Skip(entries.Count - MaxEntries + 1), intent.Event]
            : [.. entries, intent.Event];

        return Unchanged(state with { Entries = next });
    }
}
