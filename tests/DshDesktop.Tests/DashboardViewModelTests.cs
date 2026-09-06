using DshDesktop.Application.Runtime;
using DshDesktop.Domain.Diagnostics;
using DshDesktop.Domain.Plugins;
using DshDesktop.Domain.Runtime;
using DshDesktop.Domain.Updates;
using DshDesktop.Presentation.Avalonia.Features.AppShell;
using DshDesktop.Presentation.Avalonia.Features.Dashboard;
using DshDesktop.Presentation.Avalonia.Features.Diagnostics;
using DshDesktop.Presentation.Avalonia.Features.Plugins;
using DshDesktop.Presentation.Avalonia.Features.Runtime;
using DshDesktop.Presentation.Avalonia.Features.Updates;
using MiKiNuo.Mvi.Application.MVI.Mediator;
using MiKiNuo.Mvi.Application.MVI.Store;
using MiKiNuo.Mvi.Domain.MVI.Effect;
using MiKiNuo.Mvi.Domain.MVI.Intent;
using MiKiNuo.Mvi.Domain.MVI.State;
using R3;

namespace DshDesktop.Tests;

/// <summary>
/// Dashboard ViewModel 测试（Phase 8 Issue 03）：独立 MVI 三元组 + BindSiblingState
/// 投影 Runtime / Updates / Plugins / Diagnostics 四个兄弟 Store 只读投影（§11.2）。
/// </summary>
public sealed class DashboardViewModelTests
{
    [Test]
    public async Task RuntimeProjection_TracksRuntimeStore()
    {
        var runtimeStore = new FakeStore<RuntimeState, RuntimeIntent, RuntimeEffect>(RuntimeState.Initial);
        using var harness = CreateViewModel(runtimeStore);

        await Assert.That(harness.ViewModel.Lifecycle).IsEqualTo(RuntimeLifecycle.Stopped);
        await Assert.That(harness.ViewModel.HeroTitle).IsEqualTo("DSH 服务未运行");

        runtimeStore.Push(RuntimeState.Initial with
        {
            Lifecycle = RuntimeLifecycle.Running,
            Health = RuntimeHealth.Healthy,
            Port = 3080,
            StartupElapsed = TimeSpan.FromMilliseconds(1820),
        });

        await Assert.That(harness.ViewModel.Lifecycle).IsEqualTo(RuntimeLifecycle.Running);
        await Assert.That(harness.ViewModel.HeroTitle).IsEqualTo("DSH 服务已就绪");
        await Assert.That(harness.ViewModel.HeroSubtitle.Contains("127.0.0.1:3080")).IsTrue();
        await Assert.That(harness.ViewModel.HealthText).IsEqualTo("● 正常");
        await Assert.That(harness.ViewModel.StartupElapsedText).IsEqualTo("1.82s");
    }

    [Test]
    public async Task UpdatesProjection_DshVersionAndUpdatableCount()
    {
        var runtimeStore = new FakeStore<RuntimeState, RuntimeIntent, RuntimeEffect>(RuntimeState.Initial);
        using var harness = CreateViewModel(runtimeStore);

        harness.UpdatesStore.Push(UpdatesState.Initial with
        {
            CurrentDshVersion = "0.1.0-rc.12",
            PluginUpdates = [new PluginUpdateInfo("plugin-a", "1.0.0", "1.1.0")],
        });

        await Assert.That(harness.ViewModel.DshVersion).IsEqualTo("0.1.0-rc.12");
        await Assert.That(harness.ViewModel.UpdatablePluginCount).IsEqualTo(1);
        await Assert.That(harness.ViewModel.PluginsFooterText).IsEqualTo("1 个可更新");
    }

    [Test]
    public async Task PluginsProjection_PluginCount()
    {
        var runtimeStore = new FakeStore<RuntimeState, RuntimeIntent, RuntimeEffect>(RuntimeState.Initial);
        using var harness = CreateViewModel(runtimeStore);

        await Assert.That(harness.ViewModel.PluginCount).IsEqualTo(0);

        harness.PluginsStore.Push(PluginsState.Initial with
        {
            Plugins =
            [
                new PluginInfo("a", "1.0.0", false, true, ""),
                new PluginInfo("b", "2.0.0", false, false, ""),
            ],
        });

        await Assert.That(harness.ViewModel.PluginCount).IsEqualTo(2);
        await Assert.That(harness.ViewModel.PluginsFooterText).IsEqualTo("全部最新");
    }

    [Test]
    public async Task DiagnosticsProjection_ActivityFeedTrimmedNewestFirst()
    {
        var runtimeStore = new FakeStore<RuntimeState, RuntimeIntent, RuntimeEffect>(RuntimeState.Initial);
        using var harness = CreateViewModel(runtimeStore);

        var baseTime = new DateTimeOffset(2026, 9, 5, 10, 0, 0, TimeSpan.FromHours(8));
        List<DiagnosticEvent> entries = [];
        for (int i = 0; i < ActivityFeed.MaxEntries + 2; i++)
        {
            entries.Add(new DiagnosticEvent(
                baseTime.AddMinutes(i), DiagnosticSource.App, DiagnosticLevel.Info, $"Event.{i}"));
        }

        // 原始 stdout 行应被过滤，不进活动 feed。
        entries.Add(new DiagnosticEvent(
            baseTime.AddMinutes(99), DiagnosticSource.DshStdout, DiagnosticLevel.Info, "raw"));

        harness.DiagnosticsStore.Push(new DiagnosticsState(entries));

        await Assert.That(harness.ViewModel.ActivityItems.Count).IsEqualTo(ActivityFeed.MaxEntries);

        // 展示序 = 最新在前。
        await Assert.That(harness.ViewModel.ActivityItems[0].Title)
            .IsEqualTo($"Event.{ActivityFeed.MaxEntries + 1}");
    }

