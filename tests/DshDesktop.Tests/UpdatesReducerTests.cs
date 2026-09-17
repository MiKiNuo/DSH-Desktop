using DshDesktop.Domain.Updates;
using DshDesktop.Presentation.Avalonia.Features.Updates;

namespace DshDesktop.Tests;

/// <summary>
/// Updates 规约器测试（§23 更新状态机：可用性判定只认"有差异"，禁止布尔组合）。
/// </summary>
public sealed class UpdatesReducerTests
{
    private readonly UpdatesReducer _reducer = new();

    [Test]
    public async Task CheckUpdates_TransitionsToCheckingWithEffect()
    {
        var result = _reducer.Reduce(UpdatesState.Initial, new UpdatesIntent.CheckUpdates());

        await Assert.That(result.State.Status).IsEqualTo(UpdateStatus.Checking);
        await Assert.That(result.Effects[0] is UpdatesEffect.CheckUpdates).IsTrue();
    }

    [Test]
    public async Task CheckUpdatesCompleted_NoChanges_ReturnsToIdle()
    {
        var response = new CheckUpdatesResponse(
            "0.1.2", "0.1.2",
            [new DshRuntimeInfo("0.1.2", true, false)],
            [], null);

        var result = _reducer.Reduce(CheckingState(), new UpdatesIntent.CheckUpdatesCompleted(response));

        await Assert.That(result.State.Status).IsEqualTo(UpdateStatus.Idle);
        await Assert.That(result.State.LatestDshVersion).IsEqualTo("0.1.2");
    }

    [Test]
    public async Task CheckUpdatesCompleted_PluginUpdateAvailable_ReturnsAvailable()
    {
        var response = new CheckUpdatesResponse(
            "0.1.2", "0.1.2",
            [new DshRuntimeInfo("0.1.2", true, false)],
            [new PluginUpdateInfo("dsh-foo", "1.0.0", "1.1.0")], null);

        var result = _reducer.Reduce(CheckingState(), new UpdatesIntent.CheckUpdatesCompleted(response));

        await Assert.That(result.State.Status).IsEqualTo(UpdateStatus.Available);
        await Assert.That(result.State.PluginUpdates.Count).IsEqualTo(1);
    }

    [Test]
    public async Task CheckUpdatesCompleted_DshVersionDiffers_ReturnsAvailable()
    {
        var response = new CheckUpdatesResponse(
            "0.1.3", "0.1.2",
            [new DshRuntimeInfo("0.1.2", true, false)],
            [], null);

        var result = _reducer.Reduce(CheckingState(), new UpdatesIntent.CheckUpdatesCompleted(response));

        await Assert.That(result.State.Status).IsEqualTo(UpdateStatus.Available);
    }

    [Test]
    public async Task CheckUpdatesCompleted_CurrentDshUnknown_ReturnsIdle()
    {
        // 借用安装的版本未知时（null），无法判定差异，不误报有更新。
        var response = new CheckUpdatesResponse("0.1.3", null, [], [], null);

        var result = _reducer.Reduce(CheckingState(), new UpdatesIntent.CheckUpdatesCompleted(response));

        await Assert.That(result.State.Status).IsEqualTo(UpdateStatus.Idle);
    }

    [Test]
    public async Task InstallDshRuntime_TransitionsToInstallingWithEffect()
    {
        var result = _reducer.Reduce(UpdatesState.Initial, new UpdatesIntent.InstallDshRuntime("0.1.3"));

        await Assert.That(result.State.Status).IsEqualTo(UpdateStatus.Installing);
        await Assert.That(result.Effects[0] is UpdatesEffect.InstallDshRuntime { Version: "0.1.3" }).IsTrue();
    }

    [Test]
    public async Task ActivateDshRuntime_DeclaresEffect()
    {
        var result = _reducer.Reduce(UpdatesState.Initial, new UpdatesIntent.ActivateDshRuntime("0.1.3"));

        await Assert.That(result.State.PendingOperation).IsNotNull();
        await Assert.That(result.Effects[0] is UpdatesEffect.ActivateDshRuntime { Version: "0.1.3" }).IsTrue();
    }

    [Test]
    public async Task UpdatePlugin_DeclaresEffect()
    {
        var result = _reducer.Reduce(UpdatesState.Initial, new UpdatesIntent.UpdatePlugin("dsh-foo"));

        await Assert.That(result.Effects[0] is UpdatesEffect.UpdatePlugin { Name: "dsh-foo" }).IsTrue();
    }

