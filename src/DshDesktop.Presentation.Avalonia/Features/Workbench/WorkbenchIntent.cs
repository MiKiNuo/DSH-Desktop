using MiKiNuo.Mvi.Domain.MVI.Intent;

namespace DshDesktop.Presentation.Avalonia.Features.Workbench;

/// <summary>
/// 表示 Workbench 意图（架构文档 §21；导航事件为 WebView 回流意图）。
/// </summary>
public abstract partial record WorkbenchIntent : IMviIntent
{
    /// <summary>
    /// 表示后退意图（走 WebView 内部历史，不触碰 Session URL）。
    /// </summary>
    public sealed partial record NavigateBack : WorkbenchIntent;

    /// <summary>
    /// 表示前进意图（走 WebView 内部历史，不触碰 Session URL）。
    /// </summary>
    public sealed partial record NavigateForward : WorkbenchIntent;

    /// <summary>
    /// 表示刷新意图（从 Runtime 投影取最新 Session URL 再导航，禁止缓存旧 URL）。
    /// </summary>
    public sealed partial record Reload : WorkbenchIntent;

    /// <summary>
    /// 表示导航开始意图。
    /// </summary>
    /// <param name="Url">目标地址。</param>
    public sealed partial record NavigationStarted(string Url) : WorkbenchIntent;

    /// <summary>
    /// 表示导航完成意图。
    /// </summary>
    /// <param name="Url">完成地址。</param>
    /// <param name="CanGoBack">完成时 WebView 是否可后退。</param>
    /// <param name="CanGoForward">完成时 WebView 是否可前进。</param>
    public sealed partial record NavigationCompleted(
        string Url,
        bool CanGoBack,
        bool CanGoForward) : WorkbenchIntent;

    /// <summary>
    /// 表示导航失败意图。
    /// </summary>
    /// <param name="Error">错误信息。</param>
    public sealed partial record NavigationFailed(string Error) : WorkbenchIntent;
}
