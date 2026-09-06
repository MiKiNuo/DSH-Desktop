using DshDesktop.Presentation.Avalonia.Features.Settings;

namespace DshDesktop.Tests;

/// <summary>
/// Settings 规约器测试（乐观更新语义：同值守卫不产生副作用，失败只回流错误）。
/// </summary>
public sealed class SettingsReducerTests
{
    private readonly SettingsReducer _reducer = new();

    [Test]
    public async Task LoadSettings_DeclaresLoadEffect()
    {
        var result = _reducer.Reduce(SettingsState.Initial, new SettingsIntent.LoadSettings());

        await Assert.That(result.State.PendingOperation).IsNotNull();
        await Assert.That(result.Effects[0] is SettingsEffect.LoadSettings).IsTrue();
    }

    [Test]
    public async Task ToggleSafeMode_FlipsWithPersistEffect()
    {
        var result = _reducer.Reduce(SettingsState.Initial, new SettingsIntent.ToggleSafeMode());

        await Assert.That(result.State.SafeMode).IsTrue();
        await Assert.That(result.Effects[0] is SettingsEffect.SaveSafeMode { Enabled: true }).IsTrue();
    }

    [Test]
    public async Task ToggleSafeMode_TwiceFlipsBack()
    {
        SettingsState on = _reducer.Reduce(SettingsState.Initial, new SettingsIntent.ToggleSafeMode()).State;

        var result = _reducer.Reduce(on, new SettingsIntent.ToggleSafeMode());

        await Assert.That(result.State.SafeMode).IsFalse();
        await Assert.That(result.Effects[0] is SettingsEffect.SaveSafeMode { Enabled: false }).IsTrue();
    }

    [Test]
    public async Task ToggleNotifications_FlipsWithPersistEffect()
    {
        // 初始投影 true（产品默认开），翻转后落盘 false。
        var result = _reducer.Reduce(SettingsState.Initial, new SettingsIntent.ToggleNotifications());

        await Assert.That(result.State.NotificationsEnabled).IsFalse();
        await Assert.That(result.Effects[0] is SettingsEffect.SaveNotificationsEnabled { Enabled: false }).IsTrue();
    }

    [Test]
    public async Task ToggleNotifications_TwiceFlipsBack()
    {
        SettingsState off = _reducer.Reduce(SettingsState.Initial, new SettingsIntent.ToggleNotifications()).State;

        var result = _reducer.Reduce(off, new SettingsIntent.ToggleNotifications());

        await Assert.That(result.State.NotificationsEnabled).IsTrue();
        await Assert.That(result.Effects[0] is SettingsEffect.SaveNotificationsEnabled { Enabled: true }).IsTrue();
    }

    [Test]
    public async Task ChangeChannel_WhenDifferent_OptimisticUpdateWithEffect()
    {
        var result = _reducer.Reduce(SettingsState.Initial, new SettingsIntent.ChangeChannel("alpha"));

        await Assert.That(result.State.Channel).IsEqualTo("alpha");
        await Assert.That(result.Effects[0] is SettingsEffect.SaveChannel { Channel: "alpha" }).IsTrue();
    }

    [Test]
    public async Task ChangeChannel_WhenSame_IsIgnored()
    {
        var result = _reducer.Reduce(SettingsState.Initial, new SettingsIntent.ChangeChannel("latest"));

        await Assert.That(result.Effects.Count).IsEqualTo(0);
    }

    [Test]
    public async Task SettingsLoaded_FillsAllFields()
    {
        var info = new SettingsInfo(
            SafeMode: true,
            NotificationsEnabled: false,
            Channel: "alpha",
            NodePath: @"D:\node\node.exe",
            DshHome: @"C:\Data\dsh-home",
            DataDirectory: @"C:\Data",
            PluginsDirectory: @"C:\Data\dsh-home\profiles\web\node_modules",
            DshRuntimeDirectory: @"C:\Data\runtime\dsh",
            MinimizeToTrayOnClose: false,
            LaunchOnStartup: true,
            BackgroundUpdateCheck: false,
            AutoDownloadUpdates: true);
        SettingsState busy = SettingsState.Initial with { PendingOperation = "加载设置…" };

        var result = _reducer.Reduce(busy, new SettingsIntent.SettingsLoaded(info));

        await Assert.That(result.State.SafeMode).IsTrue();
        await Assert.That(result.State.NotificationsEnabled).IsFalse();
        await Assert.That(result.State.Channel).IsEqualTo("alpha");
        await Assert.That(result.State.NodePath).IsEqualTo(@"D:\node\node.exe");
        await Assert.That(result.State.DshHome).IsEqualTo(@"C:\Data\dsh-home");
        await Assert.That(result.State.DataDirectory).IsEqualTo(@"C:\Data");
        await Assert.That(result.State.PluginsDirectory).IsEqualTo(@"C:\Data\dsh-home\profiles\web\node_modules");
        await Assert.That(result.State.DshRuntimeDirectory).IsEqualTo(@"C:\Data\runtime\dsh");
        await Assert.That(result.State.MinimizeToTrayOnClose).IsFalse();
        await Assert.That(result.State.LaunchOnStartup).IsTrue();
        await Assert.That(result.State.BackgroundUpdateCheck).IsFalse();
        await Assert.That(result.State.AutoDownloadUpdates).IsTrue();
        await Assert.That(result.State.PendingOperation).IsNull();
    }

