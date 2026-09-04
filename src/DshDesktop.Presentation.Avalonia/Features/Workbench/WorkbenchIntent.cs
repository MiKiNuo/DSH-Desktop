using MiKiNuo.Mvi.Domain.MVI.Intent;

namespace DshDesktop.Presentation.Avalonia.Features.Workbench;

/// <summary>
/// 表示 Workbench 意图（架构文档 §21 的 Phase 1 子集）。
/// </summary>
public abstract partial record WorkbenchIntent : IMviIntent
{
    /// <summary>
    /// 表示导航开始意图。
    /// </summary>
    /// <param name="Url">目标地址。</param>
    public sealed partial record NavigationStarted(string Url) : WorkbenchIntent;

    /// <summary>
    /// 表示导航完成意图。
    /// </summary>
    /// <param name="Url">完成地址。</param>
    public sealed partial record NavigationCompleted(string Url) : WorkbenchIntent;

    /// <summary>
    /// 表示导航失败意图。
    /// </summary>
    /// <param name="Error">错误信息。</param>
    public sealed partial record NavigationFailed(string Error) : WorkbenchIntent;
}
