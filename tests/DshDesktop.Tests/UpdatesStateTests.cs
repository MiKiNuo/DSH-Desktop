using DshDesktop.Domain.Updates;
using DshDesktop.Presentation.Avalonia.Features.Updates;

namespace DshDesktop.Tests;

/// <summary>
/// UpdatesState.AvailableCount 纯逻辑测试（AppShell UpdateBadge 投影口径，各来源独立计数）。
/// Desktop 侧：LatestDesktopVersion 仅在更新器确认有更新时非空（当前版本是编译期常量），非空计 1；
/// DSH 侧：LatestDshVersion 是通道最新版本（始终上报），须与当前版本不等才计 1。
/// </summary>
public sealed class UpdatesStateTests
{
    [Test]
    public async Task AvailableCount_NothingAvailable_IsZero()
    {
        await Assert.That(UpdatesState.Initial.AvailableCount).IsEqualTo(0);
    }

    [Test]
    public async Task AvailableCount_DesktopUpdateOnly_CountsOne()
    {
        UpdatesState state = UpdatesState.Initial with { LatestDesktopVersion = "0.6.0" };

        await Assert.That(state.AvailableCount).IsEqualTo(1);
    }

    [Test]
    public async Task AvailableCount_DshNewerThanCurrent_CountsOne()
    {
        UpdatesState state = UpdatesState.Initial with
        {
            LatestDshVersion = "rc.13",
            CurrentDshVersion = "rc.12",
        };

        await Assert.That(state.AvailableCount).IsEqualTo(1);
    }

    [Test]
    public async Task AvailableCount_DshSameVersion_NotCounted()
    {
        UpdatesState state = UpdatesState.Initial with
        {
            LatestDshVersion = "rc.12",
            CurrentDshVersion = "rc.12",
        };

        await Assert.That(state.AvailableCount).IsEqualTo(0);
    }

    [Test]
    public async Task AvailableCount_DshCurrentUnknown_NotCounted()
    {
        // 当前版本未知（尚无激活 Runtime）时无法判定新旧，不计数。
        UpdatesState state = UpdatesState.Initial with { LatestDshVersion = "rc.13" };

        await Assert.That(state.AvailableCount).IsEqualTo(0);
    }

    [Test]
    public async Task DshStage_LatestUnknown_UpToDate()
    {
        await Assert.That(UpdatesState.Initial.DshStage).IsEqualTo(DshRuntimeStage.UpToDate);
    }

    [Test]
    public async Task DshStage_LatestEqualsCurrent_UpToDate()
    {
        UpdatesState state = UpdatesState.Initial with
        {
            LatestDshVersion = "rc.12",
            CurrentDshVersion = "rc.12",
        };

        await Assert.That(state.DshStage).IsEqualTo(DshRuntimeStage.UpToDate);
    }

    [Test]
    public async Task DshStage_LatestNotOnMachine_Available()
    {
        UpdatesState state = UpdatesState.Initial with
        {
            LatestDshVersion = "rc.13",
            CurrentDshVersion = "rc.12",
            Runtimes = [new DshRuntimeInfo("rc.12", IsActive: true, IsBorrowed: false)],
        };

        await Assert.That(state.DshStage).IsEqualTo(DshRuntimeStage.Available);
    }

    [Test]
    public async Task DshStage_LatestDownloadedNotActive_ReadyToActivate()
    {
        UpdatesState state = UpdatesState.Initial with
        {
            LatestDshVersion = "rc.13",
            CurrentDshVersion = "rc.12",
            Runtimes =
            [
                new DshRuntimeInfo("rc.12", IsActive: true, IsBorrowed: false),
                new DshRuntimeInfo("rc.13", IsActive: false, IsBorrowed: false),
            ],
        };

        await Assert.That(state.DshStage).IsEqualTo(DshRuntimeStage.ReadyToActivate);
    }

    [Test]
    public async Task DshStage_LatestOnlyBorrowed_Available()
    {
        // 借用安装是外部只读副本，不等于「已下载到本机」——仍引导 side-by-side 安装。
        UpdatesState state = UpdatesState.Initial with
        {
            LatestDshVersion = "rc.13",
            CurrentDshVersion = "rc.12",
            Runtimes =
            [
                new DshRuntimeInfo("rc.13", IsActive: false, IsBorrowed: true),
                new DshRuntimeInfo("rc.12", IsActive: true, IsBorrowed: false),
            ],
        };

        await Assert.That(state.DshStage).IsEqualTo(DshRuntimeStage.Available);
    }

    [Test]
    public async Task DshStage_CurrentUnknown_Available()
    {
        // 尚无激活 Runtime 时，最新版不在本机即引导安装。
        UpdatesState state = UpdatesState.Initial with { LatestDshVersion = "rc.13" };

        await Assert.That(state.DshStage).IsEqualTo(DshRuntimeStage.Available);
    }

    [Test]
    public async Task AvailableCount_AllSources_SumsUp()
    {
        UpdatesState state = UpdatesState.Initial with
        {
            LatestDesktopVersion = "0.6.0",
            LatestDshVersion = "rc.13",
            CurrentDshVersion = "rc.12",
        };

        await Assert.That(state.AvailableCount).IsEqualTo(2);
    }

    /// <summary>
    /// ADR-0011：版本发现源可能落后于本机已装版本（如本机已装 rc 而上游 tag 回退）。
    /// 此时必须判「已是最新」，不能把「装回旧版」提示成有可用更新。
    /// </summary>
    [Test]
    public async Task DshStage_LatestOlderThanCurrent_UpToDate()
    {
        UpdatesState state = UpdatesState.Initial with
        {
            LatestDshVersion = "0.2.0-rc.2",
            CurrentDshVersion = "0.2.1-rc.2",
        };

        await Assert.That(state.DshStage).IsEqualTo(DshRuntimeStage.UpToDate);
        await Assert.That(state.AvailableCount).IsEqualTo(0);
    }

    /// <summary>rc.10 必须压过 rc.2（ADR-0011 前按字符串判等会恒显示有更新）。</summary>
    [Test]
    public async Task DshStage_RcTenNewerThanRcTwo_Available()
    {
        UpdatesState state = UpdatesState.Initial with
        {
            LatestDshVersion = "0.2.1-rc.10",
            CurrentDshVersion = "0.2.1-rc.2",
        };

        await Assert.That(state.DshStage).IsEqualTo(DshRuntimeStage.Available);
        await Assert.That(state.AvailableCount).IsEqualTo(1);
    }
}
