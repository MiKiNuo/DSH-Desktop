using DshDesktop.Application.Diagnostics;
using DshDesktop.Domain.Diagnostics;
using Serilog.Core;
using Serilog.Events;

namespace DshDesktop.App.Logging;

/// <summary>
/// 表示把 Serilog 日志事件转发到 <see cref="DiagnosticsHub"/> 的内存 sink（Q4-A 形态：
/// 所有源统一过 Serilog → File sink 落盘 + 本 sink 入流）。
/// </summary>
public sealed class DiagnosticsSink(DiagnosticsHub hub) : ILogEventSink
{
    /// <inheritdoc />
    public void Emit(LogEvent logEvent)
    {
        DiagnosticSource source = DiagnosticSource.App;
        if (logEvent.Properties.TryGetValue("Source", out LogEventPropertyValue? value)
            && value is ScalarValue { Value: string sourceName }
            && Enum.TryParse(sourceName, out DiagnosticSource parsed))
        {
            source = parsed;
        }

        DiagnosticLevel level = logEvent.Level switch
        {
            LogEventLevel.Verbose or LogEventLevel.Debug => DiagnosticLevel.Debug,
            LogEventLevel.Information => DiagnosticLevel.Info,
            LogEventLevel.Warning => DiagnosticLevel.Warning,
            _ => DiagnosticLevel.Error,
        };

        hub.Publish(new DiagnosticEvent(
            logEvent.Timestamp,
            source,
            level,
            logEvent.RenderMessage()));
    }
}