    [Test]
    public async Task SettingsOperationFailed_SetsErrorAndClearsPending()
    {
        SettingsState busy = SettingsState.Initial with { PendingOperation = "加载设置…" };

        var result = _reducer.Reduce(busy, new SettingsIntent.SettingsOperationFailed("写入失败"));

        await Assert.That(result.State.LastError).IsEqualTo("写入失败");
        await Assert.That(result.State.PendingOperation).IsNull();
    }

    // ===== Phase 8 Issue 05：桌面行为 / 更新策略开关与打开目录（乐观更新，同 ToggleSafeMode 先例） =====

    [Test]
    public async Task InitialState_Issue05Defaults_MatchSpec()
    {
        await Assert.That(SettingsState.Initial.MinimizeToTrayOnClose).IsTrue();
        await Assert.That(SettingsState.Initial.LaunchOnStartup).IsFalse();
        await Assert.That(SettingsState.Initial.BackgroundUpdateCheck).IsTrue();
        await Assert.That(SettingsState.Initial.AutoDownloadUpdates).IsFalse();
    }

    [Test]
    public async Task ToggleMinimizeToTrayOnClose_FlipsWithPersistEffect()
    {
        var result = _reducer.Reduce(SettingsState.Initial, new SettingsIntent.ToggleMinimizeToTrayOnClose());

        await Assert.That(result.State.MinimizeToTrayOnClose).IsFalse();
        await Assert.That(result.Effects[0] is SettingsEffect.SaveMinimizeToTrayOnClose { Enabled: false }).IsTrue();
    }

    [Test]
    public async Task ToggleLaunchOnStartup_FlipsWithPersistEffect()
    {
        var result = _reducer.Reduce(SettingsState.Initial, new SettingsIntent.ToggleLaunchOnStartup());

        await Assert.That(result.State.LaunchOnStartup).IsTrue();
        await Assert.That(result.Effects[0] is SettingsEffect.SaveLaunchOnStartup { Enabled: true }).IsTrue();
    }

    [Test]
    public async Task ToggleBackgroundUpdateCheck_FlipsWithPersistEffect()
    {
        var result = _reducer.Reduce(SettingsState.Initial, new SettingsIntent.ToggleBackgroundUpdateCheck());

        await Assert.That(result.State.BackgroundUpdateCheck).IsFalse();
        await Assert.That(result.Effects[0] is SettingsEffect.SaveBackgroundUpdateCheck { Enabled: false }).IsTrue();
    }

    [Test]
    public async Task ToggleAutoDownloadUpdates_FlipsWithPersistEffect()
    {
        var result = _reducer.Reduce(SettingsState.Initial, new SettingsIntent.ToggleAutoDownloadUpdates());

        await Assert.That(result.State.AutoDownloadUpdates).IsTrue();
        await Assert.That(result.Effects[0] is SettingsEffect.SaveAutoDownloadUpdates { Enabled: true }).IsTrue();
    }

    [Test]
    public async Task OpenDirectory_WithPath_DeclaresOpenEffect()
    {
        var result = _reducer.Reduce(SettingsState.Initial, new SettingsIntent.OpenDirectory(@"C:\Data"));

        await Assert.That(result.Effects[0] is SettingsEffect.OpenDirectory { Path: @"C:\Data" }).IsTrue();
    }

    [Test]
    public async Task OpenDirectory_WithoutPath_IsIgnored()
    {
        var result = _reducer.Reduce(SettingsState.Initial, new SettingsIntent.OpenDirectory(null));

        await Assert.That(result.Effects.Count).IsEqualTo(0);
    }
}
