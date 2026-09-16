using MiKiNuo.Mvi.Domain.MVI.State;

namespace DshDesktop.Presentation.Avalonia.Features.Workbench;

/// <summary>
/// 表示 Workbench 状态（架构文档 §21：DSH Web UI 视为黑盒）。
/// </summary>
/// <remarks>
/// 用户精简掉页内工具条与错误条后，<c>CanGoBack</c> / <c>CanGoForward</c> / <c>Error</c>
/// 失去全部消费方，已整链删除（§21 Phase 6 修订注）。
/// <c>DshUrl</c> 自 Runtime Store 经 <c>RuntimeUrlChanged</c> Intent 回流（2026-09-15 架构审查：
/// 投影落 State 而非 ViewModel 私有字段，Store 才是唯一真源——与 AppShell / Runtime /
/// Plugins / Dashboard 四个兄弟投影先例同构）。
/// </remarks>
/// <param name="CurrentUrl">当前导航地址。</param>
/// <param name="Loading">是否正在加载页面。</param>
/// <param name="DshUrl">DSH Web UI 完整地址（含 token；Runtime 非 Running 时为 null）。</param>
public sealed record WorkbenchState(
    string? CurrentUrl,
    bool Loading,
    string? DshUrl) : IMviState
{
    /// <summary>
    /// 获取初始状态。
    /// </summary>
    public static WorkbenchState Initial { get; } = new((string?)null, false, (string?)null);
}
