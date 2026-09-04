using MiKiNuo.Mvi.Application.MVI.Effect;
using MiKiNuo.Mvi.Application.MVI.Mediator;
using MiKiNuo.Mvi.Domain.MVI.Effect;

namespace DshDesktop.Presentation.Avalonia.Features.Runtime;

/// <summary>
/// 表示 Runtime 副作用分发器（§10：MVI 与 Application 之间的桥梁）。
/// </summary>
/// <remarks>
/// 库约束：Feature 组件只能解析同程序集服务 + IMviMediator + 各 Feature Store，
/// 因此编排调用经 Mediator 路由到组合根注册的 IRuntimeOrchestrator 处理器（§28），
/// Presentation 不引用 Infrastructure。
/// </remarks>
public sealed partial class RuntimeEffectDispatcher
    : MviEffectDispatcherBase<RuntimeIntent, RuntimeEffect>
{
    private readonly IMviMediator _mediator;

    /// <summary>
    /// 初始化 Runtime 副作用分发器。
    /// </summary>
    /// <param name="mediator">跨层协调中介者。</param>
    public RuntimeEffectDispatcher(IMviMediator mediator)
    {
        ArgumentNullException.ThrowIfNull(mediator);
        _mediator = mediator;
    }

    /// <summary>
    /// 处理启动 Runtime 副作用：经 Mediator 调用编排，结果回流 Store。
    /// </summary>
    [MviEffect(typeof(RuntimeEffect.StartRuntime))]
    private async ValueTask HandleStartRuntime(
        RuntimeEffect.StartRuntime effect,
        CancellationToken cancellationToken)
    {
        try
        {
            DshDesktop.Domain.Runtime.RuntimeSnapshot snapshot = await _mediator
                .SendAsync(new StartRuntimeRequest(), cancellationToken)
                .ConfigureAwait(false);

            await DispatchIntentAsync(
                new RuntimeIntent.RuntimeStarted(
                    snapshot.ProcessId ?? 0,
                    snapshot.Port ?? 0,
                    snapshot.Url ?? string.Empty),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await DispatchIntentAsync(
                new RuntimeIntent.RuntimeFailed(exception.Message),
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 处理停止 Runtime 副作用：经 Mediator 停止编排；
    /// 进程退出由组合根的 Exited 订阅统一回流 RuntimeExited，此处不重复回流。
    /// </summary>
    [MviEffect(typeof(RuntimeEffect.StopRuntime))]
    private async ValueTask HandleStopRuntime(
        RuntimeEffect.StopRuntime effect,
        CancellationToken cancellationToken)
    {
        try
        {
            _ = await _mediator
                .SendAsync(new StopRuntimeRequest(), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await DispatchIntentAsync(
                new RuntimeIntent.RuntimeFailed(exception.Message),
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 处理持久化安全模式副作用；成功状态由组合根回流 SafeModeChanged。
    /// </summary>
    [MviEffect(typeof(RuntimeEffect.SetSafeMode))]
    private async ValueTask HandleSetSafeMode(
        RuntimeEffect.SetSafeMode effect,
        CancellationToken cancellationToken)
    {
        try
        {
            _ = await _mediator
                .SendAsync(new SetSafeModeRequest(effect.Enabled), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await DispatchIntentAsync(
                new RuntimeIntent.RuntimeOperationFailed(exception.Message),
                cancellationToken).ConfigureAwait(false);
        }
    }
}
