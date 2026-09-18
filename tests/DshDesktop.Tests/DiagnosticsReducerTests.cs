using DshDesktop.Domain.Diagnostics;
using DshDesktop.Presentation.Avalonia.Features.Diagnostics;

namespace DshDesktop.Tests;

/// <summary>
/// Diagnostics 规约器测试（§25：UI Store 容量上限 1000 条，从头截断）。
/// </summary>
public sealed class DiagnosticsReducerTests
{
    private readonly DiagnosticsReducer _reducer = new();

    [Test]
    public async Task DiagnosticEventReceived_AppendsToEmpty()
    {
        var result = _reducer.Reduce(DiagnosticsState.Initial, new DiagnosticsIntent.DiagnosticEventReceived(Event("第一条")));

        await Assert.That(result.State.Entries.Count).IsEqualTo(1);
        await Assert.That(result.State.Entries[0].Message).IsEqualTo("第一条");
    }

    [Test]
    public async Task DiagnosticEventReceived_AppendsInOrder()
    {
        DiagnosticsState state = DiagnosticsState.Initial;
        state = _reducer.Reduce(state, new DiagnosticsIntent.DiagnosticEventReceived(Event("A"))).State;
        state = _reducer.Reduce(state, new DiagnosticsIntent.DiagnosticEventReceived(Event("B"))).State;

        await Assert.That(state.Entries.Count).IsEqualTo(2);
        await Assert.That(state.Entries[0].Message).IsEqualTo("A");
        await Assert.That(state.Entries[1].Message).IsEqualTo("B");
    }

    [Test]
    public async Task DiagnosticEventReceived_AtCapacity_DropsOldest()
    {
        // 构造已满 1000 条的窗口。
        List<DiagnosticEvent> full = [];
        for (int i = 0; i < 1000; i++)
        {
            full.Add(Event($"E{i}"));
        }

        DiagnosticsState state = DiagnosticsState.Initial with { Entries = full };

        var result = _reducer.Reduce(state, new DiagnosticsIntent.DiagnosticEventReceived(Event("新")));

        await Assert.That(result.State.Entries.Count).IsEqualTo(1000);
        await Assert.That(result.State.Entries[0].Message).IsEqualTo("E1"); // E0 被截掉
        await Assert.That(result.State.Entries[999].Message).IsEqualTo("新");
    }

    [Test]
    public async Task ClearEntries_EmptiesWindowWithoutEffect()
    {
        // 右键「清除日志」只清 UI Store 展示窗口：纯状态变更、不声明 Effect，
        // 磁盘日志（data/logs/）与后续 Live 事件流不受影响。
        // 本用例同时是「新增 Intent 漏 [MviReduce] 静默 no-op」的防呆断言。
        DiagnosticsState state = DiagnosticsState.Initial with
        {
            Entries = [Event("A"), Event("B"), Event("C")],
        };

        var result = _reducer.Reduce(state, new DiagnosticsIntent.ClearEntries());

        await Assert.That(result.State.Entries.Count).IsEqualTo(0);
        await Assert.That(result.Effects.Count).IsEqualTo(0);
    }

    [Test]
    public async Task RunDiagnosis_DeclaresEffect()
    {
        var result = _reducer.Reduce(DiagnosticsState.Initial, new DiagnosticsIntent.RunDiagnosis());

        await Assert.That(result.Effects[0] is DiagnosticsEffect.RunDiagnosis).IsTrue();
    }

    [Test]
    public async Task ExportDiagnosticsBundle_DeclaresEffectWithPath()
    {
        var result = _reducer.Reduce(
            DiagnosticsState.Initial, new DiagnosticsIntent.ExportDiagnosticsBundle(@"C:\Temp\diag.zip"));

        await Assert.That(
            result.Effects[0] is DiagnosticsEffect.ExportBundle { DestinationPath: @"C:\Temp\diag.zip" }).IsTrue();
    }

    [Test]
    public async Task OpenLogsDirectory_DeclaresEffect()
    {
        var result = _reducer.Reduce(DiagnosticsState.Initial, new DiagnosticsIntent.OpenLogsDirectory());

        await Assert.That(result.Effects[0] is DiagnosticsEffect.OpenLogsDirectory).IsTrue();
    }

    private static DiagnosticEvent Event(string message)
    {
        return new DiagnosticEvent(
            System.DateTimeOffset.UnixEpoch, DiagnosticSource.App, DiagnosticLevel.Info, message);
    }
}
