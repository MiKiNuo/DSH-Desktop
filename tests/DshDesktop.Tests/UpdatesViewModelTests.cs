using DshDesktop.Domain.Plugins;
using DshDesktop.Domain.Updates;
using DshDesktop.Presentation.Avalonia.Features.Plugins;
using DshDesktop.Presentation.Avalonia.Features.Updates;
using MiKiNuo.Mvi.Application.MVI.Store;
using MiKiNuo.Mvi.Domain.MVI.Effect;
using MiKiNuo.Mvi.Domain.MVI.Intent;
using MiKiNuo.Mvi.Domain.MVI.State;
using R3;

namespace DshDesktop.Tests;

/// <summary>
/// Updates ViewModel 测试（🟡1：候选 3 后插件更新的在飞与文案改由 PluginsState 承载，
/// 页内警告块须经 BindSiblingState 投影 PluginsState 以恢复页内反馈）。
/// </summary>
public sealed class UpdatesViewModelTests
{
    [Test]
    public async Task BusyNotice_FallsBackToPluginOperation_WhenPluginUpdating()
    {
        // 候选 3 后 UpdatesState.PendingOperation 恒为 null（不承载插件更新在飞）；
        // BusyNotice = PendingOperation ?? PluginOperationText，须在插件更新期间恢复页内反馈。
        var updatesStore = new FakeStore<UpdatesState, UpdatesIntent, UpdatesEffect>(UpdatesState.Initial);
        var pluginsStore = new FakeStore<PluginsState, PluginsIntent, PluginsEffect>(PluginsState.Initial);
        var viewModel = new UpdatesViewModel(updatesStore, pluginsStore);

        await Assert.That(viewModel.PendingOperation).IsNull();
        await Assert.That(viewModel.PluginOperationText).IsNull();
        await Assert.That(viewModel.BusyNotice).IsNull();

        // 插件更新事务在飞（PluginsReducer.HandleUpdatePlugin 置 PendingOperation = "更新 dsh-foo…"）。
        pluginsStore.Push(PluginsState.Initial with { PendingOperation = "更新 dsh-foo…" });

        await Assert.That(viewModel.PluginOperationText).IsEqualTo("更新 dsh-foo…");
        await Assert.That(viewModel.BusyNotice).IsEqualTo("更新 dsh-foo…");
    }

    [Test]
    public async Task BusyNotice_PrefersUpdatesPendingOperation_OverPlugin()
    {
        // Updates 自有操作优先于插件事务文本（同壳文案合成口径）。
        var updatesStore = new FakeStore<UpdatesState, UpdatesIntent, UpdatesEffect>(UpdatesState.Initial);
        var pluginsStore = new FakeStore<PluginsState, PluginsIntent, PluginsEffect>(PluginsState.Initial);
        var viewModel = new UpdatesViewModel(updatesStore, pluginsStore);

        pluginsStore.Push(PluginsState.Initial with { PendingOperation = "更新 dsh-foo…" });
        updatesStore.Push(UpdatesState.Initial with { PendingOperation = "下载 Desktop 更新 0.2.0…" });

        await Assert.That(viewModel.BusyNotice).IsEqualTo("下载 Desktop 更新 0.2.0…");
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

        public FakeStore(TState initialState) => CurrentState = initialState;

        /// <inheritdoc />
        public TState CurrentState { get; private set; }

        /// <inheritdoc />
        public Observable<TState> States => _states;

        /// <summary>改写当前状态并发布状态流。</summary>
        public void Push(TState state)
        {
            CurrentState = state;
            _states.OnNext(state);
        }

        /// <inheritdoc />
        public ValueTask DispatchAsync(TIntent intent, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        /// <inheritdoc />
        public void Dispose() => _states.Dispose();
    }
}
