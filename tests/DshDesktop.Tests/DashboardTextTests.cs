using DshDesktop.Domain.Runtime;
using DshDesktop.Presentation.Avalonia.Features.Dashboard;

namespace DshDesktop.Tests;

/// <summary>
/// Dashboard 文本映射测试（Phase 8 Issue 03）：hero 标题/副文案、健康状态、启动耗时对比、
/// 通道 footer、内存/CPU 格式化、相对时间——全部为纯函数。
/// </summary>
public sealed class DashboardTextTests
{
    [Test]
    public async Task HeroTitle_FollowsLifecycle()
    {
        await Assert.That(DashboardText.HeroTitle(RuntimeLifecycle.Running)).IsEqualTo("DSH 服务已就绪");
        await Assert.That(DashboardText.HeroTitle(RuntimeLifecycle.Starting)).IsEqualTo("DSH 服务启动中");
        await Assert.That(DashboardText.HeroTitle(RuntimeLifecycle.Stopped)).IsEqualTo("DSH 服务未运行");
        await Assert.That(DashboardText.HeroTitle(RuntimeLifecycle.Stopping)).IsEqualTo("DSH 服务停止中");
        await Assert.That(DashboardText.HeroTitle(RuntimeLifecycle.Failed)).IsEqualTo("DSH 服务启动失败");
        await Assert.That(DashboardText.HeroTitle(RuntimeLifecycle.Recovering)).IsEqualTo("DSH 服务恢复中");
    }

    [Test]
    public async Task HeroSubtitle_Running_ContainsEndpoint()
    {
        string subtitle = DashboardText.HeroSubtitle(RuntimeLifecycle.Running, 3080);

        await Assert.That(subtitle.Contains("127.0.0.1:3080")).IsTrue();
    }

    [Test]
    public async Task HeroSubtitle_NotRunning_NoEndpoint()
    {
        await Assert.That(DashboardText.HeroSubtitle(RuntimeLifecycle.Stopped, null)).IsEqualTo("Runtime 未运行。");
        await Assert.That(DashboardText.HeroSubtitle(RuntimeLifecycle.Starting, null).Contains("127.0.0.1")).IsFalse();
    }

    [Test]
    public async Task HealthText_MapsProjection()
    {
        await Assert.That(DashboardText.HealthText(RuntimeHealth.Healthy)).IsEqualTo("● 正常");
        await Assert.That(DashboardText.HealthText(RuntimeHealth.Unresponsive)).IsEqualTo("● 无响应");
        await Assert.That(DashboardText.HealthText(RuntimeHealth.Unknown)).IsEqualTo("● 未知");
    }

    [Test]
    public async Task StartupComparison_NoCurrent_Placeholder()
    {
        await Assert.That(DashboardText.FormatStartupComparison(null, 2130)).IsEqualTo("—");
    }

    [Test]
    public async Task StartupComparison_NoPrevious_FirstRecord()
    {
        await Assert.That(
            DashboardText.FormatStartupComparison(TimeSpan.FromMilliseconds(1820), null))
            .IsEqualTo("首次记录启动耗时");
    }

    [Test]
    public async Task StartupComparison_Faster()
    {
        await Assert.That(
            DashboardText.FormatStartupComparison(TimeSpan.FromMilliseconds(1820), 2130))
            .IsEqualTo("比上次快 0.31s");
    }

    [Test]
    public async Task StartupComparison_Slower()
    {
        await Assert.That(
            DashboardText.FormatStartupComparison(TimeSpan.FromMilliseconds(2345), 2130))
            .IsEqualTo("比上次慢 0.22s");
    }

    [Test]
    public async Task StartupComparison_WithinEpsilon_Even()
    {
        await Assert.That(
            DashboardText.FormatStartupComparison(TimeSpan.FromMilliseconds(2130), 2132))
            .IsEqualTo("与上次持平");
    }

    [Test]
    public async Task ChannelFooter_Capitalizes()
    {
        await Assert.That(DashboardText.ChannelFooter("stable")).IsEqualTo("Stable Channel");
        await Assert.That(DashboardText.ChannelFooter("beta")).IsEqualTo("Beta Channel");
    }

    [Test]
    public async Task FormatMemoryBytes_Scales()
    {
        await Assert.That(DashboardText.FormatMemoryBytes(412L * 1024 * 1024)).IsEqualTo("412M");
        await Assert.That(DashboardText.FormatMemoryBytes(1536L * 1024 * 1024)).IsEqualTo("1.5G");
        await Assert.That(DashboardText.FormatMemoryBytes(512L * 1024)).IsEqualTo("512K");
    }

    [Test]
    public async Task FormatCpu_Null_Placeholder()
    {
        await Assert.That(DashboardText.FormatCpu(null)).IsEqualTo("—");
        await Assert.That(DashboardText.FormatCpu(18.4)).IsEqualTo("18%");
    }

    [Test]
    public async Task RelativeTime_Buckets()
    {
        var now = new DateTimeOffset(2026, 9, 5, 10, 46, 31, TimeSpan.FromHours(8));

        await Assert.That(DashboardText.FormatRelativeTime(now, now.AddSeconds(-30))).IsEqualTo("刚刚");
        await Assert.That(DashboardText.FormatRelativeTime(now, now.AddMinutes(-4))).IsEqualTo("4 分钟前");
        await Assert.That(DashboardText.FormatRelativeTime(now, now.AddHours(-2))).IsEqualTo("今天 08:46");
        await Assert.That(
            DashboardText.FormatRelativeTime(now, now.AddDays(-1)))
            .IsEqualTo(now.AddDays(-1).ToString("MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture));
    }
}
