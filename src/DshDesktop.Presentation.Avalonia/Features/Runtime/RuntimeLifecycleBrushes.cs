using Avalonia.Media;
using DshDesktop.Domain.Runtime;

namespace DshDesktop.Presentation.Avalonia.Features.Runtime;

/// <summary>
/// 表示 Runtime 生命周期状态点的共享颜色映射（侧栏指示与 Runtime 页阶段指示同色系）。
/// </summary>
public static class RuntimeLifecycleBrushes
{
    /// <summary>
    /// 已停止 / 待定（灰）。
    /// </summary>
    public static readonly IBrush Stopped = new SolidColorBrush(Color.Parse("#6B7280"));

    /// <summary>
    /// 过渡中（黄）：Starting / Stopping / Recovering，亦用于进行中的启动阶段。
    /// </summary>
    public static readonly IBrush Transition = new SolidColorBrush(Color.Parse("#EAB308"));

    /// <summary>
    /// 运行中 / 健康 / 阶段完成（绿）。
    /// </summary>
    public static readonly IBrush Running = new SolidColorBrush(Color.Parse("#22C55E"));

    /// <summary>
    /// 失败 / 无响应（红）。
    /// </summary>
    public static readonly IBrush Failed = new SolidColorBrush(Color.Parse("#EF4444"));

    /// <summary>
    /// 按生命周期取状态点画刷。
    /// </summary>
    /// <param name="lifecycle">Runtime 生命周期。</param>
    /// <returns>对应的状态点画刷。</returns>
    public static IBrush For(RuntimeLifecycle lifecycle)
    {
        return lifecycle switch
        {
            RuntimeLifecycle.Running => Running,
            RuntimeLifecycle.Starting or RuntimeLifecycle.Stopping or RuntimeLifecycle.Recovering => Transition,
            RuntimeLifecycle.Failed => Failed,
            _ => Stopped,
        };
    }
}
