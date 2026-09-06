namespace DshDesktop.Application.Startup;

/// <summary>
/// 表示开机自启编排（Phase 8 Issue 05）：开关翻转 → 端口登记，exe 路径由组合根注入
/// （同 DiagnosticsNotificationSubscriber 先例：Application 编排 + 端口可 Fake）。
/// </summary>
public sealed class StartupRegistrationService
{
    private readonly IStartupRegistrar _registrar;
    private readonly Func<string> _executablePath;

    /// <summary>
    /// 初始化开机自启编排。
    /// </summary>
    /// <param name="registrar">自启注册端口。</param>
    /// <param name="executablePath">当前 exe 路径取值器。</param>
    public StartupRegistrationService(IStartupRegistrar registrar, Func<string> executablePath)
    {
        ArgumentNullException.ThrowIfNull(registrar);
        ArgumentNullException.ThrowIfNull(executablePath);
        _registrar = registrar;
        _executablePath = executablePath;
    }

    /// <summary>
    /// 应用开关状态：开 = 以当前 exe 路径登记；关 = 移除登记。
    /// </summary>
    /// <param name="enabled">目标开关状态。</param>
    public void SetEnabled(bool enabled)
    {
        _registrar.SetEnabled(enabled, _executablePath());
    }
}
