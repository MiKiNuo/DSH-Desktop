namespace DshDesktop.Application.Notifications;

/// <summary>
/// 表示一条待显示的通知内容。
/// </summary>
/// <param name="Title">通知标题。</param>
/// <param name="Message">通知正文。</param>
public sealed record NotificationContent(string Title, string Message);
