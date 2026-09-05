using MiKiNuo.Mvi.Domain.MVI.State;

namespace DshDesktop.Presentation.Avalonia.Features.Workbench;

/// <summary>
/// 表示 Workbench 状态（架构文档 §21：DSH Web UI 视为黑盒）。
/// RuntimeReady / SessionUrl 从 Runtime Store 投影（§6：不保存可推导状态），不在此重复。
/// </summary>
/// <param name="CurrentUrl">当前导航地址。</param>
/// <param name="CanGoBack">是否可后退（WebView 内部历史，导航完成时回流）。</param>
/// <param name="CanGoForward">是否可前进（WebView 内部历史，导航完成时回流）。</param>
/// <param name="Loading">是否正在加载页面。</param>
/// <param name="Error">最近一次导航错误信息（非空时展示页内错误条）。</param>
public sealed record WorkbenchState(
    string? CurrentUrl,
    bool CanGoBack,
    bool CanGoForward,
    bool Loading,
    string? Error) : IMviState
{
    /// <summary>
    /// 获取初始状态。
    /// </summary>
    public static WorkbenchState Initial { get; } = new((string?)null, false, false, false, null);
}
