using System.Globalization;
using DshDesktop.Domain.Runtime;

namespace DshDesktop.Presentation.Avalonia.Features.Dashboard;

/// <summary>
/// 表示 Dashboard 文本映射（Phase 8 Issue 03）：hero / 健康度 / 统计卡的纯函数文案推导，
/// 与 View 解耦可测。
/// </summary>
public static class DashboardText
{
    /// <summary>启动耗时对比的持平阈值（毫秒）：差异小于该值视为测量噪声。</summary>
    private const long EvenEpsilonMs = 5;

    /// <summary>
    /// 按生命周期取 hero 标题（原型"DSH 服务已就绪"随 Lifecycle 变化）。
    /// </summary>
    /// <param name="lifecycle">Runtime 生命周期。</param>
    /// <returns>hero 标题。</returns>
    public static string HeroTitle(RuntimeLifecycle lifecycle)
    {
        return lifecycle switch
        {
            RuntimeLifecycle.Running => "DSH 服务已就绪",
            RuntimeLifecycle.Starting => "DSH 服务启动中",
            RuntimeLifecycle.Stopping => "DSH 服务停止中",
            RuntimeLifecycle.Failed => "DSH 服务启动失败",
            RuntimeLifecycle.Recovering => "DSH 服务恢复中",
            _ => "DSH 服务未运行",
        };
    }

    /// <summary>
    /// 按生命周期取 hero 副文案（Running 时含实际地址端口；原型 hero-sub）。
    /// </summary>
    /// <param name="lifecycle">Runtime 生命周期。</param>
    /// <param name="port">实际监听端口；未运行为 null。</param>
    /// <returns>hero 副文案。</returns>
    public static string HeroSubtitle(RuntimeLifecycle lifecycle, int? port)
    {
        return lifecycle switch
        {
            RuntimeLifecycle.Running when port is { } p => string.Create(
                CultureInfo.InvariantCulture,
                $"本地 Runtime 已完成启动，当前 Web 服务运行在 127.0.0.1:{p}。启动链路完全使用本地资源，不依赖 npm、GitHub 或网络检查。"),
            RuntimeLifecycle.Running => "本地 Runtime 已完成启动，正在等待端口探测结果。",
            RuntimeLifecycle.Starting => "正在启动本地 Runtime，就绪后可直接进入工作台。",
            RuntimeLifecycle.Stopping => "正在停止 Runtime…",
            RuntimeLifecycle.Failed => "Runtime 启动失败或意外退出，请前往诊断中心查看日志。",
            RuntimeLifecycle.Recovering => "正在恢复 Runtime（禁用第三方插件后重试）…",
            _ => "Runtime 未运行。",
        };
    }

    /// <summary>
    /// 按健康状态取运行健康度卡状态文本（原型 "● 正常"）。
    /// </summary>
    /// <param name="health">健康状态投影。</param>
    /// <returns>状态文本。</returns>
    public static string HealthText(RuntimeHealth health)
    {
        return health switch
        {
            RuntimeHealth.Healthy => "● 正常",
            RuntimeHealth.Unresponsive => "● 无响应",
            _ => "● 未知",
        };
    }

    /// <summary>
    /// 格式化启动耗时对比（原型"比上次快 0.31s"；Phase 8 Issue 03，config 持久化上次值）。
    /// </summary>
    /// <param name="current">本次启动耗时；未知为 null。</param>
    /// <param name="previousMs">上次启动耗时（毫秒）；首次为 null。</param>
    /// <returns>对比文本。</returns>
    public static string FormatStartupComparison(TimeSpan? current, long? previousMs)
    {
        if (current is null)
        {
            return "—";
        }

        if (previousMs is null)
        {
            return "首次记录启动耗时";
        }

        double diffSeconds = (current.Value.TotalMilliseconds - previousMs.Value) / 1000.0;
        if (Math.Abs(current.Value.TotalMilliseconds - previousMs.Value) < EvenEpsilonMs)
        {
            return "与上次持平";
        }

        return diffSeconds < 0
            ? string.Create(CultureInfo.InvariantCulture, $"比上次快 {-diffSeconds:0.00}s")
            : string.Create(CultureInfo.InvariantCulture, $"比上次慢 {diffSeconds:0.00}s");
    }

    /// <summary>
    /// 格式化 Desktop 通道 footer（原型 "Beta Channel"：首字母大写 + Channel 后缀）。
    /// </summary>
    /// <param name="channel">通道名（config DesktopChannel）。</param>
    /// <returns>footer 文本。</returns>
    public static string ChannelFooter(string channel)
    {
        if (string.IsNullOrWhiteSpace(channel))
        {
            return "—";
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{char.ToUpperInvariant(channel[0])}{channel[1..]} Channel");
    }

    /// <summary>
    /// 格式化工作集内存（原型 "412M"；二进制单位取整）。
    /// </summary>
    /// <param name="bytes">字节数。</param>
    /// <returns>格式化文本。</returns>
    public static string FormatMemoryBytes(long bytes)
    {
        const long kib = 1024;
        const long mib = 1024 * kib;
        const long gib = 1024 * mib;
        if (bytes >= gib)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)gib:0.0}G");
        }

        if (bytes >= mib)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{bytes / mib}M");
        }

        if (bytes >= kib)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{bytes / kib}K");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{bytes}B");
    }

    /// <summary>
    /// 格式化 CPU 百分比（原型 "18%"；无基线为占位符）。
    /// </summary>
    /// <param name="cpuPercent">CPU 百分比；无基线为 null。</param>
    /// <returns>格式化文本。</returns>
    public static string FormatCpu(double? cpuPercent)
    {
        return cpuPercent is { } cpu
            ? string.Create(CultureInfo.InvariantCulture, $"{cpu:0}%")
            : "—";
    }

    /// <summary>
    /// 格式化插件 footer（原型 "1 个可更新"；无可更新为"全部最新"）。
    /// </summary>
    /// <param name="updatableCount">可更新插件数。</param>
    /// <returns>footer 文本。</returns>
    public static string PluginsFooter(int updatableCount)
    {
        return updatableCount > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{updatableCount} 个可更新")
            : "全部最新";
    }

    /// <summary>
    /// 格式化相对时间（活动 feed：&lt;1min "刚刚"，&lt;60min "N 分钟前"，当天 "今天 HH:mm"，否则 "MM-dd HH:mm"）。
    /// </summary>
    /// <param name="now">当前时间。</param>
    /// <param name="timestamp">事件时间。</param>
    /// <returns>相对时间文本。</returns>
    public static string FormatRelativeTime(DateTimeOffset now, DateTimeOffset timestamp)
    {
        TimeSpan delta = now - timestamp;
        if (delta < TimeSpan.FromMinutes(1))
        {
            return "刚刚";
        }

        if (delta < TimeSpan.FromMinutes(60))
        {
            return string.Create(CultureInfo.InvariantCulture, $"{(int)delta.TotalMinutes} 分钟前");
        }

        DateTimeOffset local = timestamp.ToLocalTime();
        if (local.Date == now.ToLocalTime().Date)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"今天 {local:HH:mm}");
        }

        return local.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture);
    }
}
