using MiKiNuo.Mvi.Domain.MVI.State;

namespace DshDesktop.Presentation.Avalonia.Features.Workbench;

/// <summary>
/// 表示 Workbench 状态（架构文档 §21 的 Phase 1 子集：DSH Web UI 视为黑盒）。
/// RuntimeReady / DshUrl 从 Runtime Store 投影（§6：不保存可推导状态），不在此重复。
/// </summary>
/// <param name="CurrentUrl">当前导航地址。</param>
public sealed record WorkbenchState(
    string? CurrentUrl) : IMviState
{
    /// <summary>
    /// 获取初始状态。
    /// </summary>
    public static WorkbenchState Initial { get; } = new((string?)null);
}
