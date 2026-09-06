using DshDesktop.Application.Runtime;
using DshDesktop.Presentation.Avalonia.Features.Dashboard;

namespace DshDesktop.Tests;

/// <summary>
/// Dashboard 启动 timeline 推导测试（Phase 8 Issue 03）：
/// 累计阶段计时 → 分段耗时行（名称 + 时长 + 占总耗时比例宽度）。
/// </summary>
public sealed class DashboardTimelineTests
{
    [Test]
    public async Task Build_Empty_ReturnsEmpty()
    {
        await Assert.That(DashboardTimeline.Build([]).Count).IsEqualTo(0);
    }

    [Test]
    public async Task Build_FullChain_SegmentsWithRelativeWidths()
    {
        StartupStageTiming[] timings =
        [
            new(RuntimeStartupSignal.Spawning, TimeSpan.FromMilliseconds(100)),
            new(RuntimeStartupSignal.WaitingReady, TimeSpan.FromMilliseconds(600)),
            new(RuntimeStartupSignal.HttpProbing, TimeSpan.FromMilliseconds(1300)),
            new(RuntimeStartupSignal.Ready, TimeSpan.FromMilliseconds(1600)),
        ];

        IReadOnlyList<DashboardTimelineRow> rows = DashboardTimeline.Build(timings);

        await Assert.That(rows.Count).IsEqualTo(4);
        await Assert.That(rows[0].Name).IsEqualTo("环境校验");
        await Assert.That(rows[1].Name).IsEqualTo("拉起 DSH 进程");
        await Assert.That(rows[2].Name).IsEqualTo("Node / Profile / Plugins");
        await Assert.That(rows[3].Name).IsEqualTo("HTTP Ready");

        await Assert.That(rows[0].DurationMs).IsEqualTo(100);
        await Assert.That(rows[1].DurationMs).IsEqualTo(500);
        await Assert.That(rows[2].DurationMs).IsEqualTo(700);
        await Assert.That(rows[3].DurationMs).IsEqualTo(300);

        // 宽度 = 段耗时 / 总耗时（1600ms）的相对比例。
        await Assert.That(rows[0].WidthPercent >= 6 && rows[0].WidthPercent <= 7).IsTrue();
        await Assert.That(rows[2].WidthPercent >= 43 && rows[2].WidthPercent <= 44).IsTrue();
        await Assert.That(rows[3].WidthPercent >= 18 && rows[3].WidthPercent <= 19).IsTrue();
    }

    [Test]
    public async Task Build_WithoutHttpProbing_StillProjects()
    {
        // 老数据缺 HttpProbing 标记时 Ready 段吸收全部等待耗时，不崩溃。
        StartupStageTiming[] timings =
        [
            new(RuntimeStartupSignal.Spawning, TimeSpan.FromMilliseconds(100)),
            new(RuntimeStartupSignal.Ready, TimeSpan.FromMilliseconds(1100)),
        ];

        IReadOnlyList<DashboardTimelineRow> rows = DashboardTimeline.Build(timings);

        await Assert.That(rows.Count).IsEqualTo(2);
        await Assert.That(rows[1].DurationMs).IsEqualTo(1000);
    }

    [Test]
    public async Task Build_FormatsDurationText()
    {
        StartupStageTiming[] timings =
        [
            new(RuntimeStartupSignal.Spawning, TimeSpan.FromMilliseconds(94)),
            new(RuntimeStartupSignal.Ready, TimeSpan.FromMilliseconds(1823)),
        ];

        IReadOnlyList<DashboardTimelineRow> rows = DashboardTimeline.Build(timings);

        await Assert.That(rows[0].DurationText).IsEqualTo("94 ms");
        await Assert.That(rows[1].DurationText).IsEqualTo("1.73 s");
    }
}
