using DshDesktop.Presentation.Avalonia.Features.Settings;
using MiKiNuo.Mvi.Application.MVI.Mediator;
using MiKiNuo.Mvi.Application.MVI.Store;
using MiKiNuo.Mvi.Domain.MVI.Mediator;

namespace DshDesktop.Tests;

/// <summary>
/// Settings 副作用分发器测试（Phase 8 Issue 05，§43.2：Effect → Mediator → config 落盘链路；
/// 注册表/资源管理器 IO 经端口在组合根执行，此处验证请求路由与失败回流）。
/// </summary>
public sealed class SettingsEffectDispatcherTests
{
    [Test]
    public async Task ToggleMinimizeToTrayOnClose_PersistsViaMediator()
    {
        var mediator = new RecordingMediator();
        using var store = CreateStore(mediator);

        await store.DispatchAsync(new SettingsIntent.ToggleMinimizeToTrayOnClose());
        await WaitForAsync(() => mediator.Requests.Contains(nameof(SetMinimizeToTrayOnCloseRequest)));

        await Assert.That(store.CurrentState.MinimizeToTrayOnClose).IsFalse();
        await Assert.That(mediator.Requests).Contains(nameof(SetMinimizeToTrayOnCloseRequest));
    }

    [Test]
    public async Task ToggleLaunchOnStartup_PersistsViaMediator()
    {
        var mediator = new RecordingMediator();
        using var store = CreateStore(mediator);

        await store.DispatchAsync(new SettingsIntent.ToggleLaunchOnStartup());
        await WaitForAsync(() => mediator.Requests.Contains(nameof(SetLaunchOnStartupRequest)));

        await Assert.That(store.CurrentState.LaunchOnStartup).IsTrue();
        await Assert.That(mediator.Requests).Contains(nameof(SetLaunchOnStartupRequest));
    }

    [Test]
    public async Task ToggleBackgroundUpdateCheck_PersistsViaMediator()
    {
        var mediator = new RecordingMediator();
        using var store = CreateStore(mediator);

        await store.DispatchAsync(new SettingsIntent.ToggleBackgroundUpdateCheck());
        await WaitForAsync(() => mediator.Requests.Contains(nameof(SetBackgroundUpdateCheckRequest)));

        await Assert.That(store.CurrentState.BackgroundUpdateCheck).IsFalse();
        await Assert.That(mediator.Requests).Contains(nameof(SetBackgroundUpdateCheckRequest));
    }

    [Test]
    public async Task ToggleAutoDownloadUpdates_PersistsViaMediator()
    {
        var mediator = new RecordingMediator();
        using var store = CreateStore(mediator);

        await store.DispatchAsync(new SettingsIntent.ToggleAutoDownloadUpdates());
        await WaitForAsync(() => mediator.Requests.Contains(nameof(SetAutoDownloadUpdatesRequest)));

        await Assert.That(store.CurrentState.AutoDownloadUpdates).IsTrue();
        await Assert.That(mediator.Requests).Contains(nameof(SetAutoDownloadUpdatesRequest));
    }

    [Test]
    public async Task OpenDirectory_RoutesPathToMediator()
    {
        var mediator = new RecordingMediator();
        using var store = CreateStore(mediator);

        await store.DispatchAsync(new SettingsIntent.OpenDirectory(@"C:\Data\runtime\dsh"));
        await WaitForAsync(() => mediator.OpenedPaths.Count > 0);

        await Assert.That(mediator.OpenedPaths[0]).IsEqualTo(@"C:\Data\runtime\dsh");
    }

    [Test]
    public async Task SaveFailure_ReportsErrorKeepsOptimisticState()
    {
        var mediator = new RecordingMediator { FailOnRequest = nameof(SetLaunchOnStartupRequest) };
        using var store = CreateStore(mediator);

        await store.DispatchAsync(new SettingsIntent.ToggleLaunchOnStartup());
        await WaitForAsync(() => store.CurrentState.LastError is not null);

        // State 已乐观更新，失败只回报错误（同 SaveSafeMode 先例）。
        await Assert.That(store.CurrentState.LaunchOnStartup).IsTrue();
        await Assert.That(store.CurrentState.LastError).IsEqualTo("注册表写入失败");
    }

    private static MviStore<SettingsState, SettingsIntent, SettingsEffect> CreateStore(
        RecordingMediator mediator)
    {
        return new MviStore<SettingsState, SettingsIntent, SettingsEffect>(
            SettingsState.Initial, new SettingsReducer(), new SettingsEffectDispatcher(mediator), []);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 300 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }
    }

    /// <summary>
    /// 表示按序记录请求的 Mediator 测试替身（记录打开路径载荷）。
    /// </summary>
    private sealed class RecordingMediator : IMviMediator
    {
        public List<string> Requests { get; } = [];

        public List<string> OpenedPaths { get; } = [];

        public string? FailOnRequest { get; init; }

        /// <inheritdoc />
        public ValueTask<TResponse> SendAsync<TResponse>(
            IMviRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            string name = request.GetType().Name;
            Requests.Add(name);
            if (name == FailOnRequest)
            {
                throw new InvalidOperationException("注册表写入失败");
            }

            object response = request switch
            {
                OpenPathRequest open => RecordOpen(open),
                SetMinimizeToTrayOnCloseRequest or SetLaunchOnStartupRequest
                    or SetBackgroundUpdateCheckRequest or SetAutoDownloadUpdatesRequest => true,
                _ => throw new NotSupportedException($"未登记的请求：{name}"),
            };
            return ValueTask.FromResult((TResponse)response);
        }

        private object RecordOpen(OpenPathRequest request)
        {
            OpenedPaths.Add(request.Path);
            return true;
        }
    }
}
