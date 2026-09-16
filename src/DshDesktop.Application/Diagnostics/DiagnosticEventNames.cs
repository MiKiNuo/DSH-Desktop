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

    /// <summary>
    /// Runtime Failed 后的有界自动恢复发起事件名（ADR-0007，组合根发，Message 附带尝试次数）。
    /// 只在「新进入 Failed」这一沿触发且每次失败周期仅一次，不构成重启循环。
    /// </summary>
    public const string RuntimeAutoRecoveryAttempted = "Runtime.AutoRecovery.Attempted";

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

    /// <summary>Desktop Bridge shim 安装事件名（ADR-0006，WorkbenchView 每次导航完成重装后发，契约失效排查依据）。</summary>
    public const string BridgeDirectoryPickerInstalled = "Bridge.DirectoryPicker.Installed";

    /// <summary>Desktop Bridge 目录选择调用事件名（ADR-0006，WorkbenchView 收到 pick 请求时发）。</summary>
    public const string BridgeDirectoryPickerInvoked = "Bridge.DirectoryPicker.Invoked";

    /// <summary>
    /// Desktop 初始化失败事件名（App 引导 catch 发）。
    /// 背景：bootstrap 失败会让 Runtime 永不起来但窗口照常可见，此前仅有一条 Log.Error，
    /// 用户完全无法察觉；改为结构化事件使其进诊断流并触发通知。
    /// </summary>
    public const string DesktopBootstrapFailed = "Desktop.Bootstrap.Failed";

    /// <summary>
    /// 工具垫片（&lt;dshHome&gt;\.desktop-bin）生成失败事件名。
    /// 非致命：应用照常启动，但工作台内的 dsh-market 会因找不到按名可解析的 pnpm 而装不了插件
    /// （顶部常驻「安装插件前需要先配置 pnpm 环境」）。缺 resources\pnpm-runner.mjs 或数据根不可写时发。
    /// </summary>
    public const string ToolBinProvisionFailed = "Runtime.ToolBin.ProvisionFailed";

    /// <summary>
    /// pnpm 自举失败事件名（pnpm 不可用时用宿主 npm 安装到 &lt;dataRoot&gt;\tools\pnpm）。
    /// 非致命：装不了插件 ≠ 应用起不来；自举失败只留日志，宿主插件页 / profile 回滚重建本运行不可用，
    /// 但 Runtime 全链路照常初始化。
    /// </summary>
    public const string PnpmProvisionFailed = "Runtime.Pnpm.ProvisionFailed";
}
