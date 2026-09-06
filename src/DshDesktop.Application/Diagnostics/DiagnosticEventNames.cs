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

    /// <summary>连续启动失败自动进入安全模式事件名（ADR-0004 修订注，Phase 8 Issue 04，组合根发）。</summary>
    public const string RuntimeAutoSafeModeEntered = "Runtime.AutoSafeMode.Entered";

    /// <summary>运行诊断开始事件名（Phase 8 Issue 06，DiagnosisRunner 发）。</summary>
    public const string DiagnosisStarted = "Diagnosis.Started";

    /// <summary>单项健康检查通过事件名（DiagnosisRunner 发，Message 附带检查名）。</summary>
    public const string DiagnosisCheckPassed = "Diagnosis.Check.Passed";

    /// <summary>单项健康检查失败事件名（DiagnosisRunner 发，Message 附带检查名与错误）。</summary>
    public const string DiagnosisCheckFailed = "Diagnosis.Check.Failed";

    /// <summary>诊断序列完成事件名（DiagnosisRunner 发，Message 附带失败计数）。</summary>
    public const string DiagnosisCompleted = "Diagnosis.Completed";

    /// <summary>诊断包导出完成事件名（组合根发，Message 附带目标路径）。</summary>
    public const string DiagnosisExportCompleted = "Diagnosis.Export.Completed";

    /// <summary>诊断包导出失败事件名（组合根发，Message 附带错误）。</summary>
    public const string DiagnosisExportFailed = "Diagnosis.Export.Failed";
}
