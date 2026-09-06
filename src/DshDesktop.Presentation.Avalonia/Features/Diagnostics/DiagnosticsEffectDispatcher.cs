using DshDesktop.Domain.Diagnostics;
using MiKiNuo.Mvi.Application.MVI.Effect;
using MiKiNuo.Mvi.Application.MVI.Mediator;
using MiKiNuo.Mvi.Domain.MVI.Effect;

namespace DshDesktop.Presentation.Avalonia.Features.Diagnostics;

/// <summary>
/// 表示 Diagnostics 副作用分发器（§10 桥梁；编排经 Mediator 路由到组合根）。
/// 失败不静默：以错误级诊断事件回流，直接显示在 Live 控制台。
/// </summary>
public sealed partial class DiagnosticsEffectDispatcher
    : MviEffectDispatcherBase<DiagnosticsIntent, DiagnosticsEffect>
{
    private readonly IMviMediator _mediator;

    /// <summary>
    /// 初始化 Diagnostics 副作用分发器。
    /// </summary>
    /// <param name="mediator">跨层协调中介者。</param>
    public DiagnosticsEffectDispatcher(IMviMediator mediator)
    {
        ArgumentNullException.ThrowIfNull(mediator);
        _mediator = mediator;
    }

    /// <summary>
    /// 处理运行诊断副作用。
    /// </summary>
    [MviEffect(typeof(DiagnosticsEffect.RunDiagnosis))]
    private async ValueTask HandleRunDiagnosis(
        DiagnosticsEffect.RunDiagnosis effect,
        CancellationToken cancellationToken)
    {
        try
        {
            _ = await _mediator.SendAsync(new RunDiagnosisRequest(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await DispatchErrorAsync(exception.Message, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 处理导出诊断包副作用。
    /// </summary>
    [MviEffect(typeof(DiagnosticsEffect.ExportBundle))]
    private async ValueTask HandleExportBundle(
        DiagnosticsEffect.ExportBundle effect,
        CancellationToken cancellationToken)
    {
        try
        {
            _ = await _mediator
                .SendAsync(new ExportDiagnosticsBundleRequest(effect.DestinationPath), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await DispatchErrorAsync(exception.Message, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 处理打开日志目录副作用。
    /// </summary>
    [MviEffect(typeof(DiagnosticsEffect.OpenLogsDirectory))]
    private async ValueTask HandleOpenLogsDirectory(
        DiagnosticsEffect.OpenLogsDirectory effect,
        CancellationToken cancellationToken)
    {
        try
        {
            _ = await _mediator.SendAsync(new OpenLogsDirectoryRequest(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await DispatchErrorAsync(exception.Message, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask DispatchErrorAsync(string message, CancellationToken cancellationToken)
    {
        await DispatchIntentAsync(
            new DiagnosticsIntent.DiagnosticEventReceived(new DiagnosticEvent(
                DateTimeOffset.Now, DiagnosticSource.App, DiagnosticLevel.Error, message)),
            cancellationToken).ConfigureAwait(false);
    }
}
