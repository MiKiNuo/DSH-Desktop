using DshDesktop.Application.Diagnostics;
using DshDesktop.Domain.Diagnostics;
using R3;

namespace DshDesktop.Tests;

/// <summary>
/// 运行诊断编排测试（Phase 8 Issue 06，原型 221-243 行 "运行诊断" 按钮）：
/// 按序执行健康检查序列并把结构化事件（Diagnosis.*）写入诊断流。
/// </summary>
public sealed class DiagnosisRunnerTests
{
    [Test]
    public async Task RunAsync_AllChecksPass_PublishesStartedPassedCompleted()
    {
        var hub = new DiagnosticsHub();
        List<DiagnosticEvent> captured = [];
        using IDisposable subscription = hub.Events.Subscribe(e => captured.Add(e));
        var runner = new DiagnosisRunner(hub);

        await runner.RunAsync(
        [
            new DiagnosisCheck("Runtime 进程健康", _ => Task.FromResult(true)),
            new DiagnosisCheck("HTTP 端点可达", _ => Task.FromResult(true)),
        ], CancellationToken.None);

        await Assert.That(captured.Count).IsEqualTo(4);
        await Assert.That(captured[0].Message).IsEqualTo(DiagnosticEventNames.DiagnosisStarted);
        await Assert.That(captured[0].Level).IsEqualTo(DiagnosticLevel.Info);
        await Assert.That(captured[1].Message).Contains(DiagnosticEventNames.DiagnosisCheckPassed);
        await Assert.That(captured[1].Message).Contains("Runtime 进程健康");
        await Assert.That(captured[1].Level).IsEqualTo(DiagnosticLevel.Success); // F10：成功用显式级别
        await Assert.That(captured[2].Message).Contains(DiagnosticEventNames.DiagnosisCheckPassed);
        await Assert.That(captured[2].Level).IsEqualTo(DiagnosticLevel.Success);
        await Assert.That(captured[3].Message).Contains(DiagnosticEventNames.DiagnosisCompleted);
        await Assert.That(captured[3].Level).IsEqualTo(DiagnosticLevel.Info);
    }

    [Test]
    public async Task RunAsync_FailingCheck_PublishesFailedAndCompletedWarning()
    {
        var hub = new DiagnosticsHub();
        List<DiagnosticEvent> captured = [];
        using IDisposable subscription = hub.Events.Subscribe(e => captured.Add(e));
        var runner = new DiagnosisRunner(hub);

        await runner.RunAsync(
        [
            new DiagnosisCheck("Runtime 进程健康", _ => Task.FromResult(true)),
            new DiagnosisCheck("HTTP 端点可达", _ => Task.FromResult(false)),
        ], CancellationToken.None);

        await Assert.That(captured.Count).IsEqualTo(4);
        await Assert.That(captured[2].Message).Contains(DiagnosticEventNames.DiagnosisCheckFailed);
        await Assert.That(captured[2].Message).Contains("HTTP 端点可达");
        await Assert.That(captured[2].Level).IsEqualTo(DiagnosticLevel.Warning);
        await Assert.That(captured[3].Message).Contains(DiagnosticEventNames.DiagnosisCompleted);
        await Assert.That(captured[3].Message).Contains("1");
        await Assert.That(captured[3].Level).IsEqualTo(DiagnosticLevel.Warning);
    }

    [Test]
    public async Task RunAsync_ThrowingCheck_TreatedAsFailureAndSequenceCompletes()
    {
        var hub = new DiagnosticsHub();
        List<DiagnosticEvent> captured = [];
        using IDisposable subscription = hub.Events.Subscribe(e => captured.Add(e));
        var runner = new DiagnosisRunner(hub);

        await runner.RunAsync(
        [
            new DiagnosisCheck("Profile 完整性", _ => throw new InvalidOperationException("manifest 损坏")),
            new DiagnosisCheck("插件依赖检查", _ => Task.FromResult(true)),
        ], CancellationToken.None);

        await Assert.That(captured.Count).IsEqualTo(4);
        await Assert.That(captured[1].Message).Contains(DiagnosticEventNames.DiagnosisCheckFailed);
        await Assert.That(captured[1].Message).Contains("manifest 损坏");
        await Assert.That(captured[2].Message).Contains(DiagnosticEventNames.DiagnosisCheckPassed);
        await Assert.That(captured[3].Message).Contains(DiagnosticEventNames.DiagnosisCompleted);
    }

    [Test]
    public async Task RunAsync_PublishesAppSourceEvents()
    {
        var hub = new DiagnosticsHub();
        List<DiagnosticEvent> captured = [];
        using IDisposable subscription = hub.Events.Subscribe(e => captured.Add(e));
        var runner = new DiagnosisRunner(hub);

        await runner.RunAsync([new DiagnosisCheck("A", _ => Task.FromResult(true))], CancellationToken.None);

        await Assert.That(captured.All(e => e.Source == DiagnosticSource.App)).IsTrue();
    }
}
