namespace DshDesktop.Domain.Diagnostics;

/// <summary>
/// 表示诊断事件来源（§24）。
/// </summary>
public enum DiagnosticSource
{
    /// <summary>DSH 进程标准输出。</summary>
    DshStdout,

    /// <summary>DSH 进程标准错误。</summary>
    DshStderr,

    /// <summary>Runtime Supervisor（阶段 / 健康 / 退出）。</summary>
    Supervisor,

    /// <summary>Desktop 应用自身。</summary>
    App,
}

/// <summary>
/// 表示诊断事件级别。
/// </summary>
public enum DiagnosticLevel
{
    /// <summary>调试。</summary>
    Debug,

    /// <summary>信息。</summary>
    Info,

    /// <summary>警告。</summary>
    Warning,

    /// <summary>错误。</summary>
    Error,
}

/// <summary>
/// 表示一条诊断事件（§24：事件流型 Feature 的最小单元）。
/// App 结构化事件（§45）以 Message 承载，如 `Runtime.Start.Begin`。
/// </summary>
/// <param name="Timestamp">事件时间。</param>
/// <param name="Source">事件来源。</param>
/// <param name="Level">事件级别。</param>
/// <param name="Message">事件内容。</param>
public sealed record DiagnosticEvent(
    DateTimeOffset Timestamp,
    DiagnosticSource Source,
    DiagnosticLevel Level,
    string Message);
