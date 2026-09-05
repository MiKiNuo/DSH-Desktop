using Avalonia;
using DshDesktop.Domain.Common;
using DshDesktop.Infrastructure.Updates;

namespace DshDesktop.App;

/// <summary>
/// 表示 Avalonia 应用程序入口。
/// </summary>
internal static class Program
{
    /// <summary>
    /// 启动应用程序。
    /// </summary>
    /// <param name="args">命令行参数。</param>
    [STAThread]
    public static void Main(string[] args)
    {
        // Velopack 安装/更新钩子必须在进程最早处执行（ADR-0003）。
        VelopackBootstrap.Run();

        // 静态初始化是惰性的：入口即触发，让 §46 计时从进程入口起跑。
        _ = StartupTimer.SinceProcessStart;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// 构建 Avalonia 应用程序。
    /// </summary>
    /// <returns>Avalonia 应用构建器。</returns>
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder
            .Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}
