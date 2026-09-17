namespace DshDesktop.Domain.Plugins;

/// <summary>
/// 表示插件操作阶段（架构文档 §20 状态机，禁止布尔组合）。
/// </summary>
public enum PluginOperationStage
{
    /// <summary>空闲。</summary>
    Idle,

    /// <summary>准备中（校验输入）。</summary>
    Preparing,

    /// <summary>创建 Profile 快照。</summary>
    CreatingSnapshot,

    /// <summary>停止 Runtime。</summary>
    StoppingRuntime,

    /// <summary>安装插件。</summary>
    Installing,

    /// <summary>校验 Profile 一致性。</summary>
    Validating,

    /// <summary>启动 Runtime。</summary>
    StartingRuntime,

    /// <summary>健康检查。</summary>
    HealthChecking,

    /// <summary>事务提交完成。</summary>
    Completed,

    /// <summary>回滚中。</summary>
    RollingBack,

    /// <summary>失败（回滚已执行）。</summary>
    Failed,
}

/// <summary>
/// 表示插件操作的业务种类：同一套安装事务被"安装"与"更新"两个入口复用，
/// 下游反馈文案（壳 toast）需要据此区分。
/// </summary>
public enum PluginOperationKind
{
    /// <summary>安装新插件。</summary>
    Install,

    /// <summary>把已安装插件升级到最新版。</summary>
    Update,
}

/// <summary>
/// 表示一次插件操作（§19 安装事务）的进度快照。
/// </summary>
/// <param name="Stage">当前阶段。</param>
/// <param name="PluginName">目标插件名（安装前可能未知，为 null）。</param>
/// <param name="Error">失败信息；未失败为 null。</param>
/// <param name="Kind">操作种类。</param>
public sealed record PluginOperation(
    PluginOperationStage Stage,
    string? PluginName,
    string? Error,
    PluginOperationKind Kind = PluginOperationKind.Install);
