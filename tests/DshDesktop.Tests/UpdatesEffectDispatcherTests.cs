using DshDesktop.Domain.Updates;
using DshDesktop.Presentation.Avalonia.Features.Updates;
using MiKiNuo.Mvi.Application.MVI.Mediator;
using MiKiNuo.Mvi.Application.MVI.Store;
using MiKiNuo.Mvi.Domain.MVI.Mediator;
using R3;

namespace DshDesktop.Tests;

public sealed class UpdatesEffectDispatcherTests
{
    [Test]
    public async Task PluginFailure_DoesNotFinishDesktopDownload()
    {
        var mediator = new ControlledMediator();
        using var store = new MviStore<UpdatesState, UpdatesIntent, UpdatesEffect>(
            UpdatesState.Initial with { LatestDesktopVersion = "2.0.0" },
            new UpdatesReducer(), new UpdatesEffectDispatcher(mediator), []);
        Task download = store.DispatchAsync(new UpdatesIntent.DownloadAndApplyDesktopUpdate()).AsTask();
        await mediator.OperationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await store.DispatchAsync(new UpdatesIntent.DesktopDownloadProgress(42));

        await store.DispatchAsync(new UpdatesIntent.UpdatePlugin("dsh-foo"));
        UpdatesState afterPluginFailure = store.CurrentState;

        mediator.Operation.SetException(new InvalidOperationException("operation failed"));
        await download.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(afterPluginFailure.Operation?.IsInProgress == true).IsTrue();
        await Assert.That(afterPluginFailure.DesktopDownloadProgress).IsEqualTo(42);
        await Assert.That(afterPluginFailure.LastError).IsEqualTo("plugin update failed");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CheckFailure_DoesNotFinishPendingOperation(bool install)
    {
        var mediator = new ControlledMediator();
        using var store = new MviStore<UpdatesState, UpdatesIntent, UpdatesEffect>(
            UpdatesState.Initial with { LatestDesktopVersion = "2.0.0" },
            new UpdatesReducer(), new UpdatesEffectDispatcher(mediator), []);
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = store.States.Subscribe(state =>
        {
            if (state.LastError == "check failed") failed.TrySetResult();
        });

        Task checkDispatch = store.DispatchAsync(new UpdatesIntent.CheckUpdates()).AsTask();
        await mediator.CheckStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task operationDispatch = store.DispatchAsync(install
            ? new UpdatesIntent.InstallDshRuntime("2.0.0")
            : new UpdatesIntent.DownloadAndApplyDesktopUpdate()).AsTask();
        await mediator.OperationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        string? pending = store.CurrentState.PendingOperation;
        int? progress = store.CurrentState.DesktopDownloadProgress;
        mediator.Check.SetException(new InvalidOperationException("check failed"));
        await failed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await checkDispatch.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(store.CurrentState.PendingOperation).IsEqualTo(pending);
        await Assert.That(store.CurrentState.DesktopDownloadProgress).IsEqualTo(progress);
        if (!install)
        {
            await store.DispatchAsync(new UpdatesIntent.DesktopDownloadProgress(60));
            await Assert.That(store.CurrentState.DesktopDownloadProgress).IsEqualTo(60);
        }

        var operationFailed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var terminal = store.States.Subscribe(state =>
        {
            if (state.LastError == "operation failed") operationFailed.TrySetResult();
        });
        mediator.Operation.SetException(new InvalidOperationException("operation failed"));
        await operationFailed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await operationDispatch.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(store.CurrentState.PendingOperation).IsNull();
        await Assert.That(store.CurrentState.DesktopDownloadProgress).IsNull();
    }

    private sealed class ControlledMediator : IMviMediator
    {
        public TaskCompletionSource CheckStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource OperationStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<CheckUpdatesResponse> Check { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<object> Operation { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<TResponse> SendAsync<TResponse>(IMviRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            if (request is CheckUpdatesRequest)
            {
                CheckStarted.TrySetResult();
                return (TResponse)(object)await Check.Task.WaitAsync(cancellationToken);
            }
            if (request is UpdatePluginRequest)
            {
                throw new InvalidOperationException("plugin update failed");
            }
            OperationStarted.TrySetResult();
            return (TResponse)await Operation.Task.WaitAsync(cancellationToken);
        }
    }
}
