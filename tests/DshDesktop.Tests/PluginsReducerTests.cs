using DshDesktop.Domain.Plugins;
using DshDesktop.Presentation.Avalonia.Features.Plugins;

namespace DshDesktop.Tests;

/// <summary>
/// Plugins 规约器测试（§20 插件操作状态机：事务进行中禁止并发安装）。
/// </summary>
public sealed class PluginsReducerTests
{
    private readonly PluginsReducer _reducer = new();

    [Test]
    public async Task LoadPlugins_DeclaresLoadEffect()
    {
        var result = _reducer.Reduce(PluginsState.Initial, new PluginsIntent.LoadPlugins());

        await Assert.That(result.State.PendingOperation).IsNotNull();
        await Assert.That(result.Effects[0] is PluginsEffect.LoadPlugins).IsTrue();
    }

    [Test]
    public async Task EnablePlugin_DeclaresSetEnabledTrue()
    {
        var result = _reducer.Reduce(PluginsState.Initial, new PluginsIntent.EnablePlugin("dsh-foo"));

        await Assert.That(result.Effects[0] is PluginsEffect.SetPluginEnabled { Name: "dsh-foo", Enabled: true }).IsTrue();
    }

    [Test]
    public async Task DisablePlugin_DeclaresSetEnabledFalse()
    {
        var result = _reducer.Reduce(PluginsState.Initial, new PluginsIntent.DisablePlugin("dsh-foo"));

        await Assert.That(result.Effects[0] is PluginsEffect.SetPluginEnabled { Name: "dsh-foo", Enabled: false }).IsTrue();
    }

    [Test]
    public async Task UninstallPlugin_DeclaresUninstallEffect()
    {
        var result = _reducer.Reduce(PluginsState.Initial, new PluginsIntent.UninstallPlugin("dsh-foo"));

        await Assert.That(result.Effects[0] is PluginsEffect.UninstallPlugin { Name: "dsh-foo" }).IsTrue();
    }

    [Test]
    public async Task PluginsLoaded_ClearsOperationAndError()
    {
        PluginsState busy = PluginsState.Initial with
        {
            PendingOperation = "安装中…",
            Operation = new PluginOperation(PluginOperationStage.Installing, "dsh-foo", null),
            LastError = "旧错误",
        };
        IReadOnlyList<PluginInfo> plugins = [new PluginInfo("dsh-foo", "1.0.0", false, true)];

        var result = _reducer.Reduce(busy, new PluginsIntent.PluginsLoaded(plugins));

        await Assert.That(result.State.Plugins.Count).IsEqualTo(1);
        await Assert.That(result.State.PendingOperation).IsNull();
        await Assert.That(result.State.Operation).IsNull();
        await Assert.That(result.State.LastError).IsNull();
    }

    [Test]
    public async Task InstallPlugin_WhenIdle_DeclaresInstallEffect()
    {
        var result = _reducer.Reduce(PluginsState.Initial, new PluginsIntent.InstallPlugin("dsh-foo"));

        await Assert.That(result.Effects[0] is PluginsEffect.InstallPlugin { Source: "dsh-foo" }).IsTrue();
    }

    [Test]
    public async Task InstallPlugin_WhenTransactionInFlight_IsIgnored()
    {
        PluginsState inFlight = PluginsState.Initial with
        {
            Operation = new PluginOperation(PluginOperationStage.Installing, "dsh-foo", null),
        };

        var result = _reducer.Reduce(inFlight, new PluginsIntent.InstallPlugin("dsh-bar"));

        await Assert.That(result.Effects.Count).IsEqualTo(0);
        await Assert.That(result.State.Operation!.Stage).IsEqualTo(PluginOperationStage.Installing);
    }

    [Test]
    public async Task InstallPlugin_WhenPreviousCompleted_AllowsNewInstall()
    {
        PluginsState completed = PluginsState.Initial with
        {
            Operation = new PluginOperation(PluginOperationStage.Completed, "dsh-foo", null),
        };

        var result = _reducer.Reduce(completed, new PluginsIntent.InstallPlugin("dsh-bar"));

        await Assert.That(result.Effects[0] is PluginsEffect.InstallPlugin { Source: "dsh-bar" }).IsTrue();
    }

    [Test]
    public async Task InstallPlugin_WhenPreviousFailed_AllowsRetry()
    {
        PluginsState failed = PluginsState.Initial with
        {
            Operation = new PluginOperation(PluginOperationStage.Failed, "dsh-foo", "npm 失败"),
        };

        var result = _reducer.Reduce(failed, new PluginsIntent.InstallPlugin("dsh-foo"));

        await Assert.That(result.Effects.Count).IsEqualTo(1);
    }

    [Test]
    public async Task DisableAllThirdParty_DeclaresEffect()
    {
        var result = _reducer.Reduce(PluginsState.Initial, new PluginsIntent.DisableAllThirdParty());

        await Assert.That(result.Effects[0] is PluginsEffect.DisableAllThirdParty).IsTrue();
    }

    [Test]
    public async Task PluginOperationChanged_InFlight_KeepsPendingText()
    {
        var operation = new PluginOperation(PluginOperationStage.StartingRuntime, "dsh-foo", null);

        var result = _reducer.Reduce(PluginsState.Initial, new PluginsIntent.PluginOperationChanged(operation));

        await Assert.That(result.State.Operation!.Stage).IsEqualTo(PluginOperationStage.StartingRuntime);
        await Assert.That(result.State.PendingOperation).IsNotNull();
    }

    [Test]
    public async Task PluginOperationChanged_Completed_ClearsPendingText()
    {
        PluginsState busy = PluginsState.Initial with { PendingOperation = "安装中…" };
        var operation = new PluginOperation(PluginOperationStage.Completed, "dsh-foo", null);

        var result = _reducer.Reduce(busy, new PluginsIntent.PluginOperationChanged(operation));

        await Assert.That(result.State.PendingOperation).IsNull();
    }

    [Test]
    public async Task PluginOperationFailed_SetsLastErrorAndClearsPending()
    {
        PluginsState busy = PluginsState.Initial with { PendingOperation = "卸载中…" };

        var result = _reducer.Reduce(busy, new PluginsIntent.PluginOperationFailed("卸载失败"));

        await Assert.That(result.State.PendingOperation).IsNull();
        await Assert.That(result.State.LastError).IsEqualTo("卸载失败");
    }
}
