namespace DshDesktop.Application.Diagnostics;

/// <summary>
/// 结构化诊断事件名常量（Phase 7）：生产侧日志点（RuntimeSupervisor / PluginOrchestrator）
/// 与消费侧通知触发（NotificationTrigger 前缀匹配）引用同一单源，改名即编译期全量生效，
/// 防字符串字面量漂移导致消费侧静默失效。
/// </summary>
public static class DiagnosticEventNames
{
    /// <summary>Runtime 崩溃结构化事件名（Supervisor 在 Running/Starting 中非编排退出时发）。</summary>
    public const string RuntimeCrashDetected = "Runtime.Crash.Detected";

    /// <summary>插件安装事务回滚主事件名（PluginOrchestrator 每次事务回滚都发）。</summary>
    public const string PluginInstallRollback = "Plugin.Install.Rollback";

    /// <summary>插件事务回滚结构化事件名前缀（RestoreFailed / RestartFailed）。</summary>
    public const string PluginRollbackPrefix = "Plugin.Rollback.";
}