    [Test]
    public async Task RuntimeListChanged_SyncsActiveVersionAndClearsPending()
    {
        UpdatesState busy = UpdatesState.Initial with
        {
            Status = UpdateStatus.Installing,
            PendingOperation = "安装中…",
            CurrentDshVersion = "0.1.2",
        };
        IReadOnlyList<DshRuntimeInfo> runtimes =
        [
            new DshRuntimeInfo("0.1.2", false, true),
            new DshRuntimeInfo("0.1.3", true, false),
        ];

        var result = _reducer.Reduce(busy, new UpdatesIntent.RuntimeListChanged(runtimes));

        await Assert.That(result.State.Runtimes.Count).IsEqualTo(2);
        await Assert.That(result.State.CurrentDshVersion).IsEqualTo("0.1.3");
        await Assert.That(result.State.Status).IsEqualTo(UpdateStatus.Idle);
        await Assert.That(result.State.PendingOperation).IsNull();
    }

    [Test]
    public async Task UpdatesOperationFailed_TransitionsToFailedWithError()
    {
        var result = _reducer.Reduce(CheckingState(), new UpdatesIntent.UpdatesOperationFailed("npm 查询失败"));

        await Assert.That(result.State.Status).IsEqualTo(UpdateStatus.Failed);
        await Assert.That(result.State.PendingOperation).IsNull();
        await Assert.That(result.State.LastError).IsEqualTo("npm 查询失败");
    }

    [Test]
    public async Task CheckUpdatesCompleted_DesktopUpdateAvailable_WritesLatestDesktopVersion()
    {
        var response = new CheckUpdatesResponse(
            "0.1.2", "0.1.2",
            [new DshRuntimeInfo("0.1.2", true, false)],
            [], "0.2.0");

        var result = _reducer.Reduce(CheckingState(), new UpdatesIntent.CheckUpdatesCompleted(response));

        await Assert.That(result.State.LatestDesktopVersion).IsEqualTo("0.2.0");
    }

    [Test]
    public async Task DownloadAndApplyDesktopUpdate_WhenUpdateAvailable_DeclaresEffect()
    {
        UpdatesState available = UpdatesState.Initial with { LatestDesktopVersion = "0.2.0" };

        var result = _reducer.Reduce(available, new UpdatesIntent.DownloadAndApplyDesktopUpdate());

        await Assert.That(result.State.PendingOperation).IsNotNull();
        await Assert.That(result.Effects[0] is UpdatesEffect.DownloadAndApplyDesktopUpdate).IsTrue();
    }

    [Test]
    public async Task DownloadAndApplyDesktopUpdate_WhenNoUpdate_IsIgnored()
    {
        var result = _reducer.Reduce(UpdatesState.Initial, new UpdatesIntent.DownloadAndApplyDesktopUpdate());

        await Assert.That(result.Effects.Count).IsEqualTo(0);
        await Assert.That(result.State.PendingOperation).IsNull();
    }

    [Test]
    public async Task DesktopDownloadProgress_UpdatesProgressAndPendingText()
    {
        // 真实流程：先进入下载（PendingOperation 已建立），进度回流才更新文本/百分比。
        UpdatesState downloading = _reducer.Reduce(
            UpdatesState.Initial with { LatestDesktopVersion = "0.2.0" },
            new UpdatesIntent.DownloadAndApplyDesktopUpdate()).State;

        var result = _reducer.Reduce(downloading, new UpdatesIntent.DesktopDownloadProgress(42));

        await Assert.That(result.State.DesktopDownloadProgress).IsEqualTo(42);
        await Assert.That(result.State.PendingOperation).IsNotNull();
    }

    [Test]
    public async Task DesktopDownloadProgress_NoOperationInProgress_DoesNotSetPendingOperation()
    {
        // Blocker 1：进度回流是 fire-and-forget，若无进行中操作则不应凭空创建 PendingOperation
        // （否则遮罩/导航锁会被无中生有地拉起）。
        UpdatesState idle = UpdatesState.Initial with { LatestDesktopVersion = "0.2.0" };

        var result = _reducer.Reduce(idle, new UpdatesIntent.DesktopDownloadProgress(50));

        await Assert.That(result.State.PendingOperation).IsNull();
        await Assert.That(result.State.DesktopDownloadProgress).IsNull();
    }

