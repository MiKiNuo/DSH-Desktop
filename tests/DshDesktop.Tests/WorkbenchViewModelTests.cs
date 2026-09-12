using DshDesktop.Domain.Runtime;
using DshDesktop.Presentation.Avalonia.Features.Runtime;
using DshDesktop.Presentation.Avalonia.Features.Workbench;
using MiKiNuo.Mvi.Application.MVI.Effect;
using MiKiNuo.Mvi.Application.MVI.Store;
using MiKiNuo.Mvi.Domain.MVI.Effect;
using R3;

namespace DshDesktop.Tests;

/// <summary>
/// Workbench ViewModel 测试（§21 Phase 6 修订：Runtime 投影驱动 WebView 导航）。
/// 用户精简掉工具条后，Refresh / GoBack / GoForward 通道已删除，RuntimeReady 也失去唯一消费方——
/// ViewModel 只剩一条对外的 DshUrl 投影：非 null 导航，null 退回占位层。
/// </summary>
public sealed class WorkbenchViewModelTests
{
    [Test]
    public async Task DshUrlProjection_FollowsRuntimeStore()
    {
        var runtimeStore = new FakeRuntimeStore(RuntimeState.Initial);
        using var workbenchStore = CreateWorkbenchStore();
        var viewModel = new WorkbenchViewModel(workbenchStore, runtimeStore);

        await Assert.That(viewModel.DshUrl).IsNull();

        runtimeStore.Push(RuntimeState.Initial with
        {
            Lifecycle = RuntimeLifecycle.Running,
            Url = "http://127.0.0.1:5000/?token=a",
        });

        await Assert.That(viewModel.DshUrl).IsEqualTo("http://127.0.0.1:5000/?token=a");
    }

    /// <summary>
    /// Runtime 停止后投影必须把 DshUrl 置空，View 才能退回占位层。
    /// Session URL 含一次性 token，停止后旧 URL 不可复用，不能留在属性上。
    /// </summary>
    [Test]
    public async Task DshUrlProjection_ClearsUrlWhenRuntimeStops()
    {
        var runtimeStore = new FakeRuntimeStore(RuntimeState.Initial with
        {
            Lifecycle = RuntimeLifecycle.Running,
            Url = "http://127.0.0.1:5000/?token=a",
        });
        using var workbenchStore = CreateWorkbenchStore();
        var viewModel = new WorkbenchViewModel(workbenchStore, runtimeStore);
        await Assert.That(viewModel.DshUrl).IsNotNull();

        runtimeStore.Push(RuntimeState.Initial with { Lifecycle = RuntimeLifecycle.Stopped });

        await Assert.That(viewModel.DshUrl).IsNull();
    }

    /// <summary>
    /// Runtime 处于 Running 但尚无 URL（端口未就绪）时同样退占位层——
    /// 不能把 null URL 当作可导航地址。
    /// </summary>
    [Test]
    public async Task DshUrlProjection_RunningWithoutUrl_StaysNull()
    {
        var runtimeStore = new FakeRuntimeStore(RuntimeState.Initial with
        {
            Lifecycle = RuntimeLifecycle.Running,
            Url = null,
        });
        using var workbenchStore = CreateWorkbenchStore();
        var viewModel = new WorkbenchViewModel(workbenchStore, runtimeStore);

        await Assert.That(viewModel.DshUrl).IsNull();
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
