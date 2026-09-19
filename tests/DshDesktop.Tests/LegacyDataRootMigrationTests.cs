using DshDesktop.Infrastructure.Config;

namespace DshDesktop.Tests;

/// <summary>
/// 旧数据根一次性迁移测试（ADR-0009）：数据根从 %LOCALAPPDATA% 回迁安装根后，
/// 旧根数据必须无缝搬到新根——不搬则安装版用户首启退化为全新环境（Runtime/Profile 全丢）。
/// 接缝：<see cref="LegacyDataRootMigration"/>（internal 经 InternalsVisibleTo 直测），
/// 全量真实临时目录，覆盖空目录、既有数据、嵌套内容与幂等跳过。
/// </summary>
public sealed class LegacyDataRootMigrationTests
{
    [Test]
    public async Task IsMigrationNeeded_LegacyMissing_False()
    {
        string root = NewTempDir();
        try
        {
            string legacy = Path.Combine(root, "legacy");
            string target = Path.Combine(root, "target");

            await Assert.That(LegacyDataRootMigration.IsMigrationNeeded(legacy, target)).IsFalse();
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Test]
    public async Task IsMigrationNeeded_NewRootAlreadyHasConfig_False()
    {
        // 新根已有配置 = 已经迁过 / 用户已在新根工作：绝不再搬（幂等守卫）。
        string root = NewTempDir();
        try
        {
            string legacy = Path.Combine(root, "legacy");
            string target = Path.Combine(root, "target");
            Directory.CreateDirectory(legacy);
            string configDir = Path.Combine(target, "config");
            Directory.CreateDirectory(configDir);
            File.WriteAllText(Path.Combine(configDir, "dsh-desktop.config.json"), "{}");

            await Assert.That(LegacyDataRootMigration.IsMigrationNeeded(legacy, target)).IsFalse();
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Test]
    public async Task IsMigrationNeeded_SamePath_False()
    {
        // 环境变量覆盖回旧根等场景：新旧同路径时不得"自己搬自己"。
        string root = NewTempDir();
        try
        {
            string legacy = Path.Combine(root, "data");
            Directory.CreateDirectory(legacy);

            await Assert.That(LegacyDataRootMigration.IsMigrationNeeded(legacy, legacy)).IsFalse();
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Test]
    public async Task Migrate_MovesAllContentAndDeletesLegacy()
    {
        string root = NewTempDir();
        try
        {
            string legacy = Path.Combine(root, "legacy");
            string target = Path.Combine(root, "target");
            string nested = Path.Combine(legacy, "runtime", "dsh", "0.1.5-rc.1");
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(nested, "package.json"), "{}");
            string configDir = Path.Combine(legacy, "config");
            Directory.CreateDirectory(configDir);
            File.WriteAllText(Path.Combine(configDir, "dsh-desktop.config.json"), "{}");

            LegacyDataRootMigration.Migrate(legacy, target);

            await Assert.That(File.Exists(
                Path.Combine(target, "runtime", "dsh", "0.1.5-rc.1", "package.json"))).IsTrue();
            await Assert.That(File.Exists(
                Path.Combine(target, "config", "dsh-desktop.config.json"))).IsTrue();
            await Assert.That(Directory.Exists(legacy)).IsFalse();
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Test]
    public async Task Migrate_NewRootAlreadyExists_MergesIntoIt()
    {
        // 安装器预建 {app}\data（ACL 目录）场景：新根已存在（空），内容并入而非报错。
        string root = NewTempDir();
        try
        {
            string legacy = Path.Combine(root, "legacy");
            string target = Path.Combine(root, "target");
            Directory.CreateDirectory(Path.Combine(legacy, "dsh-home", "profiles"));
            File.WriteAllText(Path.Combine(legacy, "dsh-home", "marker.txt"), "x");
            Directory.CreateDirectory(target); // 安装器预建的空目录

            LegacyDataRootMigration.Migrate(legacy, target);

            await Assert.That(File.Exists(Path.Combine(target, "dsh-home", "marker.txt"))).IsTrue();
            await Assert.That(Directory.Exists(Path.Combine(target, "dsh-home", "profiles"))).IsTrue();
            await Assert.That(Directory.Exists(legacy)).IsFalse();
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Test]
    public async Task IsMigrationNeeded_LegacyPresentAndNewRootFresh_True()
    {
        string root = NewTempDir();
        try
        {
            string legacy = Path.Combine(root, "legacy");
            string target = Path.Combine(root, "target");
            Directory.CreateDirectory(legacy);

            await Assert.That(LegacyDataRootMigration.IsMigrationNeeded(legacy, target)).IsTrue();
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Test]
    public async Task Migrate_NewRootHasPartialRetry_RemainingContentMerged()
    {
        // 中断重入：上次迁移并入一半（无 config 完成标记），重试必须保住已有内容并补齐剩余。
        string root = NewTempDir();
        try
        {
            string legacy = Path.Combine(root, "legacy");
            string target = Path.Combine(root, "target");
            Directory.CreateDirectory(Path.Combine(legacy, "dsh-home"));
            File.WriteAllText(Path.Combine(legacy, "dsh-home", "from-legacy.txt"), "x");
            string legacyConfig = Path.Combine(legacy, "config");
            Directory.CreateDirectory(legacyConfig);
            File.WriteAllText(Path.Combine(legacyConfig, "dsh-desktop.config.json"), "{}");
            // 上次中断的残留：已并入的 dsh-home（含文件）+ 遗留暂存目录。
            Directory.CreateDirectory(Path.Combine(target, "dsh-home"));
            File.WriteAllText(Path.Combine(target, "dsh-home", "partial.txt"), "y");
            Directory.CreateDirectory(Path.Combine(target, ".migrating-stale"));

            LegacyDataRootMigration.Migrate(legacy, target);

            await Assert.That(File.Exists(Path.Combine(target, "dsh-home", "partial.txt"))).IsTrue();
            await Assert.That(File.Exists(Path.Combine(target, "dsh-home", "from-legacy.txt"))).IsTrue();
            await Assert.That(File.Exists(
                Path.Combine(target, "config", "dsh-desktop.config.json"))).IsTrue();
            await Assert.That(Directory.GetDirectories(target, ".migrating-*")).IsEmpty();
            await Assert.That(Directory.Exists(legacy)).IsFalse();
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Test]
    public async Task MigrateLegacyDataRootIfNeeded_EnvironmentOverrideSet_NotAttempted()
    {
        // 环境变量覆盖 = 用户显式指定数据根：绝不迁移（独立评审补测缺口）。
        Environment.SetEnvironmentVariable("DSH_DESKTOP_DATA_ROOT", @"E:\dsh-test-override");
        try
        {
            LegacyDataRootMigrationOutcome outcome = DshDesktopConfigStore.MigrateLegacyDataRootIfNeeded();

            await Assert.That(outcome.Attempted).IsFalse();
            await Assert.That(outcome.Error).IsNull();
        }
        finally
        {
            Environment.SetEnvironmentVariable("DSH_DESKTOP_DATA_ROOT", null);
        }
    }

    private static string NewTempDir()
        => Path.Combine(Path.GetTempPath(), "dsh-test-" + Guid.NewGuid().ToString("N"));

    private static void Cleanup(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
