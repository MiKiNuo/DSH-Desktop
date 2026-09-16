using DshDesktop.Domain.Plugins;
using DshDesktop.Domain.Runtime;
using DshDesktop.Domain.Updates;
using DshDesktop.Presentation.Avalonia.Features.AppShell;
using DshDesktop.Presentation.Avalonia.Features.Plugins;
using DshDesktop.Presentation.Avalonia.Features.Runtime;
using DshDesktop.Presentation.Avalonia.Features.Updates;
using MiKiNuo.Mvi.Application.MVI.Effect;
using MiKiNuo.Mvi.Application.MVI.Store;
using MiKiNuo.Mvi.Application.MVI.Threading;
using MiKiNuo.Mvi.Domain.MVI.Effect;
using MiKiNuo.Mvi.Domain.MVI.Intent;
using MiKiNuo.Mvi.Domain.MVI.State;
using R3;

namespace DshDesktop.Tests;

/// <summary>
/// AppShell ViewModel 测试（§14 Phase 6 修订：RuntimeIndicator / UpdateBadge 经 BindSiblingState
/// 从兄弟 Store 只读投影，AppShell 不持有 Runtime/Updates 业务状态本体）。
/// 2026-09-15 架构审查 C3：壳直订 Plugins/Updates Store 的投影（插件事务 / 更新下载）也收进 AppShell，
/// MainWindow 只订阅 VM 的 PropertyChanged。
/// </summary>
public sealed class AppShellViewModelTests
{
    [Test]
    public async Task CurrentPage_ProjectsFromStore()
    {
        // 初始页 = 工作台（AppShellState.Initial，顶部导航改造后的默认页）。
        // 页标题/副标题投影已随页标题条一并移除，此处只守页面投影本身。
        var runtimeStore = new FakeStore<RuntimeState, RuntimeIntent, RuntimeEffect>(RuntimeState.Initial);
        var updatesStore = new FakeStore<UpdatesState, UpdatesIntent, UpdatesEffect>(UpdatesState.Initial);
        var pluginsStore = new FakeStore<PluginsState, PluginsIntent, PluginsEffect>(PluginsState.Initial);
        using var shellStore = CreateShellStore();
        var viewModel = new AppShellViewModel(shellStore, runtimeStore, updatesStore, pluginsStore);

        await Assert.That(viewModel.CurrentPage).IsEqualTo(ShellPage.Workbench);

        await shellStore.DispatchAsync(new AppShellIntent.ShowSettings());

        await Assert.That(viewModel.CurrentPage).IsEqualTo(ShellPage.Settings);
    }

    [Test]
    public async Task RuntimeEndpoint_TracksRuntimeStore()
    {
        // Phase 8 Issue 02：状态栏 PID / Port 经 BindSiblingState 投影（§11.2）。
        var runtimeStore = new FakeStore<RuntimeState, RuntimeIntent, RuntimeEffect>(RuntimeState.Initial);
        var updatesStore = new FakeStore<UpdatesState, UpdatesIntent, UpdatesEffect>(UpdatesState.Initial);
        var pluginsStore = new FakeStore<PluginsState, PluginsIntent, PluginsEffect>(PluginsState.Initial);
        using var shellStore = CreateShellStore();
        var viewModel = new AppShellViewModel(shellStore, runtimeStore, updatesStore, pluginsStore);

        await Assert.That(viewModel.RuntimeProcessId).IsNull();
        await Assert.That(viewModel.RuntimePort).IsNull();

        runtimeStore.Push(RuntimeState.Initial with
        {
            Lifecycle = RuntimeLifecycle.Running,
            ProcessId = 16428,
            Port = 3080,
        });

        await Assert.That(viewModel.RuntimeProcessId).IsEqualTo(16428);
        await Assert.That(viewModel.RuntimePort).IsEqualTo(3080);
        await Assert.That(shellStore.CurrentState.RuntimeProcessId).IsEqualTo(16428);
        await Assert.That(shellStore.CurrentState.RuntimePort).IsEqualTo(3080);
    }

