using DshDesktop.Domain.Plugins;
using DshDesktop.Presentation.Avalonia.Features.AppShell;

namespace DshDesktop.Tests;

/// <summary>
/// PluginOperationText：壳 toast 的操作动词映射。卸载/启用/禁用事务化后（A2），
/// 同一套终态 toast 被五个入口复用，文案必须按种类区分，避免点「卸载」却报「安装」。
/// </summary>
public sealed class PluginOperationTextTests
{
    [Test]
    public async Task Verb_MapsAllKinds()
    {
        await Assert.That(PluginOperationText.Verb(PluginOperationKind.Install)).IsEqualTo("安装");
        await Assert.That(PluginOperationText.Verb(PluginOperationKind.Update)).IsEqualTo("更新");
        await Assert.That(PluginOperationText.Verb(PluginOperationKind.Uninstall)).IsEqualTo("卸载");
        await Assert.That(PluginOperationText.Verb(PluginOperationKind.Enable)).IsEqualTo("启用");
        await Assert.That(PluginOperationText.Verb(PluginOperationKind.Disable)).IsEqualTo("禁用");
    }
}
