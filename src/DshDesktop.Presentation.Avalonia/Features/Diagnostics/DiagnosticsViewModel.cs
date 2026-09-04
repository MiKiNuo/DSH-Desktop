using DshDesktop.Domain.Diagnostics;
using MiKiNuo.Mvi.Application.MVI.Store;
using MiKiNuo.Mvi.Application.MVI.Threading;
using MiKiNuo.Mvi.Application.MVI.ViewModel;
using MiKiNuo.Mvi.Domain.MVI.Binding;
using MiKiNuo.Mvi.Domain.MVI.Effect;

namespace DshDesktop.Presentation.Avalonia.Features.Diagnostics;

/// <summary>
/// 表示 Diagnostics ViewModel。
/// </summary>
public sealed partial class DiagnosticsViewModel
    : MviViewModelBase<DiagnosticsState, DiagnosticsIntent, UnitEffect>
{
    /// <summary>
    /// 初始化 Diagnostics ViewModel。
    /// </summary>
    /// <param name="store">Diagnostics 状态存储。</param>
    /// <param name="uiDispatcher">UI 调度器。</param>
    public DiagnosticsViewModel(
        IMviStore<DiagnosticsState, DiagnosticsIntent, UnitEffect> store,
        IMviUiDispatcher? uiDispatcher = null)
        : base(store, uiDispatcher)
    {
    }

    /// <summary>
    /// 获取当前展示的诊断事件列表。
    /// </summary>
    [MviBind(nameof(DiagnosticsState.Entries), BindingMode = MviBindingMode.OneWay)]
    public partial IReadOnlyList<DiagnosticEvent> Entries { get; private set; }
}
