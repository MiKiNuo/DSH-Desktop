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
        UpdatesState downloading = UpdatesState.Initial with { LatestDesktopVersion = "0.2.0" };

        var result = _reducer.Reduce(downloading, new UpdatesIntent.DesktopDownloadProgress(42));

        await Assert.That(result.State.DesktopDownloadProgress).IsEqualTo(42);
        await Assert.That(result.State.PendingOperation).IsNotNull();
    }

    private static UpdatesState CheckingState()
    {
        return UpdatesState.Initial with { Status = UpdateStatus.Checking };
    }
}
