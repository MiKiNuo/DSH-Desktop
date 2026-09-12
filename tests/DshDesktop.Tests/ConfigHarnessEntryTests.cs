using DshDesktop.Infrastructure.Config;

namespace DshDesktop.Tests;

/// <summary>
/// 自带 harness 垫片解析测试（internal 方法经 InternalsVisibleTo 直测，同 ConfigDeriveTests 约定）。
/// 背景：dsh ≥0.1.5-rc.1 的 bin.js 以 import.meta.main 守门，旧 Electron harness 的
/// 纯 import() 加载方式下入口不自执行（静默退出 0），Desktop 必须自带兼容垫片并优先使用。
/// </summary>
public sealed class ConfigHarnessEntryTests
{
    [Test]
    public async Task ResolveHarnessEntryPath_OwnExists_PrefersOwn()
    {
        string root = NewTempDir();
        try
        {
            string own = Touch(root, "own", "harness-node-entry.mjs");
            string electron = Touch(root, "electron", "harness-node-entry.mjs");

            string? resolved = DshDesktopConfigStore.ResolveHarnessEntryPath(own, electron);

            await Assert.That(resolved).IsEqualTo(own);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task ResolveHarnessEntryPath_OwnMissing_FallsBackToElectron()
    {
        string root = NewTempDir();
        try
        {
            string own = Path.Combine(root, "own", "harness-node-entry.mjs");
            string electron = Touch(root, "electron", "harness-node-entry.mjs");

            string? resolved = DshDesktopConfigStore.ResolveHarnessEntryPath(own, electron);

            await Assert.That(resolved).IsEqualTo(electron);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task ResolveHarnessEntryPath_BothMissing_ReturnsNull()
    {
        string root = NewTempDir();
        // 不创建任何文件：两个候选路径都不存在。
        string? resolved = DshDesktopConfigStore.ResolveHarnessEntryPath(
            Path.Combine(root, "own", "harness-node-entry.mjs"),
            Path.Combine(root, "electron", "harness-node-entry.mjs"));

        await Assert.That(resolved).IsNull();
    }

    [Test]
    public async Task ReanchorHarnessEntryPath_ConfiguredFileMissing_ReanchorsToOwn()
    {
        string root = NewTempDir();
        try
        {
            string own = Touch(root, "own", "harness-node-entry.mjs");
            DshDesktopConfig config = new()
            {
                HarnessNodeEntryPath = Path.Combine(root, "gone", "harness-node-entry.mjs"),
            };

            bool changed = DshDesktopConfigStore.ReanchorHarnessEntryPath(config, own);

            await Assert.That(changed).IsTrue();
            await Assert.That(config.HarnessNodeEntryPath).IsEqualTo(own);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task ReanchorHarnessEntryPath_ConfiguredFileExists_KeepsConfigured()
    {
        string root = NewTempDir();
        try
        {
            string own = Touch(root, "own", "harness-node-entry.mjs");
            string configured = Touch(root, "electron", "harness-node-entry.mjs");
            DshDesktopConfig config = new() { HarnessNodeEntryPath = configured };

            bool changed = DshDesktopConfigStore.ReanchorHarnessEntryPath(config, own);

            await Assert.That(changed).IsFalse();
            await Assert.That(config.HarnessNodeEntryPath).IsEqualTo(configured);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string NewTempDir()
        => Path.Combine(Path.GetTempPath(), "dsh-test-" + Guid.NewGuid().ToString("N"));

    private static string Touch(string root, string dir, string file)
    {
        string path = Path.Combine(root, dir, file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Empty);
        return path;
    }
}
