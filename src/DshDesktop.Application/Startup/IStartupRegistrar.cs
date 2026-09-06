namespace DshDesktop.Application.Startup;

/// <summary>
/// 表示开机自启注册端口（Phase 8 Issue 05，§4.5 同 INotificationService 先例）：
/// Application 层只声明"登记/移除"，Windows 注册表 Run 键实现落在 DshDesktop.Platform.Windows。
/// </summary>
public interface IStartupRegistrar
{
    /// <summary>
    /// 登记或移除开机自启。
    /// </summary>
    /// <param name="enabled">目标开关状态。</param>
    /// <param name="executablePath">当前 exe 路径（未安装形态同样如实写入）。</param>
    void SetEnabled(bool enabled, string executablePath);
}