    [Test]
    public async Task StartupComparison_CombinesElapsedAndPrevious()
    {
        var runtimeStore = new FakeStore<RuntimeState, RuntimeIntent, RuntimeEffect>(RuntimeState.Initial);
        using var harness = CreateViewModel(runtimeStore);

        await harness.Store.DispatchAsync(new DashboardIntent.StartupElapsedRecorded(2130));
        runtimeStore.Push(RuntimeState.Initial with
        {
            Lifecycle = RuntimeLifecycle.Running,
            StartupElapsed = TimeSpan.FromMilliseconds(1820),
        });

        await Assert.That(harness.ViewModel.StartupComparisonText).IsEqualTo("比上次快 0.31s");
    }

    [Test]
    public async Task MetricsSampled_ProjectsCpuAndMemory()
    {
        var runtimeStore = new FakeStore<RuntimeState, RuntimeIntent, RuntimeEffect>(RuntimeState.Initial);
        using var harness = CreateViewModel(runtimeStore);

        await Assert.That(harness.ViewModel.CpuText).IsEqualTo("—");

        await harness.Store.DispatchAsync(new DashboardIntent.MetricsSampled(18.4, 412L * 1024 * 1024));

        await Assert.That(harness.ViewModel.CpuText).IsEqualTo("18%");
        await Assert.That(harness.ViewModel.MemoryText).IsEqualTo("412M");
    }

    [Test]
    public async Task TimelineReceived_ProjectsRows()
    {
        var runtimeStore = new FakeStore<RuntimeState, RuntimeIntent, RuntimeEffect>(RuntimeState.Initial);
        using var harness = CreateViewModel(runtimeStore);

        await harness.Store.DispatchAsync(new DashboardIntent.TimelineReceived(
        [
            new StartupStageTiming(RuntimeStartupSignal.Spawning, TimeSpan.FromMilliseconds(100)),
            new StartupStageTiming(RuntimeStartupSignal.Ready, TimeSpan.FromMilliseconds(1100)),
        ]));

        await Assert.That(harness.ViewModel.TimelineRows.Count).IsEqualTo(2);
        await Assert.That(harness.ViewModel.TimelineRows[0].Name).IsEqualTo("环境校验");
    }

    [Test]
    public async Task OpenWorkbench_FlowsNavigateRequestThroughMediator()
    {
        // 跨 Feature 导航：Intent → Effect → Mediator NavigateRequest（§28，不直接依赖 AppShell Store）。
        var runtimeStore = new FakeStore<RuntimeState, RuntimeIntent, RuntimeEffect>(RuntimeState.Initial);
        using var harness = CreateViewModel(runtimeStore);

        await harness.Store.DispatchAsync(new DashboardIntent.OpenWorkbench());
        await WaitForAsync(() => harness.Mediator.Requests.Count > 0);

        await Assert.That(harness.Mediator.Requests[0]).IsEqualTo(nameof(NavigateRequest));
    }

    private static Harness CreateViewModel(
        FakeStore<RuntimeState, RuntimeIntent, RuntimeEffect> runtimeStore)
    {
        var mediator = new RecordingMediator();
        var store = new MviStore<DashboardState, DashboardIntent, DashboardEffect>(
            DashboardState.Initial,
            new DashboardReducer(),
            new DashboardEffectDispatcher(mediator),
            []);
        var updatesStore = new FakeStore<UpdatesState, UpdatesIntent, UpdatesEffect>(UpdatesState.Initial);
        var pluginsStore = new FakeStore<PluginsState, PluginsIntent, PluginsEffect>(PluginsState.Initial);
        var diagnosticsStore = new FakeStore<DiagnosticsState, DiagnosticsIntent, DiagnosticsEffect>(DiagnosticsState.Initial);
        var viewModel = new DashboardViewModel(store, runtimeStore, updatesStore, pluginsStore, diagnosticsStore);
        return new Harness(store, viewModel, updatesStore, pluginsStore, diagnosticsStore, mediator);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 300 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }
    }

    private sealed class Harness(
        MviStore<DashboardState, DashboardIntent, DashboardEffect> store,
        DashboardViewModel viewModel,
        FakeStore<UpdatesState, UpdatesIntent, UpdatesEffect> updatesStore,
        FakeStore<PluginsState, PluginsIntent, PluginsEffect> pluginsStore,
        FakeStore<DiagnosticsState, DiagnosticsIntent, DiagnosticsEffect> diagnosticsStore,
        RecordingMediator mediator)
        : IDisposable
    {
        public MviStore<DashboardState, DashboardIntent, DashboardEffect> Store { get; } = store;
        public DashboardViewModel ViewModel { get; } = viewModel;
        public FakeStore<UpdatesState, UpdatesIntent, UpdatesEffect> UpdatesStore { get; } = updatesStore;
        public FakeStore<PluginsState, PluginsIntent, PluginsEffect> PluginsStore { get; } = pluginsStore;
        public FakeStore<DiagnosticsState, DiagnosticsIntent, DiagnosticsEffect> DiagnosticsStore { get; } = diagnosticsStore;
        public RecordingMediator Mediator { get; } = mediator;

        public void Dispose()
        {
            Store.Dispose();
        }
    }

    /// <summary>
    /// 表示按序记录请求名的 Mediator 测试替身（同 RuntimeEffectDispatcherTests 先例）。
    /// </summary>
    private sealed class RecordingMediator : IMviMediator
    {
        public List<string> Requests { get; } = [];

        /// <inheritdoc />
        public ValueTask<TResponse> SendAsync<TResponse>(
            MiKiNuo.Mvi.Domain.MVI.Mediator.IMviRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request.GetType().Name);
            return ValueTask.FromResult(default(TResponse)!);
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
