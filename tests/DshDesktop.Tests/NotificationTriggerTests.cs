using DshDesktop.Application.Diagnostics;
using DshDesktop.Application.Notifications;
using DshDesktop.Domain.Diagnostics;

namespace DshDesktop.Tests;

/// <summary>
/// 通知触发过滤测试（Phase 7 Issue 03）：结构化事件名匹配 + 开关判定，纯函数直测。
/// </summary>
public sealed class NotificationTriggerTests
{
    private static DiagnosticEvent Event(
        string message,
        DiagnosticSource source = DiagnosticSource.Supervisor,
        DiagnosticLevel level = DiagnosticLevel.Error)
    {
        return new DiagnosticEvent(DateTimeOffset.Now, source, level, message);
    }

    [Test]
    public async Task CrashEvent_WhenEnabled_Matches()
    {
        NotificationContent? content = NotificationTrigger.TryMatch(
            Event("Runtime.Crash.Detected ExitCode=1"), notificationsEnabled: true);

        await Assert.That(content).IsNotNull();
        await Assert.That(content!.Title).IsEqualTo("Runtime 崩溃");
    }

    [Test]
    public async Task CrashEvent_WhenDisabled_DoesNotMatch()
    {
        NotificationContent? content = NotificationTrigger.TryMatch(
            Event("Runtime.Crash.Detected ExitCode=1"), notificationsEnabled: false);

        await Assert.That(content).IsNull();
    }

    [Test]
    public async Task UnrelatedEvent_DoesNotMatch()
    {
        NotificationContent? content = NotificationTrigger.TryMatch(
            Event("Runtime.Exit ExitCode=0", level: DiagnosticLevel.Warning), notificationsEnabled: true);

        await Assert.That(content).IsNull();
    }

    [Test]
    public async Task RollbackEvent_WhenEnabled_Matches()
    {
        NotificationContent? content = NotificationTrigger.TryMatch(
            Event("Plugin.Rollback.RestoreFailed 磁盘占用"), notificationsEnabled: true);

        await Assert.That(content).IsNotNull();
        await Assert.That(content!.Title).IsEqualTo("插件事务失败回滚");
    }

    [Test]
    public async Task RollbackEvent_WhenDisabled_DoesNotMatch()
    {
        NotificationContent? content = NotificationTrigger.TryMatch(
            Event("Plugin.Rollback.RestartFailed 端口占用"), notificationsEnabled: false);

        await Assert.That(content).IsNull();
    }

    [Test]
    public async Task InstallRollbackEvent_WhenEnabled_Matches()
    {
        // PluginOrchestrator 每次事务回滚发的主事件（Issue 04 补漏）。
        NotificationContent? content = NotificationTrigger.TryMatch(
            Event("Plugin.Install.Rollback demo 磁盘占用", source: DiagnosticSource.App,
                level: DiagnosticLevel.Warning),
            notificationsEnabled: true);

        await Assert.That(content).IsNotNull();
        await Assert.That(content!.Title).IsEqualTo("插件事务失败回滚");
    }

    [Test]
    public async Task InstallBeginEvent_DoesNotMatch()
    {
        // 仅精确事件名命中，Plugin.Install.* 其他事件不得圈入。
        NotificationContent? content = NotificationTrigger.TryMatch(
            Event("Plugin.Install.Begin demo", source: DiagnosticSource.App), notificationsEnabled: true);

        await Assert.That(content).IsNull();
    }

    [Test]
    public async Task AutoSafeModeEvent_WhenEnabled_Matches()
    {
        // Phase 8 Issue 04（ADR-0004 修订注）：连续启动失败自动进入安全模式 → 通知。
        NotificationContent? content = NotificationTrigger.TryMatch(
            Event(DiagnosticEventNames.RuntimeAutoSafeModeEntered + " ConsecutiveFailures=2",
                source: DiagnosticSource.App),
            notificationsEnabled: true);

        await Assert.That(content).IsNotNull();
        await Assert.That(content!.Title).IsEqualTo("自动进入安全模式");
    }

    [Test]
    public async Task DshProcessOutput_SpoofingEventName_DoesNotMatch()
    {
        // DSH 进程 stdout/stderr 是自由文本，不得触发通知（只认 App/Supervisor 结构化事件）。
        NotificationContent? content = NotificationTrigger.TryMatch(
            Event("Runtime.Crash.Detected ExitCode=1", source: DiagnosticSource.DshStdout),
            notificationsEnabled: true);

        await Assert.That(content).IsNull();
    }

    [Test]
    public async Task BootstrapFailedEvent_WhenEnabled_Matches()
    {
        // 2026-09-13 回归：本地初始化失败（如配置读取异常）此前只有一条日志，
        // 用户只见「窗口能开、Runtime 不动」而无线索。必须升格为可见通知。
        NotificationContent? content = NotificationTrigger.TryMatch(
            Event(DiagnosticEventNames.DesktopBootstrapFailed + " JsonException",
                source: DiagnosticSource.App),
            notificationsEnabled: true);

        await Assert.That(content).IsNotNull();
        await Assert.That(content!.Title).IsEqualTo("Desktop 初始化失败");
    }

    [Test]
    public async Task BootstrapFailedEvent_WhenDisabled_DoesNotMatch()
    {
        NotificationContent? content = NotificationTrigger.TryMatch(
            Event(DiagnosticEventNames.DesktopBootstrapFailed + " JsonException",
                source: DiagnosticSource.App),
            notificationsEnabled: false);

        await Assert.That(content).IsNull();
    }
}
