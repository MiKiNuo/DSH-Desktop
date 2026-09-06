using DshDesktop.Domain.Diagnostics;
using MiKiNuo.Mvi.Application.MVI.Reducer;
using MiKiNuo.Mvi.Domain.DI;
using MiKiNuo.Mvi.Domain.MVI.Reducer;

namespace DshDesktop.Presentation.Avalonia.Features.Diagnostics;

/// <summary>
/// 表示 Diagnostics 规约器。纯函数，禁止 IO（§9）。
/// </summary>
[MviFeature]
public sealed partial class DiagnosticsReducer
    : MviReducerBase<DiagnosticsState, DiagnosticsIntent, DiagnosticsEffect>
{
    /// <summary>
    /// UI Store 容量上限（§25：完整日志在 data/logs/ 文件，Store 只保留展示窗口）。
    /// </summary>
    private const int MaxEntries = 1000;

    /// <summary>
    /// 处理诊断事件回流：追加并按上限从头截断。
    /// </summary>
    [MviReduce(typeof(DiagnosticsIntent.DiagnosticEventReceived))]
    private MviReduceResult<DiagnosticsState, DiagnosticsEffect> HandleDiagnosticEventReceived(
        DiagnosticsState state,
        DiagnosticsIntent.DiagnosticEventReceived intent)
    {
        IReadOnlyList<DiagnosticEvent> entries = state.Entries;
        IReadOnlyList<DiagnosticEvent> next = entries.Count >= MaxEntries
            ? [.. entries.Skip(entries.Count - MaxEntries + 1), intent.Event]
            : [.. entries, intent.Event];

        return Unchanged(state with { Entries = next });
    }

    /// <summary>
    /// 处理运行诊断意图（Phase 8 Issue 06；结果经诊断事件流回流，State 不变）。
    /// </summary>
    [MviReduce(typeof(DiagnosticsIntent.RunDiagnosis))]
    private MviReduceResult<DiagnosticsState, DiagnosticsEffect> HandleRunDiagnosis(
        DiagnosticsState state,
        DiagnosticsIntent.RunDiagnosis intent)
    {
        return WithEffect(state, new DiagnosticsEffect.RunDiagnosis());
    }

    /// <summary>
    /// 处理导出诊断包意图。
    /// </summary>
    [MviReduce(typeof(DiagnosticsIntent.ExportDiagnosticsBundle))]
    private MviReduceResult<DiagnosticsState, DiagnosticsEffect> HandleExportDiagnosticsBundle(
        DiagnosticsState state,
        DiagnosticsIntent.ExportDiagnosticsBundle intent)
    {
        return WithEffect(state, new DiagnosticsEffect.ExportBundle(intent.DestinationPath));
    }

    /// <summary>
    /// 处理打开日志目录意图。
    /// </summary>
    [MviReduce(typeof(DiagnosticsIntent.OpenLogsDirectory))]
    private MviReduceResult<DiagnosticsState, DiagnosticsEffect> HandleOpenLogsDirectory(
        DiagnosticsState state,
        DiagnosticsIntent.OpenLogsDirectory intent)
    {
        return WithEffect(state, new DiagnosticsEffect.OpenLogsDirectory());
    }
}
