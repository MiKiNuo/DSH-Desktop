namespace DshDesktop.Application.Notifications;

/// <summary>
/// 表示通知服务端口（Phase 7 Issue 03，§4.5）：Application 层只声明"显示"，
/// Windows 气泡实现落在 DshDesktop.Platform.Windows。
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// 显示一条通知。
    /// </summary>
    /// <param name="title">通知标题。</param>
    /// <param name="message">通知正文。</param>
    /// <param name="cancellationToken">取消标记。</param>
    Task ShowAsync(string title, string message, CancellationToken cancellationToken = default);
}
