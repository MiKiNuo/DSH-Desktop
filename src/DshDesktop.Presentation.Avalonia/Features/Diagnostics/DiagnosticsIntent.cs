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

    /// <summary>
    /// 表示运行诊断意图（Phase 8 Issue 06：编排健康检查序列写诊断流）。
    /// </summary>
    public sealed partial record RunDiagnosis : DiagnosticsIntent;

    /// <summary>
    /// 表示导出诊断包意图。
    /// </summary>
    /// <param name="DestinationPath">目标 zip 绝对路径（View 层保存对话框产出）。</param>
    public sealed partial record ExportDiagnosticsBundle(string DestinationPath) : DiagnosticsIntent;

    /// <summary>
    /// 表示打开日志目录意图。
    /// </summary>
    public sealed partial record OpenLogsDirectory : DiagnosticsIntent;
}
