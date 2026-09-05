using DshDesktop.Domain.Runtime;
using DshDesktop.Presentation.Avalonia.Features.Runtime;
using DshDesktop.Presentation.Avalonia.Features.Workbench;
using MiKiNuo.Mvi.Application.MVI.Effect;
using MiKiNuo.Mvi.Application.MVI.Store;
using MiKiNuo.Mvi.Domain.MVI.Effect;
using R3;

namespace DshDesktop.Tests;

/// <summary>
/// Workbench ViewModel 测试（§21 Phase 6 修订：Reload 从 Runtime 投影取最新 Session URL，禁止缓存旧 URL）。
/// </summary>
public sealed class WorkbenchViewModelTests
{
    [Test]
    public async Task Reload_UsesLatestRuntimeSessionUrl_NotCached()
    {
        var runtimeStore = new FakeRuntimeStore(RuntimeState.Initial with
        {
            Lifecycle = RuntimeLifecycle.Running,
            Url = "http://127.0.0.1:5000/?token=old",
        });
        using var workbenchStore = CreateWorkbenchStore();
        var viewModel = new WorkbenchViewModel(workbenchStore, runtimeStore);

        // Runtime 重启后 Session URL 已变化，但投影事件尚未送达（模拟通知时序）：
        // Reload 必须读取 Runtime Store 当前状态，而不是构造期缓存的旧 URL。
        runtimeStore.OverwriteCurrentState(runtimeStore.CurrentState with
        {
            Url = "http://127.0.0.1:5000/?token=new",
        });

        string? reloadUrl = null;
        viewModel.ReloadRequested += url => reloadUrl = url;
        viewModel.RequestReload();

        await Assert.That(reloadUrl).IsEqualTo("http://127.0.0.1:5000/?token=new");
        await Assert.That(workbenchStore.CurrentState.Loading).IsTrue();
    }

    [Test]
    public async Task Reload_WhenRuntimeNotRunning_DoesNothing()
    {
        var runtimeStore = new FakeRuntimeStore(RuntimeState.Initial);
        using var workbenchStore = CreateWorkbenchStore();
        var viewModel = new WorkbenchViewModel(workbenchStore, runtimeStore);

        bool raised = false;
        viewModel.ReloadRequested += _ => raised = true;
        viewModel.RequestReload();

        await Assert.That(raised).IsFalse();
        await Assert.That(workbenchStore.CurrentState.Loading).IsFalse();
    }

    [Test]
    public async Task RuntimeProjection_TracksRuntimeStore()
    {
        var runtimeStore = new FakeRuntimeStore(RuntimeState.Initial);
        using var workbenchStore = CreateWorkbenchStore();
        var viewModel = new WorkbenchViewModel(workbenchStore, runtimeStore);

        await Assert.That(viewModel.RuntimeReady).IsFalse();
        await Assert.That(viewModel.DshUrl).IsNull();

        runtimeStore.Push(RuntimeState.Initial with
        {
            Lifecycle = RuntimeLifecycle.Running,
            Url = "http://127.0.0.1:5000/?token=a",
        });

        await Assert.That(viewModel.RuntimeReady).IsTrue();
        await Assert.That(viewModel.DshUrl).IsEqualTo("http://127.0.0.1:5000/?token=a");
    }

    private static MviStore<WorkbenchState, WorkbenchIntent, UnitEffect> CreateWorkbenchStore()
    {
        return new MviStore<WorkbenchState, WorkbenchIntent, UnitEffect>(
            WorkbenchState.Initial,
            new WorkbenchReducer(),
            NullEffectDispatcher.Instance,
            []);
    }

    /// <summary>
    /// 表示 Runtime Store 的测试替身：CurrentState 可静默改写（不通知），Push 模拟真实的状态发布。
    /// </summary>
    private sealed class FakeRuntimeStore : IMviStore<RuntimeState, RuntimeIntent, RuntimeEffect>
    {
        private readonly Subject<RuntimeState> _states = new();

        public FakeRuntimeStore(RuntimeState initialState)
        {
            CurrentState = initialState;
        }

        /// <inheritdoc />
        public RuntimeState CurrentState { get; private set; }

        /// <inheritdoc />
        public Observable<RuntimeState> States => _states;

        /// <summary>
        /// 静默改写当前状态（不发布状态流），用于验证消费方不依赖通知时序或缓存。
        /// </summary>
        /// <param name="state">新状态。</param>
        public void OverwriteCurrentState(RuntimeState state)
        {
            CurrentState = state;
        }

        /// <summary>
        /// 改写当前状态并发布状态流（模拟真实 Store 行为）。
        /// </summary>
        /// <param name="state">新状态。</param>
        public void Push(RuntimeState state)
        {
            CurrentState = state;
            _states.OnNext(state);
        }

        /// <inheritdoc />
        public ValueTask DispatchAsync(RuntimeIntent intent, CancellationToken cancellationToken = default)
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
