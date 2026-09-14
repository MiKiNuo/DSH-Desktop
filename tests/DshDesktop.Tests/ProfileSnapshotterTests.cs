using DshDesktop.Infrastructure.Plugins;

namespace DshDesktop.Tests;

/// <summary>
/// Profile 快照器测试（Q3-B：清单四件套快照，保留最近 5 份）。
/// Restore 的 pnpm 重建段需要真实 pnpm，覆盖不到；只测快照缺失分支。
/// </summary>
public sealed class ProfileSnapshotterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dsh-test-" + Guid.NewGuid().ToString("N"));

    private string ProfileDir => Path.Combine(_root, "profile");

    private string BackupsDir => Path.Combine(_root, "backups");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Test]
    public async Task CreateSnapshotAsync_CopiesExistingManifestFilesOnly()
    {
        Directory.CreateDirectory(ProfileDir);
        File.WriteAllText(Path.Combine(ProfileDir, "package.json"), "{}");
        File.WriteAllText(Path.Combine(ProfileDir, "cordis.patch.yml"), "[]");
        // pnpm-lock.yaml / .npmrc 缺失：应跳过而非报错。
        var snapshotter = new ProfileSnapshotter(ProfileDir, BackupsDir, "node", "pnpm.cjs");

        string snapshotId = await snapshotter.CreateSnapshotAsync(CancellationToken.None);

        string snapshotDir = Path.Combine(BackupsDir, snapshotId);
        await Assert.That(File.Exists(Path.Combine(snapshotDir, "package.json"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(snapshotDir, "cordis.patch.yml"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(snapshotDir, "pnpm-lock.yaml"))).IsFalse();
    }

    [Test]
    public async Task CreateSnapshotAsync_PrunesToFiveMostRecent()
    {
        Directory.CreateDirectory(ProfileDir);
        File.WriteAllText(Path.Combine(ProfileDir, "package.json"), "{}");

        // 预造 5 份"旧"快照（字典序 = 时间序）。
        for (int i = 0; i < 5; i++)
        {
            Directory.CreateDirectory(Path.Combine(BackupsDir, $"20000101-00000{i}"));
        }

        var snapshotter = new ProfileSnapshotter(ProfileDir, BackupsDir, "node", "pnpm.cjs");
        string newSnapshotId = await snapshotter.CreateSnapshotAsync(CancellationToken.None);

        string[] remaining = Directory.GetDirectories(BackupsDir);
        await Assert.That(remaining.Length).IsEqualTo(5);
        await Assert.That(Directory.Exists(Path.Combine(BackupsDir, "20000101-000000"))).IsFalse(); // 最旧被裁
        await Assert.That(Directory.Exists(Path.Combine(BackupsDir, newSnapshotId))).IsTrue();
    }

    [Test]
    public async Task RestoreAsync_MissingSnapshot_Throws()
    {
        Directory.CreateDirectory(ProfileDir);
        var snapshotter = new ProfileSnapshotter(ProfileDir, BackupsDir, "node", "pnpm.cjs");

        await Assert.That(async () => await snapshotter.RestoreAsync("20990101-000000", CancellationToken.None))
            .Throws<InvalidOperationException>();
    }

    /// <summary>
    /// 恢复重建不得含 --frozen-lockfile：回滚恰是 lockfile 与配置已漂移的场景，冻结安装必失败且会
    /// purge 改坏环境（2026-09 实机现场）。注入 runner 捕获参数，避免真实 pnpm。
    /// 断言精确到独立参数 --frozen-lockfile（--no-frozen-lockfile 含子串 frozen-lockfile 但不含 --frozen-lockfile）。
    /// </summary>
    [Test]
    public async Task RestoreAsync_RebuildUsesNoFrozenLockfile()
    {
        Directory.CreateDirectory(ProfileDir);
        Directory.CreateDirectory(BackupsDir);
        string snapshotId = "20990101-000000";
        Directory.CreateDirectory(Path.Combine(BackupsDir, snapshotId));
        File.WriteAllText(Path.Combine(BackupsDir, snapshotId, "package.json"), "{}");
        File.WriteAllText(Path.Combine(BackupsDir, snapshotId, "pnpm-lock.yaml"), "lockfileVersion: 1");

        List<string[]> calls = [];
        var runner = (string np, string tc, string wd, string[] args, CancellationToken ct) =>
        {
            calls.Add(args);
            return Task.FromResult((0, string.Empty));
        };
        var snapshotter = new ProfileSnapshotter(ProfileDir, BackupsDir, "node", "pnpm.cjs", runner);

        await snapshotter.RestoreAsync(snapshotId, CancellationToken.None);

        await Assert.That(calls.Count).IsGreaterThan(0);
        await Assert.That(calls.All(a => !a.Contains("--frozen-lockfile"))).IsTrue();
    }
}
