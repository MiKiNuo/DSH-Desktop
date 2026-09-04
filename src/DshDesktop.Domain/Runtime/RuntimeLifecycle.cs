namespace DshDesktop.Domain.Runtime;

/// <summary>
/// 表示 Runtime 生命周期的明确状态机（CONTEXT.md: Runtime Lifecycle）。
/// 禁止拆分为多个布尔字段。
/// </summary>
public enum RuntimeLifecycle
{
    /// <summary>已停止。</summary>
    Stopped,

    /// <summary>启动中。</summary>
    Starting,

    /// <summary>运行中。</summary>
    Running,

    /// <summary>停止中。</summary>
    Stopping,

    /// <summary>恢复中（安全模式 / 回滚流程）。</summary>
    Recovering,

    /// <summary>启动或运行失败。</summary>
    Failed,
}
