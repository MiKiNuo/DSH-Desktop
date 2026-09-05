using DshDesktop.Domain.Runtime;
using DshDesktop.Domain.Updates;
using DshDesktop.Presentation.Avalonia.Features.AppShell;
using DshDesktop.Presentation.Avalonia.Features.Runtime;
using DshDesktop.Presentation.Avalonia.Features.Updates;
using MiKiNuo.Mvi.Application.MVI.Effect;
using MiKiNuo.Mvi.Application.MVI.Store;
using MiKiNuo.Mvi.Domain.MVI.Effect;
using MiKiNuo.Mvi.Domain.MVI.Intent;
using MiKiNuo.Mvi.Domain.MVI.State;
using R3;

namespace DshDesktop.Tests;

/// <summary>
/// AppShell ViewModel 测试（§14 Phase 6 修订：RuntimeIndicator / UpdateBadge 经 BindSiblingState
/// 从兄弟 Store 只读投影，AppShell 不持有 Runtime/Updates 业务状态本体）。
/// </summary>
public sealed class AppShellViewModelTests
{
    [Test]
    public async Task RuntimeIndicator_TracksRuntimeStore()
    {
        var runtimeStore = new FakeStore<RuntimeState, RuntimeIntent, RuntimeEffect>(RuntimeState.Initial);
        var updatesStore = new FakeStore<UpdatesState, UpdatesIntent, UpdatesEffect>(UpdatesState.Initial);
        using var shellStore = CreateShellStore();
        var viewModel = new AppShellViewModel(shellStore, runtimeStore, updatesStore);

        await Assert.That(viewModel.RuntimeIndicator).IsEqualTo(RuntimeLifecycle.Stopped);

        runtimeStore.Push(RuntimeState.Initial with { Lifecycle = RuntimeLifecycle.Running });

        await Assert.That(viewModel.RuntimeIndicator).IsEqualTo(RuntimeLifecycle.Running);
        await Assert.That(shellStore.CurrentState.RuntimeIndicator).IsEqualTo(RuntimeLifecycle.Running);
    }

    [Test]
    public async Task UpdateBadge_CountsDesktopDshAndPluginUpdates()
    {
        var runtimeStore = new FakeStore<RuntimeState, RuntimeIntent, RuntimeEffect>(RuntimeState.Initial);
        var updatesStore = new FakeStore<UpdatesState, UpdatesIntent, UpdatesEffect>(UpdatesState.Initial);
        using var shellStore = CreateShellStore();
        var viewModel = new AppShellViewModel(shellStore, runtimeStore, updatesStore);

        await Assert.That(viewModel.UpdateBadge).IsEqualTo(0);

        updatesStore.Push(UpdatesState.Initial with
        {
            LatestDesktopVersion = "0.6.0",
            LatestDshVersion = "rc.13",
            CurrentDshVersion = "rc.12",
            PluginUpdates =
            [
                new PluginUpdateInfo("plugin-a", "1.0.0", "1.1.0"),
                new PluginUpdateInfo("plugin-b", "2.0.0", "2.1.0"),
            ],
        });

        await Assert.That(viewModel.UpdateBadge).IsEqualTo(4);
        await Assert.That(shellStore.CurrentState.UpdateBadge).IsEqualTo(4);
    }

    [Test]
    public async Task UpdateBadge_SameDshVersion_NotCounted()
    {
        var runtimeStore = new FakeStore<RuntimeState, RuntimeIntent, RuntimeEffect>(RuntimeState.Initial);
        var updatesStore = new FakeStore<UpdatesState, UpdatesIntent, UpdatesEffect>(UpdatesState.Initial);
        using var shellStore = CreateShellStore();
        var viewModel = new AppShellViewModel(shellStore, runtimeStore, updatesStore);

        updatesStore.Push(UpdatesState.Initial with
        {
            LatestDshVersion = "rc.12",
            CurrentDshVersion = "rc.12",
        });

        await Assert.That(viewModel.UpdateBadge).IsEqualTo(0);
    }

    private static MviStore<AppShellState, AppShellIntent, UnitEffect> CreateShellStore()
    {
        return new MviStore<AppShellState, AppShellIntent, UnitEffect>(
            AppShellState.Initial,
            new AppShellReducer(),
            NullEffectDispatcher.Instance,
            []);
    }

    /// <summary>
    /// 表示兄弟 Store 的测试替身：Push 改写当前状态并发布状态流（模拟真实 Store 行为）。
    /// </summary>
    private sealed class FakeStore<TState, TIntent, TEffect> : IMviStore<TState, TIntent, TEffect>
        where TState : IMviState
        where TIntent : IMviIntent
        where TEffect : IMviEffect
    {
        private readonly Subject<TState> _states = new();

        public FakeStore(TState initialState)
        {
            CurrentState = initialState;
        }

        /// <inheritdoc />
        public TState CurrentState { get; private set; }

        /// <inheritdoc />
        public Observable<TState> States => _states;

        /// <summary>
        /// 改写当前状态并发布状态流。
        /// </summary>
        /// <param name="state">新状态。</param>
        public void Push(TState state)
        {
            CurrentState = state;
            _states.OnNext(state);
        }

        /// <inheritdoc />
        public ValueTask DispatchAsync(TIntent intent, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _states.Dispose();
        }
    }
}
