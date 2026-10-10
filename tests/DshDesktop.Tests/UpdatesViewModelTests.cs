using DshDesktop.Domain.Updates;
using DshDesktop.Presentation.Avalonia.Features.Updates;
using MiKiNuo.Mvi.Application.MVI.Mediator;
using MiKiNuo.Mvi.Application.MVI.Store;
using MiKiNuo.Mvi.Domain.MVI.Mediator;
using R3;

namespace DshDesktop.Tests;

/// <summary>
/// Updates ViewModel 测试。
/// </summary>
public sealed class UpdatesViewModelTests
{
    [Test]
    public async Task InstallLatestRuntime_CompletedThenRefreshed_RequestsActivationOnce()
    {
        IReadOnlyList<DshRuntimeInfo> runtimes =
        [
            new("0.1.2", IsActive: true, IsBorrowed: false),
            new("0.1.3", IsActive: false, IsBorrowed: false),
        ];
        using var updatesStore = new MviStore<UpdatesState, UpdatesIntent, UpdatesEffect>(
            UpdatesState.Initial with { LatestDshVersion = "0.1.3", CurrentDshVersion = "0.1.2" },
            new UpdatesReducer(), new UpdatesEffectDispatcher(new InstallMediator(runtimes)), []);
        using var viewModel = new UpdatesViewModel(updatesStore);
        var prompts = new List<string>();
        viewModel.ActivationConfirmationRequested += (_, version) => prompts.Add(version);
        var installed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = updatesStore.States.Subscribe(state =>
        {
            if (state.DshStage is DshRuntimeStage.ReadyToActivate && state.PendingOperation is null)
                installed.TrySetResult();
        });

        viewModel.InstallLatestRuntime();
        await installed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await updatesStore.DispatchAsync(new UpdatesIntent.CheckUpdatesCompleted(
            new CheckUpdatesResponse("0.1.3", "0.1.2", runtimes, null)));
        await updatesStore.DispatchAsync(new UpdatesIntent.RuntimeListChanged(runtimes));

        await Assert.That(prompts.Count).IsEqualTo(1);
        await Assert.That(prompts[0]).IsEqualTo("0.1.3");
    }

    private sealed class InstallMediator(IReadOnlyList<DshRuntimeInfo> runtimes) : IMviMediator
    {
        public ValueTask<TResponse> SendAsync<TResponse>(IMviRequest<TResponse> request,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult((TResponse)(object)runtimes);
    }
}
