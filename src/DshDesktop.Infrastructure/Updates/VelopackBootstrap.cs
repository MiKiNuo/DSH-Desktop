using Velopack;

namespace DshDesktop.Infrastructure.Updates;

/// <summary>
/// Velopack 入口引导（ADR-0003）：必须在进程最早处执行，处理安装/更新/卸载钩子。
/// 未安装形态下为空操作。
/// </summary>
public static class VelopackBootstrap
{
    /// <summary>
    /// 执行 Velopack 入口钩子。
    /// </summary>
    public static void Run()
    {
        VelopackApp.Build().Run();
    }
}