    [Test]
    public async Task DshVersion_TracksUpdatesStore()
    {
        // 状态栏 DSH 版本段的投影自 UpdatesStore.CurrentDshVersion。
        var runtimeStore = new FakeStore<RuntimeState, RuntimeIntent, RuntimeEffect>(RuntimeState.Initial);
        var updatesStore = new FakeStore<UpdatesState, UpdatesIntent, UpdatesEffect>(UpdatesState.Initial);
        var pluginsStore = new FakeStore<PluginsState, PluginsIntent, PluginsEffect>(PluginsState.Initial);
        using var shellStore = CreateShellStore();
        var viewModel = new AppShellViewModel(shellStore, runtimeStore, updatesStore, pluginsStore);

        await Assert.That(viewModel.DshVersion).IsNull();

        updatesStore.Push(UpdatesState.Initial with { CurrentDshVersion = "0.1.0-rc.12" });

        await Assert.That(viewModel.DshVersion).IsEqualTo("0.1.0-rc.12");
        await Assert.That(shellStore.CurrentState.DshVersion).IsEqualTo("0.1.0-rc.12");
    }

    [Test]
    public async Task RuntimeLifecycleText_FollowsIndicator()
    {
        var runtimeStore = new FakeStore<RuntimeState, RuntimeIntent, RuntimeEffect>(RuntimeState.Initial);
        var updatesStore = new FakeStore<UpdatesState, UpdatesIntent, UpdatesEffect>(UpdatesState.Initial);
        var pluginsStore = new FakeStore<PluginsState, PluginsIntent, PluginsEffect>(PluginsState.Initial);
        using var shellStore = CreateShellStore();
        var viewModel = new AppShellViewModel(shellStore, runtimeStore, updatesStore, pluginsStore);

        await Assert.That(viewModel.RuntimeLifecycleText).IsEqualTo("已停止");

        runtimeStore.Push(RuntimeState.Initial with { Lifecycle = RuntimeLifecycle.Running });

        await Assert.That(viewModel.RuntimeLifecycleText).IsEqualTo("运行中");
    }

    [Test]
    public async Task RuntimeIndicator_TracksRuntimeStore()
    {
        var runtimeStore = new FakeStore<RuntimeState, RuntimeIntent, RuntimeEffect>(RuntimeState.Initial);
        var updatesStore = new FakeStore<UpdatesState, UpdatesIntent, UpdatesEffect>(UpdatesState.Initial);
        var pluginsStore = new FakeStore<PluginsState, PluginsIntent, PluginsEffect>(PluginsState.Initial);
        using var shellStore = CreateShellStore();
        var viewModel = new AppShellViewModel(shellStore, runtimeStore, updatesStore, pluginsStore);

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
        var pluginsStore = new FakeStore<PluginsState, PluginsIntent, PluginsEffect>(PluginsState.Initial);
        using var shellStore = CreateShellStore();
        var viewModel = new AppShellViewModel(shellStore, runtimeStore, updatesStore, pluginsStore);

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
        var pluginsStore = new FakeStore<PluginsState, PluginsIntent, PluginsEffect>(PluginsState.Initial);
        using var shellStore = CreateShellStore();
        var viewModel = new AppShellViewModel(shellStore, runtimeStore, updatesStore, pluginsStore);

        updatesStore.Push(UpdatesState.Initial with
        {
            LatestDshVersion = "rc.12",
            CurrentDshVersion = "rc.12",
        });

        await Assert.That(viewModel.UpdateBadge).IsEqualTo(0);
    }

    [Test]
    public async Task UpdateInProgress_ProjectsFromPendingOperation()
    {
        // §11.2：壳经 BindSiblingState 投影 UpdatesStore.PendingOperation（非空 = 更新中），
        // 驱动全屏遮罩 + 导航锁定（NavigationBlocked 决策的单一真值源）。
        var runtimeStore = new FakeStore<RuntimeState, RuntimeIntent, RuntimeEffect>(RuntimeState.Initial);
        var updatesStore = new FakeStore<UpdatesState, UpdatesIntent, UpdatesEffect>(UpdatesState.Initial);
        var pluginsStore = new FakeStore<PluginsState, PluginsIntent, PluginsEffect>(PluginsState.Initial);
        using var shellStore = CreateShellStore();
        var viewModel = new AppShellViewModel(shellStore, runtimeStore, updatesStore, pluginsStore);

        await Assert.That(viewModel.UpdateInProgress).IsFalse();

        updatesStore.Push(UpdatesState.Initial with { PendingOperation = "安装 DSH Runtime 0.1.3…" });

        await Assert.That(viewModel.UpdateInProgress).IsTrue();
        await Assert.That(shellStore.CurrentState.UpdateInProgress).IsTrue();

        updatesStore.Push(UpdatesState.Initial with { PendingOperation = null });

        await Assert.That(viewModel.UpdateInProgress).IsFalse();
        await Assert.That(shellStore.CurrentState.UpdateInProgress).IsFalse();
    }

    // ===== 2026-09-15 架构审查 C3：插件事务 / 更新下载投影收进 AppShell =====

