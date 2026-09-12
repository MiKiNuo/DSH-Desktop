using MiKiNuo.Mvi.Domain.MVI.Intent;

namespace DshDesktop.Presentation.Avalonia.Features.Workbench;

/// <summary>
/// 表示 Workbench 意图（架构文档 §21；导航事件为 WebView 回流意图）。
/// </summary>
/// <remarks>
/// 用户移除页内工具条后，<c>NavigateBack</c> / <c>NavigateForward</c> / <c>Reload</c> /
/// <c>NavigationFailed</c> 失去唯一发起者，已整链删除（§21 Phase 6 修订注）——
/// 这里只剩导航回流的两端：开始与完成。
/// </remarks>
public abstract partial record WorkbenchIntent : IMviIntent
{
    /// <summary>
    /// 表示导航开始意图。
    /// </summary>
    /// <param name="Url">目标地址。</param>
    public sealed partial record NavigationStarted(string Url) : WorkbenchIntent;

    /// <summary>
    /// 表示导航完成意图。成功与失败共用：失败不再有页内错误条承载，
    /// 但两者都必须结束 <c>Loading</c>，否则加载条会永久悬停。
    /// </summary>
    /// <param name="Url">完成地址。</param>
    public sealed partial record NavigationCompleted(string Url) : WorkbenchIntent;
}
