using DshDesktop.Domain.Diagnostics;
using R3;

namespace DshDesktop.Application.Diagnostics;

/// <summary>
/// 表示诊断事件中枢（§24：Infrastructure 暴露 Observable&lt;DiagnosticEvent&gt; 的应用层落点）。
/// 发布者不感知订阅者；截断是 Store 的职责（§25），hub 不截断。
/// </summary>
public sealed class DiagnosticsHub
{
    private readonly Subject<DiagnosticEvent> _subject = new();

    /// <summary>
    /// 获取诊断事件流。
    /// </summary>
    public Observable<DiagnosticEvent> Events => _subject;

    /// <summary>
    /// 发布一条诊断事件。
    /// </summary>
    /// <param name="diagnosticEvent">诊断事件。</param>
    public void Publish(DiagnosticEvent diagnosticEvent)
    {
        _subject.OnNext(diagnosticEvent);
    }
}