    [Test]
    public async Task PluginOperation_TracksPluginsStore()
    {
        // 壳 toast（插件安装事务终态）的数据源：投影必须回流自身 Store（§11.2 同四先例）。
        var runtimeStore = new FakeStore<RuntimeState, RuntimeIntent, RuntimeEffect>(RuntimeState.Initial);
        var updatesStore = new FakeStore<UpdatesState, UpdatesIntent, UpdatesEffect>(UpdatesState.Initial);
        var pluginsStore = new FakeStore<PluginsState, PluginsIntent, PluginsEffect>(PluginsState.Initial);
        using var shellStore = CreateShellStore();
        var viewModel = new AppShellViewModel(shellStore, runtimeStore, updatesStore, pluginsStore);

        await Assert.That(viewModel.PluginOperation).IsNull();

        var operation = new PluginOperation(PluginOperationStage.Completed, "demo-plugin", null);
        pluginsStore.Push(PluginsState.Initial with { Operation = operation });

        await Assert.That(viewModel.PluginOperation).IsSameReferenceAs(operation);
        await Assert.That(shellStore.CurrentState.PluginOperation).IsSameReferenceAs(operation);
    }

    [Test]
    public async Task UpdateDownload_TracksUpdatesStore()
    {
        // 更新遮罩的「旋转图标 ↔ 确定进度条」互斥与副标题：投影必须回流自身 Store。
        var runtimeStore = new FakeStore<RuntimeState, RuntimeIntent, RuntimeEffect>(RuntimeState.Initial);
        var updatesStore = new FakeStore<UpdatesState, UpdatesIntent, UpdatesEffect>(UpdatesState.Initial);
        var pluginsStore = new FakeStore<PluginsState, PluginsIntent, PluginsEffect>(PluginsState.Initial);
        using var shellStore = CreateShellStore();
        var viewModel = new AppShellViewModel(shellStore, runtimeStore, updatesStore, pluginsStore);

        await Assert.That(viewModel.UpdateDownloadPercent).IsNull();
        await Assert.That(viewModel.UpdateOperationText).IsNull();

        updatesStore.Push(UpdatesState.Initial with
        {
            DesktopDownloadProgress = 42,
            PendingOperation = "下载 Desktop 更新…",
        });

        await Assert.That(viewModel.UpdateDownloadPercent).IsEqualTo(42);
        await Assert.That(viewModel.UpdateOperationText).IsEqualTo("下载 Desktop 更新…");
        await Assert.That(shellStore.CurrentState.UpdateDownloadPercent).IsEqualTo(42);
    }

