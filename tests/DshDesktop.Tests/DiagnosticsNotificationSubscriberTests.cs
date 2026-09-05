using DshDesktop.Application.Diagnostics;
using DshDesktop.Application.Notifications;
using DshDesktop.Domain.Diagnostics;
using R3;

namespace DshDesktop.Tests;

/// <summary>
/// 诊断通知订阅器测试（Phase 7 Issue 03）：订阅 DiagnosticsHub，命中触发条件才调通知服务。
/// 通知服务用 Fake，Platform 层"显示"不在此测。
/// </summary>
public sealed class DiagnosticsNotificationSubscriberTests
{
    private static DiagnosticEvent Event(string message)
    {
        return new DiagnosticEvent(
            DateTimeOffset.Now, DiagnosticSource.Supervisor, DiagnosticLevel.Error, message);
    }

    [Test]
    public async Task CrashEvent_ShowsNotification()
    {
        var hub = new DiagnosticsHub();
        var fake = new FakeNotificationService();
        using var subscriber = new DiagnosticsNotificationSubscriber(hub, fake, () => true);

        hub.Publish(Event("Runtime.Crash.Detected ExitCode=1"));

        await Assert.That(fake.Calls.Count).IsEqualTo(1);
        await Assert.That(fake.Calls[0].Title).IsEqualTo("Runtime 崩溃");
    }

    [Test]
    public async Task CrashEvent_WhenDisabled_DoesNotShow()
    {
        var hub = new DiagnosticsHub();
        var fake = new FakeNotificationService();
        using var subscriber = new DiagnosticsNotificationSubscriber(hub, fake, () => false);

        hub.Publish(Event("Runtime.Crash.Detected ExitCode=1"));

        await Assert.That(fake.Calls.Count).IsEqualTo(0);
    }

    [Test]
    public async Task UnrelatedEvent_DoesNotShow()
    {
        var hub = new DiagnosticsHub();
        var fake = new FakeNotificationService();
        using var subscriber = new DiagnosticsNotificationSubscriber(hub, fake, () => true);

        hub.Publish(Event("Runtime.Start.Begin"));

        await Assert.That(fake.Calls.Count).IsEqualTo(0);
    }

    [Test]
    public async Task RollbackEvent_ShowsNotification()
    {
        var hub = new DiagnosticsHub();
        var fake = new FakeNotificationService();
        using var subscriber = new DiagnosticsNotificationSubscriber(hub, fake, () => true);

        hub.Publish(Event("Plugin.Rollback.RestoreFailed 磁盘占用"));

        await Assert.That(fake.Calls.Count).IsEqualTo(1);
        await Assert.That(fake.Calls[0].Title).IsEqualTo("插件事务失败回滚");
    }

    [Test]
    public async Task NotificationServiceThrows_DoesNotPropagateToDiagnosticStream()
    {
        // F2：通知服务同步抛异常（如 Win32Exception）不得逃逸进 R3 订阅回调——
        // R3 会把逃逸异常路由到订阅时快照的未处理异常处理器，须就地吞掉并降级为日志。
        // 故捕获处理器须在订阅之前注册（R3 订阅时快照处理器引用）。
        var unhandled = new List<Exception>();
        Action<Exception> previousHandler = ObservableSystem.GetUnhandledExceptionHandler();
        ObservableSystem.RegisterUnhandledExceptionHandler(unhandled.Add);
        int otherSubscriberCount = 0;
        try
        {
            var hub = new DiagnosticsHub();
            using var subscriber = new DiagnosticsNotificationSubscriber(hub, new ThrowingNotificationService(), () => true);
            using var other = hub.Events.Subscribe(_ => otherSubscriberCount++);

            hub.Publish(Event("Runtime.Crash.Detected ExitCode=1"));
        }
        finally
        {
            ObservableSystem.RegisterUnhandledExceptionHandler(previousHandler);
        }

        await Assert.That(unhandled.Count).IsEqualTo(0);
        await Assert.That(otherSubscriberCount).IsEqualTo(1);
    }

    private sealed class FakeNotificationService : INotificationService
    {
        public List<(string Title, string Message)> Calls { get; } = [];

        public Task ShowAsync(string title, string message, CancellationToken cancellationToken = default)
        {
            Calls.Add((title, message));
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingNotificationService : INotificationService
    {
        public Task ShowAsync(string title, string message, CancellationToken cancellationToken = default)
        {
            throw new System.ComponentModel.Win32Exception(5, "模拟气泡通知 Win32 失败");
        }
    }
}
