using MiKiNuo.Mvi.Domain.MVI.State;

namespace DshDesktop.Presentation.Avalonia.Features.Workbench;

/// <summary>
/// 表示 Workbench 状态（架构文档 §21：DSH Web UI 视为黑盒）。
/// RuntimeReady / SessionUrl 从 Runtime Store 投影（§6：不保存可推导状态），不在此重复。
/// </summary>
/// <remarks>
/// 用户精简掉页内工具条与错误条后，<c>CanGoBack</c> / <c>CanGoForward</c> / <c>Error</c>
/// 失去全部消费方，已整链删除（§21 Phase 6 修订注）。
/// </remarks>
/// <param name="CurrentUrl">当前导航地址。</param>
/// <param name="Loading">是否正在加载页面。</param>
public sealed record WorkbenchState(
    string? CurrentUrl,
    bool Loading) : IMviState
{
    /// <summary>
    /// 获取初始状态。
    /// </summary>
    public static WorkbenchState Initial { get; } = new((string?)null, false);
}
