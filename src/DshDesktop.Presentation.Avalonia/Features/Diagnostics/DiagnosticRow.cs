using DshDesktop.Domain.Diagnostics;

namespace DshDesktop.Presentation.Avalonia.Features.Diagnostics;

/// <summary>
/// 表示 Live 控制台行投影（Phase 8 Issue 06，原型 .diag-console 级别着色）：
/// 把 <see cref="DiagnosticEvent"/> 映射为等宽时间戳 + 级别着色 class 开关。
/// </summary>
/// <param name="Event">源诊断事件。</param>
public sealed record DiagnosticRow(DiagnosticEvent Event)
{
    /// <summary>获取等宽时间戳文本（HH:mm:ss.fff，固定区域性格式防分隔符漂移）。</summary>
    public string TimestampText => Event.Timestamp.ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>获取消息文本。</summary>
    public string Message => Event.Message;

    /// <summary>获取是否成功行（log-ok 绿；Phase 8 评审 F10：显式 Success 级别，不嗅探消息前缀）。</summary>
    public bool IsOk => Event.Level is DiagnosticLevel.Success;

    /// <summary>获取是否警告行（log-warn 黄）。</summary>
    public bool IsWarning => Event.Level is DiagnosticLevel.Warning;

    /// <summary>获取是否错误行（log-err 红）。</summary>
    public bool IsError => Event.Level is DiagnosticLevel.Error;
}
