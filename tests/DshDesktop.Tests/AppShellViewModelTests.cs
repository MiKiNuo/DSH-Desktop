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
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task OverlappingOperations_KeepMaskUntilBothFinish(bool pluginStartsFirst, bool pluginEndsFirst)
    {
        var runtimeStore = new FakeStore<RuntimeState, RuntimeIntent, RuntimeEffect>(RuntimeState.Initial);
        var updatesStore = new FakeStore<UpdatesState, UpdatesIntent, UpdatesEffect>(UpdatesState.Initial);
        var pluginsStore = new FakeStore<PluginsState, PluginsIntent, PluginsEffect>(PluginsState.Initial);
        using var shellStore = CreateShellStore();
        using var viewModel = new AppShellViewModel(shellStore, runtimeStore, updatesStore, pluginsStore);
        void Plugin(bool active) => pluginsStore.Push(PluginsState.Initial with
        {
            Operation = new PluginOperation(active ? PluginOperationStage.Preparing : PluginOperationStage.Completed, "demo", null),
        });
        void Update(bool active) => updatesStore.Push(UpdatesState.Initial with
        {
            Operation = active ? new UpdatesOperation(UpdatesOperationKind.DesktopDownload, "0.2.0") : null,
        });
        if (pluginStartsFirst) { Plugin(true); Update(true); }
        else { Update(true); Plugin(true); }
        await Assert.That(shellStore.CurrentState.UpdatesInProgress).IsTrue();
        await Assert.That(viewModel.UpdateInProgress).IsTrue();
        if (pluginEndsFirst) Plugin(false); else Update(false);
        await Assert.That(viewModel.UpdateInProgress).IsTrue();
        if (pluginEndsFirst) Update(false); else Plugin(false);
        await Assert.That(viewModel.UpdateInProgress).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task InitialOverlappingOperations_KeepMaskUntilBothFinish(bool pluginEndsFirst)
    {
        var runtimeStore = new FakeStore<RuntimeState, RuntimeIntent, RuntimeEffect>(RuntimeState.Initial);
        var updatesStore = new FakeStore<UpdatesState, UpdatesIntent, UpdatesEffect>(
            UpdatesState.Initial with { Operation = new UpdatesOperation(UpdatesOperationKind.DesktopDownload, "0.2.0") });
        var pluginsStore = new FakeStore<PluginsState, PluginsIntent, PluginsEffect>(
            PluginsState.Initial with { Operation = new PluginOperation(PluginOperationStage.Preparing, "demo", null) });
        using var shellStore = CreateShellStore();
        using var viewModel = new AppShellViewModel(shellStore, runtimeStore, updatesStore, pluginsStore);
        await Assert.That(viewModel.UpdateInProgress).IsTrue();
        await Assert.That(shellStore.CurrentState.UpdatesInProgress).IsTrue();
        if (pluginEndsFirst) pluginsStore.Push(PluginsState.Initial);
        else updatesStore.Push(UpdatesState.Initial);
        await Assert.That(viewModel.UpdateInProgress).IsTrue();
        if (pluginEndsFirst) updatesStore.Push(UpdatesState.Initial);
        else pluginsStore.Push(PluginsState.Initial);
        await Assert.That(viewModel.UpdateInProgress).IsFalse();
    }

    [Test]
    public async Task UpdateInProgress_ProjectsFromOwnOperation()
    {
        // §11.2：壳经 BindSiblingState 投影 UpdatesStore.PendingOperation（非空 = 更新中），
        // 驱动全屏遮罩 + 导航锁定（NavigationBlocked 决策的单一真值源）。
        var runtimeStore = new FakeStore<RuntimeState, RuntimeIntent, RuntimeEffect>(RuntimeState.Initial);
        var updatesStore = new FakeStore<UpdatesState, UpdatesIntent, UpdatesEffect>(UpdatesState.Initial);
        var pluginsStore = new FakeStore<PluginsState, PluginsIntent, PluginsEffect>(PluginsState.Initial);
        using var shellStore = CreateShellStore();
        var viewModel = new AppShellViewModel(shellStore, runtimeStore, updatesStore, pluginsStore);

        await Assert.That(viewModel.UpdateInProgress).IsFalse();

        updatesStore.Push(UpdatesState.Initial with
        {
            Operation = new UpdatesOperation(UpdatesOperationKind.RuntimeInstall, "0.1.3"),
        });

        await Assert.That(viewModel.UpdateInProgress).IsTrue();
        await Assert.That(shellStore.CurrentState.UpdateInProgress).IsTrue();

        updatesStore.Push(UpdatesState.Initial with
        {
            Operation = new UpdatesOperation(UpdatesOperationKind.RuntimeInstall, "0.1.3", UpdatesOperationPhase.Completed),
        });

        await Assert.That(viewModel.UpdateInProgress).IsFalse();
        await Assert.That(shellStore.CurrentState.UpdateInProgress).IsFalse();
    }

    [Test]
    public async Task UpdateInProgress_AlsoTracksPluginTransaction()
    {
        // 插件页入口只写 PluginsStore（UpdatesStore 的待办始终为空），若遮罩只认
        // UpdatesStore.PendingOperation，插件更新全程（停 Runtime → pnpm add → 重启 Runtime）
        // 就没有任何全局进度指示（2026-09-17 定位）。
        var runtimeStore = new FakeStore<RuntimeState, RuntimeIntent, RuntimeEffect>(RuntimeState.Initial);
        var updatesStore = new FakeStore<UpdatesState, UpdatesIntent, UpdatesEffect>(UpdatesState.Initial);
        var pluginsStore = new FakeStore<PluginsState, PluginsIntent, PluginsEffect>(PluginsState.Initial);
        using var shellStore = CreateShellStore();
        var viewModel = new AppShellViewModel(shellStore, runtimeStore, updatesStore, pluginsStore);

        await Assert.That(viewModel.UpdateInProgress).IsFalse();

        pluginsStore.Push(PluginsState.Initial with
        {
            Operation = new PluginOperation(PluginOperationStage.Installing, "dsh-foo", null),
        });

        await Assert.That(viewModel.UpdateInProgress).IsTrue();
        await Assert.That(shellStore.CurrentState.PluginOperationInProgress).IsTrue();

        pluginsStore.Push(PluginsState.Initial with
        {
            Operation = new PluginOperation(PluginOperationStage.Completed, "dsh-foo", null),
        });

        await Assert.That(viewModel.UpdateInProgress).IsFalse();
        await Assert.That(shellStore.CurrentState.PluginOperationInProgress).IsFalse();
    }

    [Test]
    public async Task PluginUpdate_PreparingStage_LocksMaskFromEntry()
    {
        // 🟡2：组合根在调用编排器前同步预置 Preparing 阶段（非终态），使壳遮罩自入口即锁定，
        // 消除"UpdatePlugin 意图 → 编排器首个 OperationChanged(Preparing) 回流"前的遮罩锁定窗口。
        // 此处锁定预置阶段的值类型（Preparing 非 Completed/Failed）确实拉起遮罩。
        var runtimeStore = new FakeStore<RuntimeState, RuntimeIntent, RuntimeEffect>(RuntimeState.Initial);
        var updatesStore = new FakeStore<UpdatesState, UpdatesIntent, UpdatesEffect>(UpdatesState.Initial);
        var pluginsStore = new FakeStore<PluginsState, PluginsIntent, PluginsEffect>(PluginsState.Initial);
        using var shellStore = CreateShellStore();
        var viewModel = new AppShellViewModel(shellStore, runtimeStore, updatesStore, pluginsStore);

        await Assert.That(viewModel.UpdateInProgress).IsFalse();

        // 入口预置的 Preparing 阶段：遮罩立即锁定（不依赖编排器回流）。
        pluginsStore.Push(PluginsState.Initial with
        {
            Operation = new PluginOperation(PluginOperationStage.Preparing, "dsh-foo", null, PluginOperationKind.Update),
        });

        await Assert.That(viewModel.UpdateInProgress).IsTrue();
        await Assert.That(shellStore.CurrentState.PluginOperationInProgress).IsTrue();

        // 终态回流后释放。
        pluginsStore.Push(PluginsState.Initial with
        {
            Operation = new PluginOperation(PluginOperationStage.Completed, "dsh-foo", null, PluginOperationKind.Update),
        });

        await Assert.That(viewModel.UpdateInProgress).IsFalse();
        await Assert.That(shellStore.CurrentState.PluginOperationInProgress).IsFalse();
    }

    [Test]
    public async Task PluginUpdate_InFlight_MaskNotReleasedByCheckUpdatesReflow()
    {
        // 🟡4（与 🟡2 协同）：插件更新在飞（PluginsState.Operation 非终态）时，UpdatesStore 的一次检查回流
        // （PluginOperationFinished 自带的那次 CheckUpdates 完成）不得释放壳遮罩——在飞事实只看 PluginsState.Operation，
        // 与 UpdatesStore 的检查回流无关。落点从"UpdatesState.PendingOperation"改到"PluginsState.Operation / 壳在飞投影"。
        var runtimeStore = new FakeStore<RuntimeState, RuntimeIntent, RuntimeEffect>(RuntimeState.Initial);
        var updatesStore = new FakeStore<UpdatesState, UpdatesIntent, UpdatesEffect>(UpdatesState.Initial);
        var pluginsStore = new FakeStore<PluginsState, PluginsIntent, PluginsEffect>(PluginsState.Initial);
        using var shellStore = CreateShellStore();
        var viewModel = new AppShellViewModel(shellStore, runtimeStore, updatesStore, pluginsStore);

        await Assert.That(viewModel.UpdateInProgress).IsFalse();

        // 插件事务在飞（Preparing，正是 🟡2 入口预置的阶段）。
        pluginsStore.Push(PluginsState.Initial with
        {
            Operation = new PluginOperation(PluginOperationStage.Preparing, "dsh-foo", null, PluginOperationKind.Update),
        });
        await Assert.That(viewModel.UpdateInProgress).IsTrue();
        await Assert.That(shellStore.CurrentState.PluginOperationInProgress).IsTrue();

        // 检查回流（PluginOperationFinished 自带的那次 CheckUpdates 完成，落在 UpdatesStore）。
        updatesStore.Push(UpdatesState.Initial with
        {
            Status = UpdateStatus.Checking,
            PluginUpdates = [new PluginUpdateInfo("dsh-foo", "1.0.0", "1.1.0")],
        });

        // 遮罩不释放：在飞事实归因 PluginsState.Operation，与 UpdatesStore 的检查回流无关。
        await Assert.That(viewModel.UpdateInProgress).IsTrue();
        await Assert.That(shellStore.CurrentState.PluginOperationInProgress).IsTrue();
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
            Operation = new UpdatesOperation(UpdatesOperationKind.DesktopDownload, "0.2.0", Percent: 42),
        });

        await Assert.That(viewModel.UpdateDownloadPercent).IsEqualTo(42);
        await Assert.That(viewModel.UpdateOperationText).IsEqualTo("下载 Desktop 更新 0.2.0（42%）…");
        await Assert.That(shellStore.CurrentState.UpdateDownloadPercent).IsEqualTo(42);
    }

    [Test]
    public async Task UpdateOperationText_FallsBackToPluginsPendingOperation()
    {
        // 候选 3 壳文案合成：Updates.PendingOperation ?? Plugins.PendingOperation。
        // 仅 Plugins 有在飞（插件页行内"更新"只写 PluginsStore，Updates 待办为空）时，
        // 壳遮罩副标题须回退到 Plugins.PendingOperation（"{stage}：{name}" 兜底）。
        var runtimeStore = new FakeStore<RuntimeState, RuntimeIntent, RuntimeEffect>(RuntimeState.Initial);
        var updatesStore = new FakeStore<UpdatesState, UpdatesIntent, UpdatesEffect>(UpdatesState.Initial);
        var pluginsStore = new FakeStore<PluginsState, PluginsIntent, PluginsEffect>(PluginsState.Initial);
        using var shellStore = CreateShellStore();
        var viewModel = new AppShellViewModel(shellStore, runtimeStore, updatesStore, pluginsStore);

        await Assert.That(viewModel.UpdateOperationText).IsNull();

        pluginsStore.Push(PluginsState.Initial with { PendingOperation = "卸载 dsh-foo…" });

        await Assert.That(viewModel.UpdateOperationText).IsEqualTo("卸载 dsh-foo…");
        await Assert.That(shellStore.CurrentState.UpdateOperationText).IsEqualTo("卸载 dsh-foo…");
    }

    [Test]
    public async Task UpdateOperationText_UpdatesPriorityOverPlugins()
    {
        // 壳文案合成优先序：两源皆在时 Updates.PendingOperation 优先（自有操作优先于兜底）。
        var runtimeStore = new FakeStore<RuntimeState, RuntimeIntent, RuntimeEffect>(RuntimeState.Initial);
        var updatesStore = new FakeStore<UpdatesState, UpdatesIntent, UpdatesEffect>(UpdatesState.Initial);
        var pluginsStore = new FakeStore<PluginsState, PluginsIntent, PluginsEffect>(PluginsState.Initial);
        using var shellStore = CreateShellStore();
        var viewModel = new AppShellViewModel(shellStore, runtimeStore, updatesStore, pluginsStore);

        pluginsStore.Push(PluginsState.Initial with { PendingOperation = "卸载 dsh-foo…" });
        updatesStore.Push(UpdatesState.Initial with
        {
            Operation = new UpdatesOperation(UpdatesOperationKind.DesktopDownload, "0.2.0"),
        });

        await Assert.That(viewModel.UpdateOperationText).IsEqualTo("下载 Desktop 更新 0.2.0…");
        await Assert.That(shellStore.CurrentState.UpdateOperationText).IsEqualTo("下载 Desktop 更新 0.2.0…");
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

    // ===== 候选 5：ToastRequested 事件转发（边沿判定下沉 VM） =====

    [Test]
    public async Task ToastRequested_NotRaisedDuringConstruction_FirstFrameSuppressed()
    {
        // 首帧（构造期初始投影）不弹：构造 VM 期间不得触发任何 ToastRequested。
        var runtimeStore = new FakeStore<RuntimeState, RuntimeIntent, RuntimeEffect>(RuntimeState.Initial);
        var updatesStore = new FakeStore<UpdatesState, UpdatesIntent, UpdatesEffect>(UpdatesState.Initial);
        var pluginsStore = new FakeStore<PluginsState, PluginsIntent, PluginsEffect>(PluginsState.Initial);
        using var shellStore = CreateShellStore();

        var raised = false;
        var viewModel = new AppShellViewModel(shellStore, runtimeStore, updatesStore, pluginsStore);
        viewModel.ToastRequested += (_, _) => raised = true;

        // 构造已结束并订阅，首帧不应漏弹。
        await Assert.That(raised).IsFalse();
    }

    [Test]
    public async Task ToastRequested_RaisedOnBadgeRisingEdge()
    {
        var runtimeStore = new FakeStore<RuntimeState, RuntimeIntent, RuntimeEffect>(RuntimeState.Initial);
        var updatesStore = new FakeStore<UpdatesState, UpdatesIntent, UpdatesEffect>(UpdatesState.Initial);
        var pluginsStore = new FakeStore<PluginsState, PluginsIntent, PluginsEffect>(PluginsState.Initial);
        using var shellStore = CreateShellStore();
        var viewModel = new AppShellViewModel(shellStore, runtimeStore, updatesStore, pluginsStore);

        string? captured = null;
        viewModel.ToastRequested += (_, text) => captured = text;

        // 徽标 0 → 1（1 条插件更新）：上升沿弹一条。
        updatesStore.Push(UpdatesState.Initial with
        {
            PluginUpdates = [new PluginUpdateInfo("plugin-a", "1.0.0", "1.1.0")],
        });

        await Assert.That(captured).IsEqualTo("发现 1 项可用更新");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RecoveryToast_FollowsRealReducerSequence(bool fail)
    {
        var runtimeStore = new FakeStore<RuntimeState, RuntimeIntent, RuntimeEffect>(RuntimeState.Initial);
        var updatesStore = new FakeStore<UpdatesState, UpdatesIntent, UpdatesEffect>(UpdatesState.Initial);
        var pluginsStore = new FakeStore<PluginsState, PluginsIntent, PluginsEffect>(PluginsState.Initial);
        using var shellStore = CreateShellStore();
        using var viewModel = new AppShellViewModel(shellStore, runtimeStore, updatesStore, pluginsStore);
        var messages = new List<string>();
        viewModel.ToastRequested += (_, text) => messages.Add(text);
        var reducer = new RuntimeReducer();
        RuntimeState state = RuntimeState.Initial;
        void Apply(RuntimeIntent intent)
        {
            state = reducer.Reduce(state, intent).State;
            runtimeStore.Push(state);
        }
        Apply(new RuntimeIntent.RuntimeFailed("initial failure"));
        Apply(new RuntimeIntent.RecoverRuntime());
        Apply(new RuntimeIntent.RecoverPluginsDisabled());
        await Assert.That(state.Lifecycle).IsEqualTo(RuntimeLifecycle.Starting);
        await Assert.That(messages.Count).IsEqualTo(0);
        if (fail)
        {
            Apply(new RuntimeIntent.RuntimeFailed("recovery failed"));
            Apply(new RuntimeIntent.StartRuntime());
        }
        Apply(new RuntimeIntent.RuntimeStarted(123, 456, "http://localhost:456/"));
        Apply(new RuntimeIntent.RuntimeStarted(123, 456, "http://localhost:456/"));
        await Assert.That(messages.Count).IsEqualTo(fail ? 0 : 1);
        if (!fail) await Assert.That(messages[0]).IsEqualTo("Runtime 已恢复运行");
    }

    [Test]
    public async Task RecoveryStopped_ThenOrdinaryStart_DoesNotShowRecoveryToast()
    {
        var runtimeStore = new FakeStore<RuntimeState, RuntimeIntent, RuntimeEffect>(RuntimeState.Initial);
        var updatesStore = new FakeStore<UpdatesState, UpdatesIntent, UpdatesEffect>(UpdatesState.Initial);
        var pluginsStore = new FakeStore<PluginsState, PluginsIntent, PluginsEffect>(PluginsState.Initial);
        using var shellStore = CreateShellStore();
        using var viewModel = new AppShellViewModel(shellStore, runtimeStore, updatesStore, pluginsStore);
        var messages = new List<string>();
        viewModel.ToastRequested += (_, text) => messages.Add(text);
        var reducer = new RuntimeReducer();
        RuntimeState state = RuntimeState.Initial;
        void Apply(RuntimeIntent intent)
        {
            state = reducer.Reduce(state, intent).State;
            runtimeStore.Push(state);
        }
        Apply(new RuntimeIntent.RuntimeFailed("initial failure"));
        Apply(new RuntimeIntent.RecoverRuntime());
        Apply(new RuntimeIntent.RecoverPluginsDisabled());
        Apply(new RuntimeIntent.RuntimeStopOrchestrated());
        Apply(new RuntimeIntent.RuntimeExited(0));
        await Assert.That(state.Lifecycle).IsEqualTo(RuntimeLifecycle.Stopped);
        Apply(new RuntimeIntent.StartRuntime());
        Apply(new RuntimeIntent.RuntimeStarted(123, 456, "http://localhost:456/"));
        await Assert.That(state.Lifecycle).IsEqualTo(RuntimeLifecycle.Running);
        await Assert.That(messages.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ToastRequested_RaisedOnLifecycleRecovered()
    {
        var runtimeStore = new FakeStore<RuntimeState, RuntimeIntent, RuntimeEffect>(RuntimeState.Initial);
        var updatesStore = new FakeStore<UpdatesState, UpdatesIntent, UpdatesEffect>(UpdatesState.Initial);
        var pluginsStore = new FakeStore<PluginsState, PluginsIntent, PluginsEffect>(PluginsState.Initial);
        using var shellStore = CreateShellStore();
        var viewModel = new AppShellViewModel(shellStore, runtimeStore, updatesStore, pluginsStore);

        string? captured = null;
        viewModel.ToastRequested += (_, text) => captured = text;

        // Recovering → Running 迁移才弹（首帧 Stopped→Recovering 不弹）。
        runtimeStore.Push(RuntimeState.Initial with { Lifecycle = RuntimeLifecycle.Recovering });
        runtimeStore.Push(RuntimeState.Initial with { Lifecycle = RuntimeLifecycle.Running });

        await Assert.That(captured).IsEqualTo("Runtime 已恢复运行");
    }

    [Test]
    public async Task ToastRequested_RaisedOnPluginOperationCompleted()
    {
        var runtimeStore = new FakeStore<RuntimeState, RuntimeIntent, RuntimeEffect>(RuntimeState.Initial);
        var updatesStore = new FakeStore<UpdatesState, UpdatesIntent, UpdatesEffect>(UpdatesState.Initial);
        var pluginsStore = new FakeStore<PluginsState, PluginsIntent, PluginsEffect>(PluginsState.Initial);
        using var shellStore = CreateShellStore();
        var viewModel = new AppShellViewModel(shellStore, runtimeStore, updatesStore, pluginsStore);

        string? captured = null;
        viewModel.ToastRequested += (_, text) => captured = text;

        var operation = new PluginOperation(PluginOperationStage.Completed, "demo-plugin", null);
        pluginsStore.Push(PluginsState.Initial with { Operation = operation });

        await Assert.That(captured).IsEqualTo("插件 demo-plugin 安装完成");
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
