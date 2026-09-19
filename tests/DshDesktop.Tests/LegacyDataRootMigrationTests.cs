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

    /// <summary>
    /// 迁移必须排除 harness 自管易失物（2026-09-19 v0.1.6 实机根因）：
    /// ① <c>&lt;DSH_HOME&gt;\profiles\node_modules.lock</c> —— harness 跨进程写锁，搬走 = 新根永久持锁，
    /// 此后每次启动 2s 超时 ExitCode=1；
    /// ② <c>&lt;DSH_HOME&gt;\profiles\node_modules</c> —— harness 自管共享 fallback，启动时自建为 junction，
    /// 实体化副本会让它抛 "exists and is not a symlink or dsh-managed module proxy"。
    /// 任意 <c>*.lock</c> 同源（原子写瞬时锁），一并排除；profile 自身依赖树必须保留。
    /// </summary>
    [Test]
    public async Task Migrate_HarnessOwnedState_NotCarriedOver()
    {
        string root = NewTempDir();
        try
        {
            string legacy = Path.Combine(root, "legacy");
            string target = Path.Combine(root, "target");
            string profiles = Path.Combine(legacy, "dsh-home", "profiles");
            Directory.CreateDirectory(Path.Combine(profiles, "node_modules", "undici"));
            File.WriteAllText(Path.Combine(profiles, "node_modules", "undici", "package.json"), "{}");
            File.WriteAllText(Path.Combine(profiles, "node_modules.lock"), "28572\n");
            Directory.CreateDirectory(Path.Combine(profiles, "web", "node_modules", "dshmarket"));
            File.WriteAllText(
                Path.Combine(profiles, "web", "node_modules", "dshmarket", "package.json"), "{}");
            File.WriteAllText(Path.Combine(profiles, "web", "atomic-write.lock"), "x");
            // profile 自管 fallback（healProfileModuleFallback 的 <profile>/.dsh-module-fallback/node_modules）：
            // 与共享 fallback 同类，ProfileSeeder 的 robocopy 同样以 /XD 排除它。
            Directory.CreateDirectory(Path.Combine(profiles, "web", ".dsh-module-fallback", "node_modules"));
            File.WriteAllText(
                Path.Combine(profiles, "web", ".dsh-module-fallback", "node_modules", "leftover.txt"), "x");
            Directory.CreateDirectory(Path.Combine(legacy, "config"));
            File.WriteAllText(Path.Combine(legacy, "config", "dsh-desktop.config.json"), "{}");

            LegacyDataRootMigration.Migrate(legacy, target);

            string targetProfiles = Path.Combine(target, "dsh-home", "profiles");
            await Assert.That(File.Exists(Path.Combine(targetProfiles, "node_modules.lock"))).IsFalse();
            await Assert.That(Directory.Exists(Path.Combine(targetProfiles, "node_modules"))).IsFalse();
            await Assert.That(File.Exists(Path.Combine(targetProfiles, "web", "atomic-write.lock"))).IsFalse();
            await Assert.That(Directory.Exists(
                    Path.Combine(targetProfiles, "web", ".dsh-module-fallback")))
                .IsFalse();
            // profile 依赖树是启动可用性的根据，必须原样保留。
            await Assert.That(File.Exists(Path.Combine(
                    targetProfiles, "web", "node_modules", "dshmarket", "package.json")))
                .IsTrue();
        }
        finally
        {
            Cleanup(root);
        }
    }

    /// <summary>
    /// 旧根清理失败必须是**可上报的部分失败**，不得改判为整次迁移失败（2026-09-19 v0.1.6 实机：
    /// 内容已并入新根，仅旧根删除失败，日志却报 "MigrationFailed（旧根未动，按新根全新初始化继续）"
    /// —— 误导排查方向：用户据此以为还在旧根，实际新根已在用）。
    /// </summary>
    [Test]
    public async Task Migrate_LegacyRootUndeletable_ReportsCleanupErrorWithoutFailingMigration()
    {
        string root = NewTempDir();
        try
        {
            string legacy = Path.Combine(root, "legacy");
            string target = Path.Combine(root, "target");
            Directory.CreateDirectory(Path.Combine(legacy, "dsh-home"));
            File.WriteAllText(Path.Combine(legacy, "dsh-home", "marker.txt"), "x");
            Directory.CreateDirectory(Path.Combine(legacy, "config"));
            File.WriteAllText(Path.Combine(legacy, "config", "dsh-desktop.config.json"), "{}");

            // 共享掩码不含 Delete：复制可读该文件，旧根整树删除必然失败。
            using FileStream blocker = new(
                Path.Combine(legacy, "dsh-home", "blocker.bin"),
                FileMode.Create,
                FileAccess.ReadWrite,
                FileShare.ReadWrite);

            string? cleanupError = LegacyDataRootMigration.Migrate(legacy, target);

            await Assert.That(File.Exists(Path.Combine(target, "dsh-home", "marker.txt"))).IsTrue();
            await Assert.That(File.Exists(
                Path.Combine(target, "config", "dsh-desktop.config.json"))).IsTrue();
            await Assert.That(cleanupError).IsNotNull();
        }
        finally
        {
            Cleanup(root);
        }
    }

    /// <summary>
    /// 迁移会把旧数据根的 config 一并带入新根，其中的自动安全模式属于**旧现场**（连续启动失败自动进入）。
    /// 新根首启继承它会让 bootstrap 直接跳过自动启动，用户侧表现为"更新后不自动启动"
    /// （2026-09-19 v0.1.6 实机：迁移继承 safeMode=true，两次启动都没有 Runtime.Start.Begin）。
    /// </summary>
    [Test]
    public async Task ClearInheritedSafeMode_SafeModeOn_ResetsToFalseKeepingOtherFields()
    {
        string root = NewTempDir();
        try
        {
            string configPath = Path.Combine(root, "config", "dsh-desktop.config.json");
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            File.WriteAllText(configPath, """{ "safeMode": true, "theme": "Dark" }""");

            DshDesktopConfigStore.ClearInheritedSafeMode(configPath);

            string content = await File.ReadAllTextAsync(configPath);
            await Assert.That(content).Contains("\"safeMode\": false");
            await Assert.That(content).Contains("\"theme\": \"Dark\"");
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Test]
    public async Task ClearInheritedSafeMode_SafeModeOff_LeavesFileUntouched()
    {
        string root = NewTempDir();
        try
        {
            string configPath = Path.Combine(root, "config", "dsh-desktop.config.json");
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            string original = """{ "safeMode": false }""";
            File.WriteAllText(configPath, original);

            DshDesktopConfigStore.ClearInheritedSafeMode(configPath);

            await Assert.That(await File.ReadAllTextAsync(configPath)).IsEqualTo(original);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Test]
    public async Task ClearInheritedSafeMode_MissingFile_NoThrow()
    {
        string root = NewTempDir();
        try
        {
            string configPath = Path.Combine(root, "config", "dsh-desktop.config.json");

            DshDesktopConfigStore.ClearInheritedSafeMode(configPath);

            await Assert.That(File.Exists(configPath)).IsFalse();
        }
        finally
        {
            Cleanup(root);
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
