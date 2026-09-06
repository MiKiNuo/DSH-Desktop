using DshDesktop.Domain.Diagnostics;
using DshDesktop.Presentation.Avalonia.Features.Dashboard;

namespace DshDesktop.Tests;

/// <summary>
/// Dashboard 最近活动 feed 测试（Phase 8 Issue 03）：
/// 过滤（仅 App / Supervisor 且 Info 以上的结构化事件）+ 截断（最新 N 条）。
/// </summary>
public sealed class ActivityFeedTests
{
    [Test]
    public async Task IsActivity_FiltersRawProcessOutputAndDebug()
    {
        var now = DateTimeOffset.Now;

        await Assert.That(ActivityFeed.IsActivity(
            new DiagnosticEvent(now, DiagnosticSource.App, DiagnosticLevel.Info, "Desktop.Startup"))).IsTrue();
        await Assert.That(ActivityFeed.IsActivity(
            new DiagnosticEvent(now, DiagnosticSource.Supervisor, DiagnosticLevel.Info, "Runtime.Start.Ready"))).IsTrue();
        await Assert.That(ActivityFeed.IsActivity(
            new DiagnosticEvent(now, DiagnosticSource.Supervisor, DiagnosticLevel.Warning, "Runtime.Health.Changed"))).IsTrue();

        // DSH 进程原始 stdout/stderr 行不是"活动"。
        await Assert.That(ActivityFeed.IsActivity(
            new DiagnosticEvent(now, DiagnosticSource.DshStdout, DiagnosticLevel.Info, "some log"))).IsFalse();
        await Assert.That(ActivityFeed.IsActivity(
            new DiagnosticEvent(now, DiagnosticSource.DshStderr, DiagnosticLevel.Warning, "warn"))).IsFalse();

        // Debug 级（如 Runtime.Start.Stage 计时）不进活动 feed。
        await Assert.That(ActivityFeed.IsActivity(
            new DiagnosticEvent(now, DiagnosticSource.Supervisor, DiagnosticLevel.Debug, "Runtime.Start.Stage"))).IsFalse();
        await Assert.That(ActivityFeed.IsActivity(
            new DiagnosticEvent(now, DiagnosticSource.App, DiagnosticLevel.Debug, "debug"))).IsFalse();
    }

    [Test]
    public async Task Project_TrimsToNewestN_PreservingChronologicalOrder()
    {
        var baseTime = new DateTimeOffset(2026, 9, 5, 10, 0, 0, TimeSpan.FromHours(8));
        List<DiagnosticEvent> entries = [];
        for (int i = 0; i < ActivityFeed.MaxEntries + 4; i++)
        {
            entries.Add(new DiagnosticEvent(
                baseTime.AddMinutes(i), DiagnosticSource.App, DiagnosticLevel.Info, $"Event.{i}"));
        }

        IReadOnlyList<DiagnosticEvent> projected = ActivityFeed.Project(entries);

        await Assert.That(projected.Count).IsEqualTo(ActivityFeed.MaxEntries);
        await Assert.That(projected[0].Message).IsEqualTo("Event.4");
        await Assert.That(projected[^1].Message).IsEqualTo($"Event.{ActivityFeed.MaxEntries + 3}");
    }

    [Test]
    public async Task Project_FiltersBeforeTrimming()
    {
        var now = DateTimeOffset.Now;
        DiagnosticEvent[] entries =
        [
            new(now, DiagnosticSource.DshStdout, DiagnosticLevel.Info, "raw-1"),
            new(now, DiagnosticSource.App, DiagnosticLevel.Info, "App.Event"),
            new(now, DiagnosticSource.DshStderr, DiagnosticLevel.Error, "raw-2"),
        ];

        IReadOnlyList<DiagnosticEvent> projected = ActivityFeed.Project(entries);

        await Assert.That(projected.Count).IsEqualTo(1);
        await Assert.That(projected[0].Message).IsEqualTo("App.Event");
    }
}
