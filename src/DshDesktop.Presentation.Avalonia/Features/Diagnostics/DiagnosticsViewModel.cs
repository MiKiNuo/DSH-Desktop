using DshDesktop.Domain.Diagnostics;
using MiKiNuo.Mvi.Application.MVI.Command;
using MiKiNuo.Mvi.Application.MVI.Store;
using MiKiNuo.Mvi.Application.MVI.Threading;
using MiKiNuo.Mvi.Application.MVI.ViewModel;
using MiKiNuo.Mvi.Domain.MVI.Binding;

namespace DshDesktop.Presentation.Avalonia.Features.Diagnostics;

/// <summary>
/// 表示 Diagnostics ViewModel。
/// </summary>
public sealed partial class DiagnosticsViewModel
    : MviViewModelBase<DiagnosticsState, DiagnosticsIntent, DiagnosticsEffect>
{
    /// <summary>
    /// 初始化 Diagnostics ViewModel。
    /// </summary>
    /// <param name="store">Diagnostics 状态存储。</param>
    /// <param name="uiDispatcher">UI 调度器。</param>
    public DiagnosticsViewModel(
        IMviStore<DiagnosticsState, DiagnosticsIntent, DiagnosticsEffect> store,
        IMviUiDispatcher? uiDispatcher = null)
        : base(store, uiDispatcher)
    {
    }

    /// <summary>
    /// 获取当前展示的诊断事件列表。
    /// </summary>
    [MviBind(nameof(DiagnosticsState.Entries), BindingMode = MviBindingMode.OneWay)]
    public partial IReadOnlyList<DiagnosticEvent> Entries { get; private set; }

    /// <summary>
    /// 获取运行诊断命令（Phase 8 Issue 06）。
    /// </summary>
    [MviCommand(typeof(DiagnosticsIntent.RunDiagnosis))]
    public partial IMviAsyncCommand RunDiagnosisCommand { get; private set; }

    /// <summary>
    /// 获取导出诊断包命令（载荷：目标 zip 绝对路径）。
    /// </summary>
    [MviCommand(typeof(DiagnosticsIntent.ExportDiagnosticsBundle), PayloadType = typeof(string))]
    public partial IMviAsyncCommand ExportDiagnosticsBundleCommand { get; private set; }

    /// <summary>
    /// 获取打开日志目录命令。
    /// </summary>
    [MviCommand(typeof(DiagnosticsIntent.OpenLogsDirectory))]
    public partial IMviAsyncCommand OpenLogsDirectoryCommand { get; private set; }
}
