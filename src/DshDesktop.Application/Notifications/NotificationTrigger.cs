using DshDesktop.Application.Diagnostics;
using DshDesktop.Domain.Diagnostics;

namespace DshDesktop.Application.Notifications;

/// <summary>
/// 表示通知触发过滤器（Phase 7 Issue 03）：纯函数，事件名匹配 + 开关判定。
/// 触发点两个——Runtime 崩溃（<see cref="DiagnosticEventNames.RuntimeCrashDetected"/>）+
/// 插件事务失败回滚（<see cref="DiagnosticEventNames.PluginInstallRollback"/> /
/// <see cref="DiagnosticEventNames.PluginRollbackPrefix"/>*）。事件名以生产侧常量为单源。
/// </summary>
public static class NotificationTrigger
{
    /// <summary>
    /// 判定一条诊断事件是否应触发通知。
    /// </summary>
    /// <param name="diagnosticEvent">诊断事件。</param>
    /// <param name="notificationsEnabled">通知开关（config.NotificationsEnabled 的当前值）。</param>
    /// <returns>命中时返回通知内容；否则返回 null。</returns>
    public static NotificationContent? TryMatch(DiagnosticEvent diagnosticEvent, bool notificationsEnabled)
    {
        if (!notificationsEnabled)
        {
            return null;
        }

        // 只认 App/Supervisor 结构化事件（§45）；DSH 进程 stdout/stderr 是自由文本，防误报。
        if (diagnosticEvent.Source is not (DiagnosticSource.Supervisor or DiagnosticSource.App))
        {
            return null;
        }

        string message = diagnosticEvent.Message;
        if (message.StartsWith(DiagnosticEventNames.RuntimeCrashDetected, StringComparison.Ordinal))
        {
            return new NotificationContent("Runtime 崩溃", message);
        }

        if (message.StartsWith(DiagnosticEventNames.PluginRollbackPrefix, StringComparison.Ordinal)
            || message.StartsWith(DiagnosticEventNames.PluginInstallRollback, StringComparison.Ordinal))
        {
            return new NotificationContent("插件事务失败回滚", message);
        }

        return null;
    }
}