    [Test]
    public async Task DesktopDownloadProgress_AfterTerminalFailure_DoesNotResurrectPendingOperation()
    {
        // Blocker 1：操作失败已清空 PendingOperation；若仍有排队的进度回流抵达，不得复活它
        // （否则遮罩永久卡死，用户无法关闭）。
        UpdatesState downloading = _reducer.Reduce(
            UpdatesState.Initial with { LatestDesktopVersion = "0.2.0" },
            new UpdatesIntent.DownloadAndApplyDesktopUpdate()).State;
        UpdatesState failed = _reducer.Reduce(downloading, new UpdatesIntent.UpdatesOperationFailed("下载失败")).State;
        await Assert.That(failed.PendingOperation).IsNull();

        UpdatesState resurrected = _reducer.Reduce(failed, new UpdatesIntent.DesktopDownloadProgress(80)).State;
        await Assert.That(resurrected.PendingOperation).IsNull();
    }

    [Test]
    public async Task CheckUpdates_BackgroundCheckDuringInstall_DoesNotReleasePendingOperation()
    {
        // Should-fix 2：启动期后台静默检查（App.axaml.cs → BackgroundCheckUpdatesAsync）绕过按钮，
        // 若安装进行中抵达，不得清空 PendingOperation（否则遮罩/导航锁被提前释放）。
        UpdatesState installing = UpdatesState.Initial with
        {
            Status = UpdateStatus.Installing,
            PendingOperation = "安装 DSH Runtime 0.1.3…",
        };

        var result = _reducer.Reduce(installing, new UpdatesIntent.CheckUpdates());

        await Assert.That(result.State.PendingOperation).IsNotNull();
    }

    [Test]
    public async Task PluginUpdate_SuccessPath_ClearsPendingOperation()
    {
        // 插件更新成功回流检查更新（§23）；终态必须清空 PendingOperation，否则壳遮罩永久卡死。
        UpdatesState busy = _reducer.Reduce(UpdatesState.Initial, new UpdatesIntent.UpdatePlugin("dsh-foo")).State;
        await Assert.That(busy.PendingOperation).IsNotNull();

        UpdatesState afterCheck = _reducer.Reduce(busy, new UpdatesIntent.CheckUpdates()).State;
        await Assert.That(afterCheck.PendingOperation).IsNull();

        var response = new CheckUpdatesResponse("0.1.2", "0.1.2", [], [], null);
        UpdatesState done = _reducer.Reduce(afterCheck, new UpdatesIntent.CheckUpdatesCompleted(response)).State;
        await Assert.That(done.PendingOperation).IsNull();
    }

    [Test]
    public async Task InstallDshRuntime_SuccessPath_ClearsPendingOperation()
    {
        // DSH Runtime 安装成功 → RuntimeListChanged 终态清空 PendingOperation。
        UpdatesState busy = _reducer.Reduce(UpdatesState.Initial, new UpdatesIntent.InstallDshRuntime("0.1.3")).State;
        await Assert.That(busy.PendingOperation).IsNotNull();

        IReadOnlyList<DshRuntimeInfo> runtimes = [new DshRuntimeInfo("0.1.3", true, false)];
        UpdatesState done = _reducer.Reduce(busy, new UpdatesIntent.RuntimeListChanged(runtimes)).State;
        await Assert.That(done.PendingOperation).IsNull();
    }

    [Test]
    public async Task ActivateDshRuntime_SuccessPath_ClearsPendingOperation()
    {
        // 激活 Runtime 成功 → RuntimeListChanged 终态清空 PendingOperation。
        UpdatesState busy = _reducer.Reduce(UpdatesState.Initial, new UpdatesIntent.ActivateDshRuntime("0.1.3")).State;
        await Assert.That(busy.PendingOperation).IsNotNull();

        IReadOnlyList<DshRuntimeInfo> runtimes = [new DshRuntimeInfo("0.1.3", true, false)];
        UpdatesState done = _reducer.Reduce(busy, new UpdatesIntent.RuntimeListChanged(runtimes)).State;
        await Assert.That(done.PendingOperation).IsNull();
    }

