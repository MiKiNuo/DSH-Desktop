using DshDesktop.Domain.Plugins;
using DshDesktop.Domain.Runtime;
using DshDesktop.Presentation.Avalonia.Features.Plugins;
using DshDesktop.Presentation.Avalonia.Features.Runtime;
using MiKiNuo.Mvi.Application.MVI.Mediator;
using MiKiNuo.Mvi.Application.MVI.Store;
using MiKiNuo.Mvi.Domain.MVI.Mediator;

namespace DshDesktop.Tests;

/// <summary>
/// Runtime 副作用分发器测试（§43.2：Effect → Mediator → Result Intent）。
/// 覆盖 ADR-0004 两段恢复链路：RecoverRuntimeEffect 仅禁用全部第三方插件（经 Mediator），
/// 成功回流 RecoverPluginsDisabled → Starting + StartRuntimeEffect 复用启动链路。
/// </summary>
public sealed class RuntimeEffectDispatcherTests
{
    private static readonly RuntimeSnapshot RunningSnapshot = new(
        RuntimeLifecycle.Running, RuntimeHealth.Healthy, RuntimeStartupStage.Ready,
        TimeSpan.FromSeconds(1), 1234, 5678, "http://127.0.0.1:5678/?token=x");

    [Test]
    public async Task RecoverRuntimeChain_DisablesPluginsThenStartsRuntime()
    {
        var mediator = new RecordingMediator();
        using var store = CreateStore(mediator);

        await store.DispatchAsync(new RuntimeIntent.RecoverRuntime());
        await WaitForAsync(() => store.CurrentState.Lifecycle is RuntimeLifecycle.Running);

        // 编排顺序（ADR-0004）：先禁用全部第三方插件，再启动 Runtime。
        await Assert.That(mediator.Requests.Count).IsEqualTo(2);
        await Assert.That(mediator.Requests[0]).IsEqualTo(nameof(DisableAllThirdPartyRequest));
        await Assert.That(mediator.Requests[1]).IsEqualTo(nameof(StartRuntimeRequest));
        await Assert.That(store.CurrentState.Lifecycle).IsEqualTo(RuntimeLifecycle.Running);
    }

    [Test]
    public async Task RecoverRuntimeChain_DisableFails_BackToFailedWithError()
    {
        var mediator = new RecordingMediator { DisableError = "禁用插件失败" };
        using var store = CreateStore(mediator);

        await store.DispatchAsync(new RuntimeIntent.RecoverRuntime());
        await WaitForAsync(() => store.CurrentState.LastError == "禁用插件失败");

        // 禁用段失败：不进入启动段，回 Failed 且用户可见失败原因（ADR-0004）。
        await Assert.That(mediator.Requests.Count).IsEqualTo(1);
        await Assert.That(store.CurrentState.Lifecycle).IsEqualTo(RuntimeLifecycle.Failed);
        await Assert.That(store.CurrentState.LastError).IsEqualTo("禁用插件失败");
    }

    [Test]
    public async Task RecoverRuntimeChain_StartFails_BackToFailedWithError()
    {
        var mediator = new RecordingMediator { StartError = "禁用后启动仍失败" };
        using var store = CreateStore(mediator);

        await store.DispatchAsync(new RuntimeIntent.RecoverRuntime());
        await WaitForAsync(() => store.CurrentState.LastError == "禁用后启动仍失败");

        await Assert.That(mediator.Requests.Count).IsEqualTo(2);
        await Assert.That(store.CurrentState.Lifecycle).IsEqualTo(RuntimeLifecycle.Failed);
        await Assert.That(store.CurrentState.LastError).IsEqualTo("禁用后启动仍失败");
    }

    private static MviStore<RuntimeState, RuntimeIntent, RuntimeEffect> CreateStore(
        RecordingMediator mediator)
    {
        RuntimeState failed = RuntimeState.Initial with
        {
            Lifecycle = RuntimeLifecycle.Failed,
            LastError = "插件导致启动失败",
        };
        return new MviStore<RuntimeState, RuntimeIntent, RuntimeEffect>(
            failed, new RuntimeReducer(), new RuntimeEffectDispatcher(mediator), []);
    }

    // ===== Phase 8 Issue 04：三策略开关走 Intent→Effect→Mediator→config 落盘（照 SetSafeMode 链路） =====

    [Test]
    public async Task ToggleKeepRuntimeOnClose_PersistsViaMediator()
    {
        var mediator = new RecordingMediator();
        using var store = CreateStore(mediator);

        await store.DispatchAsync(new RuntimeIntent.ToggleKeepRuntimeOnClose());
        await WaitForAsync(() => mediator.Requests.Contains(nameof(SetKeepRuntimeOnCloseRequest)));

        await Assert.That(store.CurrentState.KeepRuntimeOnClose).IsTrue(); // 默认关 → 翻为开
        await Assert.That(mediator.Requests).Contains(nameof(SetKeepRuntimeOnCloseRequest));
    }

    [Test]
    public async Task ToggleAutoSafeModeOnFailure_PersistsViaMediator()
    {
        var mediator = new RecordingMediator();
        using var store = CreateStore(mediator);

        await store.DispatchAsync(new RuntimeIntent.ToggleAutoSafeModeOnFailure());
        await WaitForAsync(() => mediator.Requests.Contains(nameof(SetAutoSafeModeOnFailureRequest)));

        await Assert.That(store.CurrentState.AutoSafeModeOnFailure).IsFalse();
        await Assert.That(mediator.Requests).Contains(nameof(SetAutoSafeModeOnFailureRequest));
    }

    [Test]
    public async Task ToggleCheckUpdatesOnStartup_PersistsViaMediator()
    {
        var mediator = new RecordingMediator();
        using var store = CreateStore(mediator);

        await store.DispatchAsync(new RuntimeIntent.ToggleCheckUpdatesOnStartup());
        await WaitForAsync(() => mediator.Requests.Contains(nameof(SetCheckUpdatesOnStartupRequest)));

        await Assert.That(store.CurrentState.CheckUpdatesOnStartup).IsTrue();
        await Assert.That(mediator.Requests).Contains(nameof(SetCheckUpdatesOnStartupRequest));
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 300 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }
    }

    /// <summary>
    /// 表示按序记录请求的 Mediator 测试替身（替代 PluginOrchestrator 编排断言）。
    /// </summary>
    private sealed class RecordingMediator : IMviMediator
    {
        public List<string> Requests { get; } = [];

        public string? DisableError { get; init; }

        public string? StartError { get; init; }

        /// <inheritdoc />
        public ValueTask<TResponse> SendAsync<TResponse>(
            IMviRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request.GetType().Name);
            object response = request switch
            {
                DisableAllThirdPartyRequest when DisableError is not null =>
                    throw new InvalidOperationException(DisableError),
                DisableAllThirdPartyRequest => Array.Empty<PluginInfo>(),
                StartRuntimeRequest when StartError is not null =>
                    throw new InvalidOperationException(StartError),
                StartRuntimeRequest => RunningSnapshot,
                SetKeepRuntimeOnCloseRequest or SetAutoSafeModeOnFailureRequest
                    or SetCheckUpdatesOnStartupRequest => true,
                _ => throw new NotSupportedException($"未登记的请求：{request.GetType().Name}"),
            };
            return ValueTask.FromResult((TResponse)response);
        }
    }
}
