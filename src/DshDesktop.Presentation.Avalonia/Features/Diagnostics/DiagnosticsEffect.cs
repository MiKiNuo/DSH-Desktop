using MiKiNuo.Mvi.Domain.MVI.Effect;

namespace DshDesktop.Presentation.Avalonia.Features.Diagnostics;

/// <summary>
/// 表示 Diagnostics 副作用（Phase 8 Issue 06：工具条三按钮）。
/// </summary>
public abstract partial record DiagnosticsEffect : IMviEffect
{
    /// <summary>
    /// 表示运行诊断副作用（编排健康检查序列，结果经诊断事件流回流）。
    /// </summary>
    public sealed partial record RunDiagnosis : DiagnosticsEffect;

    /// <summary>
    /// 表示导出诊断包副作用（打包 data/logs 为 zip；目标路径由 View 层保存对话框产出）。
    /// </summary>
    /// <param name="DestinationPath">目标 zip 绝对路径。</param>
    public sealed partial record ExportBundle(string DestinationPath) : DiagnosticsEffect;

    /// <summary>
    /// 表示打开日志目录副作用（组合根经 IPathOpener 端口执行，§4.1）。
    /// </summary>
    public sealed partial record OpenLogsDirectory : DiagnosticsEffect;
}
