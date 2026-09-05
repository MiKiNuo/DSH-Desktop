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
    public async Task ChangeSafeMode_WhenDifferent_OptimisticUpdateWithEffect()
    {
        var result = _reducer.Reduce(SettingsState.Initial, new SettingsIntent.ChangeSafeMode(true));

        await Assert.That(result.State.SafeMode).IsTrue();
        await Assert.That(result.Effects[0] is SettingsEffect.SaveSafeMode { Enabled: true }).IsTrue();
    }

    [Test]
    public async Task ChangeSafeMode_WhenSame_IsIgnored()
    {
        var result = _reducer.Reduce(SettingsState.Initial, new SettingsIntent.ChangeSafeMode(false));

        await Assert.That(result.Effects.Count).IsEqualTo(0);
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
            Channel: "alpha",
            NodePath: @"D:\node\node.exe",
            DshHome: @"C:\Data\dsh-home",
            DataDirectory: @"C:\Data",
            DesktopVersion: "0.1.0");
        SettingsState busy = SettingsState.Initial with { PendingOperation = "加载设置…" };

        var result = _reducer.Reduce(busy, new SettingsIntent.SettingsLoaded(info));

        await Assert.That(result.State.SafeMode).IsTrue();
        await Assert.That(result.State.Channel).IsEqualTo("alpha");
        await Assert.That(result.State.NodePath).IsEqualTo(@"D:\node\node.exe");
        await Assert.That(result.State.DshHome).IsEqualTo(@"C:\Data\dsh-home");
        await Assert.That(result.State.DataDirectory).IsEqualTo(@"C:\Data");
        await Assert.That(result.State.DesktopVersion).IsEqualTo("0.1.0");
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
}
