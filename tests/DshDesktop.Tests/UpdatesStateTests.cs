using DshDesktop.Domain.Updates;
using DshDesktop.Presentation.Avalonia.Features.Updates;

namespace DshDesktop.Tests;

/// <summary>
/// UpdatesState.AvailableCount 纯逻辑测试（AppShell UpdateBadge 投影口径，各来源独立计数）。
/// Desktop 侧：LatestDesktopVersion 仅在 Velopack 确认有更新时非空（当前版本是编译期常量），非空计 1；
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
    public async Task AvailableCount_AllSources_SumsUp()
    {
        UpdatesState state = UpdatesState.Initial with
        {
            LatestDesktopVersion = "0.6.0",
            LatestDshVersion = "rc.13",
            CurrentDshVersion = "rc.12",
            PluginUpdates =
            [
                new PluginUpdateInfo("plugin-a", "1.0.0", "1.1.0"),
                new PluginUpdateInfo("plugin-b", "2.0.0", "2.1.0"),
            ],
        };

        await Assert.That(state.AvailableCount).IsEqualTo(4);
    }
}
