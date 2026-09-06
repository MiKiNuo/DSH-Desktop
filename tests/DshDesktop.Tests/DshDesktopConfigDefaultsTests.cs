using DshDesktop.Infrastructure.Config;

namespace DshDesktop.Tests;

/// <summary>
/// Phase 8 Issue 04 新增配置字段的默认值测试（ADR-0005 / ADR-0004 修订注 / §34 修订注）：
/// 旧配置缺字段反序列化时保持默认（属性初始化器），故默认值即兼容语义。
/// </summary>
public sealed class DshDesktopConfigDefaultsTests
{
    [Test]
    public async Task NewConfig_PolicyDefaults_MatchAdr()
    {
        var config = new DshDesktopConfig();

        await Assert.That(config.KeepRuntimeOnClose).IsFalse(); // ADR-0005 落地对账：默认关（重接管恒退化为重启）
        await Assert.That(config.AutoSafeModeOnFailure).IsTrue(); // ADR-0004 修订注：默认开
        await Assert.That(config.CheckUpdatesOnStartup).IsFalse(); // §34 修订注：默认关
    }

    [Test]
    public async Task NewConfig_Issue05Defaults_MatchSpec()
    {
        var config = new DshDesktopConfig();

        await Assert.That(config.MinimizeToTrayOnClose).IsTrue(); // 原型 switch on：默认开
        await Assert.That(config.LaunchOnStartup).IsFalse(); // 原型副文案"默认关闭"
        await Assert.That(config.BackgroundUpdateCheck).IsTrue(); // 原型 switch on：默认开
        await Assert.That(config.AutoDownloadUpdates).IsFalse(); // 原型 switch off：默认关
    }

    [Test]
    public async Task NewConfig_ReattachTarget_InitiallyEmpty()
    {
        var config = new DshDesktopConfig();

        await Assert.That(config.LastRuntimePid).IsNull();
        await Assert.That(config.LastRuntimePort).IsNull();
    }
}
