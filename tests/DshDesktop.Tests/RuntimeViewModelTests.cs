using DshDesktop.Domain.Runtime;
using DshDesktop.Presentation.Avalonia.Features.Runtime;
using DshDesktop.Presentation.Avalonia.Features.Updates;
using MiKiNuo.Mvi.Application.MVI.Store;
using MiKiNuo.Mvi.Domain.MVI.Effect;
using MiKiNuo.Mvi.Domain.MVI.Intent;
using MiKiNuo.Mvi.Domain.MVI.State;
using R3;

namespace DshDesktop.Tests;

/// <summary>
/// Runtime 页主操作按钮契约（用户症状 2026-09-28：运行环境页点「重启 Runtime」无任何反馈）。
///
/// 根因：按钮在全部 6 个生命周期状态下恒可用（生成体 CanExecute = 常量 true），而
/// <c>RuntimeReducer</c> 只放行 Running / Failed —— 其余状态下点击被静默丢弃
/// （无状态变化、无 Effect、无日志、无提示），用户看到的就是"点了没反应"。
///
/// 契约：按钮文案与可用性必须与 Reducer 守卫一致 —— 非法状态要**可见**（禁用），
/// Stopped 要走既有 StartRuntime 链路（按钮文案切换为「启动 DSH」），而不是继续无声无息。
/// </summary>
public sealed class RuntimeViewModelTests
{
    [Test]
    [Arguments(RuntimeLifecycle.Stopped, true, false, "启动 DSH", true)]
    [Arguments(RuntimeLifecycle.Running, false, true, "重启 Runtime", true)]
    [Arguments(RuntimeLifecycle.Failed, false, true, "重启 Runtime", true)]
    [Arguments(RuntimeLifecycle.Starting, false, false, "重启 Runtime", false)]
    [Arguments(RuntimeLifecycle.Stopping, false, false, "重启 Runtime", false)]
    [Arguments(RuntimeLifecycle.Recovering, false, false, "重启 Runtime", false)]
    public async Task PrimaryAction_FollowsLifecycleGuard(
        RuntimeLifecycle lifecycle,
        bool canStart,
        bool canRestart,
        string text,
        bool enabled)
    {
        using var harness = CreateViewModel(lifecycle);

        await Assert.That(harness.ViewModel.CanStartRuntime).IsEqualTo(canStart);
        await Assert.That(harness.ViewModel.CanRestartRuntime).IsEqualTo(canRestart);
        await Assert.That(harness.ViewModel.PrimaryActionText).IsEqualTo(text);
        await Assert.That(harness.ViewModel.IsPrimaryActionEnabled).IsEqualTo(enabled);
    }

    [Test]
    public async Task PrimaryAction_NotifiesDerivedProjectionsOnLifecycleChange()
    {
        using var harness = CreateViewModel(RuntimeLifecycle.Running);
        var changed = new List<string>();
        harness.ViewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName!);

        harness.RuntimeStore.Push(State(RuntimeLifecycle.Stopped));

        // 派生投影必须随生命周期联动刷新，否则 View 停在旧文案/旧可用性上。
        await Assert.That(changed).Contains(nameof(RuntimeViewModel.PrimaryActionText));
        await Assert.That(changed).Contains(nameof(RuntimeViewModel.IsPrimaryActionEnabled));
        await Assert.That(harness.ViewModel.PrimaryActionText).IsEqualTo("启动 DSH");
    }

    /// <summary>
    /// 双向对账：以真实 <see cref="RuntimeReducer"/> 为独立事实源（而非再手写一份期望值），
    /// 锁死「投影 ↔ 守卫」不能失同步返祖：
    /// ① 投影说可执行 ⇒ Reducer 必须真的产出 Effect（否则就是本次症状：点击被静默丢弃）；
    /// ② 投影说不可执行 ⇒ Start 与 Restart 都必须被 Reducer 忽略（否则禁用按钮把合法动作藏了）。
    /// </summary>
    [Test]
    public async Task PrimaryAction_NeitherSilentlyDropsNorHidesALegalAction()
    {
        var reducer = new RuntimeReducer();
        var violations = new List<string>();

        foreach (RuntimeLifecycle lifecycle in Enum.GetValues<RuntimeLifecycle>())
        {
            RuntimeState state = State(lifecycle);
            using var harness = CreateViewModel(lifecycle);
            RuntimeViewModel viewModel = harness.ViewModel;

            int startEffects = reducer.Reduce(state, new RuntimeIntent.StartRuntime()).Effects.Count;
            int restartEffects = reducer.Reduce(state, new RuntimeIntent.RestartRuntime()).Effects.Count;

            if (viewModel.CanStartRuntime && startEffects != 1)
            {
                violations.Add($"{lifecycle}：CanStartRuntime=true 但 Start 只产出 {startEffects} 个 Effect");
            }

            if (viewModel.CanRestartRuntime && restartEffects != 1)
            {
                violations.Add($"{lifecycle}：CanRestartRuntime=true 但 Restart 只产出 {restartEffects} 个 Effect");
            }

            if (!viewModel.IsPrimaryActionEnabled && (startEffects != 0 || restartEffects != 0))
            {
                violations.Add($"{lifecycle}：按钮禁用却仍有合法动作（Start {startEffects} / Restart {restartEffects}）");
            }
        }

        await Assert.That(string.Join("；", violations)).IsEmpty();
    }

    private static RuntimeState State(RuntimeLifecycle lifecycle)
        => RuntimeState.Initial with { Lifecycle = lifecycle };

    private static Harness CreateViewModel(RuntimeLifecycle lifecycle)
    {
        var runtimeStore = new FakeStore<RuntimeState, RuntimeIntent, RuntimeEffect>(State(lifecycle));
        var updatesStore = new FakeStore<UpdatesState, UpdatesIntent, UpdatesEffect>(UpdatesState.Initial);
        return new Harness(runtimeStore, new RuntimeViewModel(runtimeStore, updatesStore));
    }

    private sealed class Harness(
        FakeStore<RuntimeState, RuntimeIntent, RuntimeEffect> runtimeStore,
        RuntimeViewModel viewModel)
        : IDisposable
    {
        public FakeStore<RuntimeState, RuntimeIntent, RuntimeEffect> RuntimeStore { get; } = runtimeStore;

        public RuntimeViewModel ViewModel { get; } = viewModel;

        public void Dispose()
        {
            RuntimeStore.Dispose();
            ViewModel.Dispose();
        }
    }

    /// <summary>
    /// 表示 Store 的测试替身：Push 改写当前状态并发布状态流（同 DashboardViewModelTests 先例）。
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