    [Test]
    public async Task InstallDshRuntime_ResetsStaleDownloadProgress()
    {
        // 遮罩「spinner ↔ 确定进度条」互斥依赖"进度字段仅在 Desktop 下载期间有值"：
        // 非下载操作必须清掉上一次 Desktop 下载残留的百分比，否则遮罩会显示一个卡住的进度条。
        UpdatesState stale = UpdatesState.Initial with { DesktopDownloadProgress = 42 };

        var result = _reducer.Reduce(stale, new UpdatesIntent.InstallDshRuntime("0.1.3"));

        await Assert.That(result.State.DesktopDownloadProgress).IsNull();
    }

    [Test]
    public async Task ActivateDshRuntime_ResetsStaleDownloadProgress()
    {
        UpdatesState stale = UpdatesState.Initial with { DesktopDownloadProgress = 42 };

        var result = _reducer.Reduce(stale, new UpdatesIntent.ActivateDshRuntime("0.1.3"));

        await Assert.That(result.State.DesktopDownloadProgress).IsNull();
    }

    [Test]
    public async Task UpdatePlugin_ResetsStaleDownloadProgress()
    {
        UpdatesState stale = UpdatesState.Initial with { DesktopDownloadProgress = 42 };

        var result = _reducer.Reduce(stale, new UpdatesIntent.UpdatePlugin("dsh-foo"));

        await Assert.That(result.State.DesktopDownloadProgress).IsNull();
    }

    [Test]
    public async Task UpdatesOperationFailed_ClearsDownloadProgress()
    {
        // 下载失败终态须一并清进度：否则遮罩与页面进度条停在失败前的百分比（假死观感）。
        UpdatesState downloading = _reducer.Reduce(
            UpdatesState.Initial with { LatestDesktopVersion = "0.2.0" },
            new UpdatesIntent.DownloadAndApplyDesktopUpdate()).State;
        UpdatesState progressed = _reducer.Reduce(downloading, new UpdatesIntent.DesktopDownloadProgress(42)).State;
        await Assert.That(progressed.DesktopDownloadProgress).IsEqualTo(42);

        var result = _reducer.Reduce(progressed, new UpdatesIntent.UpdatesOperationFailed("下载失败"));

        await Assert.That(result.State.DesktopDownloadProgress).IsNull();
    }

    [Test]
    public async Task RuntimeListChanged_ClearsDownloadProgress()
    {
        UpdatesState stale = UpdatesState.Initial with { DesktopDownloadProgress = 42 };
        IReadOnlyList<DshRuntimeInfo> runtimes = [new DshRuntimeInfo("0.1.3", true, false)];

        var result = _reducer.Reduce(stale, new UpdatesIntent.RuntimeListChanged(runtimes));

        await Assert.That(result.State.DesktopDownloadProgress).IsNull();
    }

    [Test]
    public async Task PendingOperationSource_DistinguishesPluginUpdateFromRuntimeInstall()
    {
        // 此前靠 PendingOperation 文案前缀（"更新"）判断"本次待办是否插件更新引起"，文案一改即静默失灵；
        // 改为 State 上的显式标记后，本用例锁定两种来源的区分。
        UpdatesState pluginUpdate = _reducer.Reduce(
            UpdatesState.Initial,
            new UpdatesIntent.UpdatePlugin("dsh-foo")).State;
        UpdatesState runtimeInstall = _reducer.Reduce(
            UpdatesState.Initial,
            new UpdatesIntent.InstallDshRuntime("0.1.3")).State;

        await Assert.That(pluginUpdate.IsPluginUpdatePending).IsTrue();
        await Assert.That(runtimeInstall.IsPluginUpdatePending).IsFalse();
    }

    [Test]
    public async Task PendingOperationSource_ResetsOnTerminalState()
    {
        UpdatesState busy = _reducer.Reduce(
            UpdatesState.Initial,
            new UpdatesIntent.UpdatePlugin("dsh-foo")).State;
        IReadOnlyList<DshRuntimeInfo> runtimes = [new DshRuntimeInfo("0.1.3", true, false)];

        var result = _reducer.Reduce(busy, new UpdatesIntent.RuntimeListChanged(runtimes));

        await Assert.That(result.State.IsPluginUpdatePending).IsFalse();
    }

    private static UpdatesState CheckingState()
    {
        return UpdatesState.Initial with { Status = UpdateStatus.Checking };
    }
}
