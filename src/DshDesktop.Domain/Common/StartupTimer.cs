namespace DshDesktop.Domain.Common;

/// <summary>
/// 表示进程级启动计时器（§46 启动性能指标）：静态只读，进程入口即起跑。
/// 各层只读（Domain 位于最底层，§41 依赖方向）。
/// </summary>
public static class StartupTimer
{
    /// <summary>
    /// 获取自进程入口起的计时。
    /// </summary>
    public static System.Diagnostics.Stopwatch SinceProcessStart { get; } =
        System.Diagnostics.Stopwatch.StartNew();
}
