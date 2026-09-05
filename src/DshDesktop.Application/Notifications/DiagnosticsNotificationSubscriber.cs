using DshDesktop.Application.Diagnostics;
using DshDesktop.Domain.Diagnostics;
using R3;
using Serilog;

namespace DshDesktop.Application.Notifications;

/// <summary>
/// 表示诊断通知订阅器（Phase 7 Issue 03）：订阅 <see cref="DiagnosticsHub"/> 事件流，
/// 命中 <see cref="NotificationTrigger"/> 触发条件时调用通知服务。
/// Hub 不是 Store（§11.2），组合根可直接订阅；开关判定委托给外部（config 为权威源）。
/// </summary>
public sealed class DiagnosticsNotificationSubscriber : IDisposable
{
    private readonly INotificationService _notifications;
    private readonly Func<bool> _isEnabled;
    private readonly IDisposable _subscription;

    /// <summary>
    /// 初始化诊断通知订阅器并开始订阅。
    /// </summary>
    /// <param name="hub">诊断事件中枢。</param>
    /// <param name="notifications">通知服务。</param>
    /// <param name="isEnabled">通知开关读取委托（每次事件到来时读取最新值）。</param>
    public DiagnosticsNotificationSubscriber(
        DiagnosticsHub hub,
        INotificationService notifications,
        Func<bool> isEnabled)
    {
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(isEnabled);
        _notifications = notifications;
        _isEnabled = isEnabled;
        _subscription = hub.Events.Subscribe(OnEvent);
    }

    /// <summary>
    /// 退订诊断事件流。
    /// </summary>
    public void Dispose()
    {
        _subscription.Dispose();
    }

    private void OnEvent(DiagnosticEvent diagnosticEvent)
    {
        if (NotificationTrigger.TryMatch(diagnosticEvent, _isEnabled()) is { } content)
        {
            try
            {
                _ = _notifications.ShowAsync(content.Title, content.Message);
            }
            catch (Exception exception)
            {
                // 通知失败降级为日志：同步异常（如 Win32Exception）不得逃逸进 R3 订阅回调，
                // 否则会被当作未处理异常全局上报并中断当次发布循环，污染诊断流其他订阅者。
                Log.Logger.Warning("Notification.Show.Failed {Error}", exception.Message);
            }
        }
    }
}
