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
    public async Task DesktopDownloadProgress_WhilePluginUpdatePending_DoesNotCreatePendingOperation()
    {
        // 候选 3（消除"在飞操作"双轨）：插件更新不再写 UpdatesState.PendingOperation（单一源 = PluginsState.Operation）。
        // 故 UpdatePlugin 后 UpdatesState.PendingOperation 为空；此时若有迟到 Desktop 进度回流抵达，
        // 守卫（PendingOperation 为空 ⇒ 不在 Desktop 下载期）仍应阻止其凭空创建待办（否则遮罩被无中生有拉起）。
        UpdatesState updating = _reducer.Reduce(UpdatesState.Initial, new UpdatesIntent.UpdatePlugin("dsh-foo")).State;
        await Assert.That(updating.PendingOperation).IsNull(); // 迁移：不再写第二轨

        var result = _reducer.Reduce(updating, new UpdatesIntent.DesktopDownloadProgress(42));

        await Assert.That(result.State.PendingOperation).IsNull();
        await Assert.That(result.State.DesktopDownloadProgress).IsNull();
    }

    [Test]
    public async Task PluginOperationFinished_WithUnknownPluginName_KeepsUpdatableRows()
    {
        // 插件名未知（null）时不得误摘可更新行：终态照常清待办，列表只由随后的检查对账。
        UpdatesState withRows = _reducer.Reduce(
            UpdatesState.Initial,
            new UpdatesIntent.UpdatePlugin("dsh-foo")).State with
        {
            PluginUpdates =
            [
                new PluginUpdateInfo("dsh-foo", "1.0.0", "1.1.0"),
                new PluginUpdateInfo("dsh-bar", "2.0.0", "2.1.0"),
            ],
        };

        var result = _reducer.Reduce(withRows, new UpdatesIntent.PluginOperationFinished(null));

        await Assert.That(result.State.PendingOperation).IsNull();
        await Assert.That(result.State.PluginUpdates.Count).IsEqualTo(2);
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
    public async Task PluginUpdate_SuccessPath_TerminalIntentRemovesRowAndReflows()
    {
        // 候选 3：插件更新成功 → 组合根在事务 Completed 发布显式终态（§23）。
        // 迁移后 HandleUpdatePlugin 不再写 UpdatesState.PendingOperation（在飞由 PluginsState.Operation 承载），
        // 故 busy 时 PendingOperation 为空；终态须按名摘行 + 回流检查，且不得触碰 PendingOperation。
        UpdatesState busy = _reducer.Reduce(UpdatesState.Initial, new UpdatesIntent.UpdatePlugin("dsh-foo")).State;
        await Assert.That(busy.PendingOperation).IsNull();

        UpdatesState withRows = busy with
        {
            PluginUpdates = [new PluginUpdateInfo("dsh-foo", "1.0.0", "1.1.0")],
        };
        var result = _reducer.Reduce(withRows, new UpdatesIntent.PluginOperationFinished("dsh-foo"));

        await Assert.That(result.State.PendingOperation).IsNull(); // 不被终态触碰
        await Assert.That(result.State.PluginUpdates.Count).IsEqualTo(0);
        await Assert.That(result.Effects[0] is UpdatesEffect.CheckUpdates).IsTrue();
    }

    [Test]
    public async Task PluginOperationFinished_AfterCheckCompletedMidTransaction_RemovesRowAndReflows()
    {
        // 2026-09-21 v0.1.8 实机回归（遮罩永久卡死）的新语义版：事务进行中，一次本就在飞的检查完成回流
        // （CheckUpdatesCompleted）不得把可更新行误摘——行摘收口到 PluginOperationFinished 按名摘除；
        // 终态意图自带一次检查回流，插件版本仍以检查结果为准。
        UpdatesState busy = _reducer.Reduce(UpdatesState.Initial, new UpdatesIntent.UpdatePlugin("dsh-foo")).State;
        // 迁移：HandleUpdatePlugin 不再写第二轨，PendingOperation 保持空（在飞由 PluginsState.Operation 承载）。
        await Assert.That(busy.PendingOperation).IsNull();

        UpdatesState withRows = busy with
        {
            PluginUpdates = [new PluginUpdateInfo("dsh-foo", "1.0.0", "1.1.0")],
        };
        // 事务进行中抵达的检查回流：此时 dsh-foo 版本尚未变化，检查仍会把它列入可更新列表
        // （真实回流即如此），CheckUpdatesCompleted 用该列表覆盖 PluginUpdates——行不被误清。
        var response = new CheckUpdatesResponse(
            "0.1.2", "0.1.2",
            [new DshRuntimeInfo("0.1.2", true, false)],
            [new PluginUpdateInfo("dsh-foo", "1.0.0", "1.1.0")], null);
        UpdatesState midStream = _reducer.Reduce(withRows, new UpdatesIntent.CheckUpdatesCompleted(response)).State;
        await Assert.That(midStream.PluginUpdates.Count).IsEqualTo(1); // 检查完成不误摘行
        await Assert.That(midStream.PendingOperation).IsNull(); // 检查完成不触碰待办

        UpdatesState finished = _reducer.Reduce(midStream, new UpdatesIntent.PluginOperationFinished("dsh-foo")).State;
        await Assert.That(finished.PluginUpdates.Count).IsEqualTo(0); // 终态按名摘行
        await Assert.That(finished.PendingOperation).IsNull();
    }

    [Test]
    public async Task CheckUpdates_WhilePluginUpdatePending_DoesNotRemovePluginRow()
    {
        // 候选 3（反向漏洞保护）：插件更新期间任何 CheckUpdates 抵达（启动后台静默检查 / 上一次事务
        // 成功广播的那次检查）都不得误摘可更新行、不得把 PendingOperation 写回（在飞由 PluginsState.Operation 承载）。
        // 旧名 CheckUpdates_DuringPluginUpdate_DoesNotReleasePendingOperation 已失准：PendingOperation 在候选 3 后
        // 不再承载插件更新在飞，断言须落到"行不被误摘 + PendingOperation 不被动"。
        UpdatesState busy = _reducer.Reduce(UpdatesState.Initial, new UpdatesIntent.UpdatePlugin("dsh-foo")).State;
        UpdatesState withRows = busy with
        {
            PluginUpdates = [new PluginUpdateInfo("dsh-foo", "1.0.0", "1.1.0")],
        };

        var result = _reducer.Reduce(withRows, new UpdatesIntent.CheckUpdates());

        await Assert.That(result.State.PendingOperation).IsNull();
        await Assert.That(result.State.PluginUpdates.Count).IsEqualTo(1); // 检查不得误摘行
        await Assert.That(result.State.Status).IsEqualTo(UpdateStatus.Checking);
    }

    [Test]
    public async Task PluginOperationFinished_RemovesFinishedPluginFromUpdatableList()
    {
        // 2026-09-21 v0.1.8 实机回归（列表不刷新）：成功后立即摘除该插件行，不依赖慢网络检查回来。
        // 否则更新中心「插件更新」卡与插件页「↻ 可更新 / 更新」按钮在成功后仍停在旧值。
        UpdatesState withRows = _reducer.Reduce(
            UpdatesState.Initial,
            new UpdatesIntent.UpdatePlugin("dsh-foo")).State with
        {
            PluginUpdates =
            [
                new PluginUpdateInfo("dsh-foo", "1.0.0", "1.1.0"),
                new PluginUpdateInfo("dsh-bar", "2.0.0", "2.1.0"),
            ],
        };

        var result = _reducer.Reduce(withRows, new UpdatesIntent.PluginOperationFinished("dsh-foo"));

        await Assert.That(result.State.PluginUpdates.Count).IsEqualTo(1);
        await Assert.That(result.State.PluginUpdates[0].Name).IsEqualTo("dsh-bar");
    }

    [Test]
    public async Task PluginOperationFinished_WhileDesktopDownloadPending_KeepsItsPendingOperation()
    {
        // 守卫：仅当待办确属插件操作（来源标记为真）才清空——Desktop 下载进行中抵达的插件事务终态
        // 不得释放遮罩（§22），但仍可按名摘除插件的可更新行并回流一次检查。
        UpdatesState downloading = UpdatesState.Initial with
        {
            PendingOperation = "下载 Desktop 更新 0.2.0…",
            DesktopDownloadProgress = 42,
            LatestDesktopVersion = "0.2.0",
            PluginUpdates = [new PluginUpdateInfo("dsh-foo", "1.0.0", "1.1.0")],
        };

        var result = _reducer.Reduce(downloading, new UpdatesIntent.PluginOperationFinished("dsh-foo"));

        await Assert.That(result.State.PendingOperation).IsNotNull();
        await Assert.That(result.State.DesktopDownloadProgress).IsEqualTo(42);
        await Assert.That(result.State.PluginUpdates.Count).IsEqualTo(0);
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
    public async Task UpdatePlugin_DoesNotWritePendingOperation_InflightOwnedByPluginsState()
    {
        // 候选 3：消除"在飞操作"双轨。插件更新的在飞与文案改由 PluginsState.Operation 单一承载，
        // UpdatesState 不再记录第二轨。故 HandleUpdatePlugin 不得写 PendingOperation（只发 UpdatePlugin effect）。
        var result = _reducer.Reduce(UpdatesState.Initial, new UpdatesIntent.UpdatePlugin("dsh-foo"));

        await Assert.That(result.State.PendingOperation).IsNull();
        await Assert.That(result.State.DesktopDownloadProgress).IsNull();
        await Assert.That(result.Effects[0] is UpdatesEffect.UpdatePlugin { Name: "dsh-foo" }).IsTrue();
    }

    [Test]
    public async Task PluginOperationFinished_UninstallTerminal_DoesNotClearDesktopDownloadPending()
    {
        // 卸载（非更新发起）终态也走 PluginOperationFinished：必须按名摘行 + 回流检查，
        // 但不得触碰 UpdatesState.PendingOperation——Desktop 下载进行中抵达时其待办属 Desktop，须保留
        // （否则遮罩/导航锁被提前释放）。PendingOperation 在该 handler 中完全不再触碰。
        UpdatesState downloading = UpdatesState.Initial with
        {
            PendingOperation = "下载 Desktop 更新 0.2.0…",
            DesktopDownloadProgress = 42,
            LatestDesktopVersion = "0.2.0",
            PluginUpdates = [new PluginUpdateInfo("dsh-foo", "1.0.0", "1.1.0")],
        };

        var result = _reducer.Reduce(downloading, new UpdatesIntent.PluginOperationFinished("dsh-foo"));

        await Assert.That(result.State.PendingOperation).IsEqualTo("下载 Desktop 更新 0.2.0…"); // 不被卸载终态清掉
        await Assert.That(result.State.DesktopDownloadProgress).IsEqualTo(42);
        await Assert.That(result.State.PluginUpdates.Count).IsEqualTo(0); // 按名摘行安全
        await Assert.That(result.Effects[0] is UpdatesEffect.CheckUpdates).IsTrue();
    }

    private static UpdatesState CheckingState()
    {
        return UpdatesState.Initial with { Status = UpdateStatus.Checking };
    }
}
