using DshDesktop.Infrastructure.Config;

namespace DshDesktop.Tests;

/// <summary>
/// 数据根迁移测试（ADR-0003 修订）：数据根从 C 盘改到 D 盘后，
/// 已落盘配置里的 <c>dshHome</c> 必须重锚到新数据根，避免继续写旧位置。
/// </summary>
public sealed class DataRootMigrationTests
{
    [Test]
    public async Task RebasesDshHome_StalePathUnderOldRoot_PointsToNewRoot()
    {
        var config = new DshDesktopConfig
        {
            DshHome = @"C:\Users\test\AppData\Local\DshDesktop\data\dsh-home",
        };

        bool changed = DshDesktopConfigStore.RebaseDshHome(
            config,
            newDataRoot: @"D:\Program Files\DSH-Desktop\data");

        await Assert.That(changed).IsTrue();
        await Assert.That(config.DshHome)
            .IsEqualTo(@"D:\Program Files\DSH-Desktop\data\dsh-home");
    }

    [Test]
    public async Task RebasesDshHome_AlreadyUnderNewRoot_NoChange()
    {
        string expected = @"D:\Program Files\DSH-Desktop\data\dsh-home";
        var config = new DshDesktopConfig { DshHome = expected };

        bool changed = DshDesktopConfigStore.RebaseDshHome(
            config,
            newDataRoot: @"D:\Program Files\DSH-Desktop\data");

        await Assert.That(changed).IsFalse();
        await Assert.That(config.DshHome).IsEqualTo(expected);
    }

    [Test]
    public async Task RebasesDshHome_BlankDshHome_SetsToNewRoot()
    {
        var config = new DshDesktopConfig { DshHome = string.Empty };

        bool changed = DshDesktopConfigStore.RebaseDshHome(
            config,
            newDataRoot: @"D:\Program Files\DSH-Desktop\data");

        await Assert.That(changed).IsTrue();
        await Assert.That(config.DshHome)
            .IsEqualTo(@"D:\Program Files\DSH-Desktop\data\dsh-home");
    }
}