    /// <summary>
    /// 线程契约钉死（2026-09-15 架构审查 C3）：兄弟 Store 在派发线程发布 State 时，
    /// <see cref="AppShellViewModel"/> 的 [MviBind] 投影属性（如 <see cref="AppShellViewModel.RuntimeIndicator"/>）
    /// 的 PropertyChanged 是<b>不经</b> <see cref="IMviUiDispatcher.Post"/> 编组的——它直接在派发线程上直触发。
    /// 这是<b>有意的行为固化</b>（第三方库 MiKiNuo.Mvi 的行为），不是 bug。
    /// 危险面：View 回调里若触碰控件，必须自行做 CheckAccess + Post 编组，
    /// 不能依赖 VM 替它上 UI 线程。此测试把这条事实钉死，防止有人误删 View 侧编组。
    /// 对照 <see cref="OwnStoreDispatch_IsMarshaledThroughUiDispatcher"/>：自身 Store 直派发则经库 Post 编组。
    /// </summary>
    [Test]
    public async Task SiblingReflowProjection_IsNotMarshaledThroughUiDispatcher()
    {
        var dispatcher = new RecordingUiDispatcher();
        var runtimeStore = new FakeStore<RuntimeState, RuntimeIntent, RuntimeEffect>(RuntimeState.Initial);
        var updatesStore = new FakeStore<UpdatesState, UpdatesIntent, UpdatesEffect>(UpdatesState.Initial);
        var pluginsStore = new FakeStore<PluginsState, PluginsIntent, PluginsEffect>(PluginsState.Initial);
        using var shellStore = CreateShellStore();
        var viewModel = new AppShellViewModel(shellStore, runtimeStore, updatesStore, pluginsStore, dispatcher);

        // 记录 RuntimeIndicator 通知发生时的上下文（是否位于 Post 内、所在线程）。
        var indicatorEvents = new List<(bool InsidePost, int ThreadId)>();
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(AppShellViewModel.RuntimeIndicator))
            {
                indicatorEvents.Add((dispatcher.InsidePost, Environment.CurrentManagedThreadId));
            }
        };

        // 在专用后台线程推送兄弟 Store（触发 BindSiblingState 回流 → RuntimeIndicator[MviBind]）。
        int pushThreadId = 0;
        var pushThread = new Thread(() =>
        {
            pushThreadId = Environment.CurrentManagedThreadId;
            runtimeStore.Push(RuntimeState.Initial with { Lifecycle = RuntimeLifecycle.Running });
        });
        pushThread.Start();
        pushThread.Join();

        // ① 收到过 RuntimeIndicator 通知。
        await Assert.That(indicatorEvents.Count).IsGreaterThan(0);

        var e = indicatorEvents[0];

        // ② 该通知发生时未经过 UI 调度器编组（InsidePost == false）。
        //    关键断言行：RuntimeIndicator:insidePost=False@thread=<pushThreadId> (push=<pushThreadId>)
        await Assert.That(e.InsidePost).IsFalse();

        // ③ 该通知落在派发线程上（证明它直接在推送线程触发，而非被编组到 UI 线程）。
        await Assert.That(e.ThreadId).IsEqualTo(pushThreadId);

        Console.WriteLine($"RuntimeIndicator:insidePost={e.InsidePost}@thread={e.ThreadId} (push={pushThreadId})");
    }

    /// <summary>
    /// 线程契约钉死（2026-09-15 架构审查 C3 对照项）：自身 Store 在后台线程直接 DispatchAsync 时，
    /// <see cref="AppShellViewModel"/> 的 [MviBind] 投影属性（如 <see cref="AppShellViewModel.CurrentPage"/>）
    /// 的 PropertyChanged 会<strong>经</strong> <see cref="IMviUiDispatcher.Post"/> 编组（库内行为）。
    /// 与兄弟 Store 回流路径（<see cref="SiblingReflowProjection_IsNotMarshaledThroughUiDispatcher"/>）相反。
    /// </summary>
    [Test]
    public async Task OwnStoreDispatch_IsMarshaledThroughUiDispatcher()
    {
        var dispatcher = new RecordingUiDispatcher();
        var runtimeStore = new FakeStore<RuntimeState, RuntimeIntent, RuntimeEffect>(RuntimeState.Initial);
        var updatesStore = new FakeStore<UpdatesState, UpdatesIntent, UpdatesEffect>(UpdatesState.Initial);
        var pluginsStore = new FakeStore<PluginsState, PluginsIntent, PluginsEffect>(PluginsState.Initial);
        using var shellStore = CreateShellStore();
        var viewModel = new AppShellViewModel(shellStore, runtimeStore, updatesStore, pluginsStore, dispatcher);

        var currentPageEvents = new List<(bool InsidePost, int ThreadId)>();
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(AppShellViewModel.CurrentPage))
            {
                currentPageEvents.Add((dispatcher.InsidePost, Environment.CurrentManagedThreadId));
            }
        };

        // 在专用后台线程直接派发自身 Store（看 CurrentPage[MviBind] 是否经库 Post 编组）。
        int dispatchThreadId = 0;
        await Task.Run(async () =>
        {
            dispatchThreadId = Environment.CurrentManagedThreadId;
            await shellStore.DispatchAsync(new AppShellIntent.ShowSettings());
        });

        await Assert.That(currentPageEvents.Count).IsGreaterThan(0);

        var cp = currentPageEvents[0];

        // 自身 Store 直派发：CurrentPage 经库 Post 编组（InsidePost == true）。
        // 关键断言行：CurrentPage:insidePost=True@thread=<dispatchThreadId> (dispatch=<dispatchThreadId>)
        await Assert.That(cp.InsidePost).IsTrue();

        Console.WriteLine($"CurrentPage:insidePost={cp.InsidePost}@thread={cp.ThreadId} (dispatch={dispatchThreadId})");
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
    /// 表示记录型 UI 调度器：统计 Post 调用并内联执行，标记回调是否运行在 Post 内。
    /// </summary>
    private sealed class RecordingUiDispatcher : IMviUiDispatcher
    {
        public int PostCount { get; private set; }

        public bool InsidePost { get; private set; }

        public int LastPostCallerThreadId { get; private set; }

        public void Post(Action action)
        {
            PostCount++;
            LastPostCallerThreadId = Environment.CurrentManagedThreadId;
            InsidePost = true;
            try
            {
                action();
            }
            finally
            {
                InsidePost = false;
            }
        }
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
