using DshDesktop.Infrastructure.Runtime;

namespace DshDesktop.Tests;

/// <summary>
/// harness 跨进程写锁孤儿回收测试（2026-09-19 v0.1.6 实机）：
/// DSH 的 <c>dsh-atomic-write.withFileLock</c> 以 <c>wx</c> 独占创建 &lt;DSH_HOME&gt;\profiles\node_modules.lock、
/// 默认只等 2s，且**从不删除他人锁**（源码注释：orphan recovery is an operator action）。
/// 于是任何一次进程被强杀（更新器覆盖安装、任务管理器结束）留下的锁，都会让此后每次启动
/// 以 ExitCode=1 失败——本接缝就是那个 operator action。数据根迁移还会把旧根孤儿锁整树搬走，
/// 使该状态在新根永久复现。
/// 接缝：<see cref="HarnessWriterLockGuard"/>（internal 经 InternalsVisibleTo 直测），
/// 进程存活判定注入以保证用例确定性。
/// </summary>
public sealed class HarnessWriterLockGuardTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dsh-lock-" + Guid.NewGuid().ToString("N"));

    private string DshHome => Path.Combine(_root, "dsh-home");

    private string LockPath => Path.Combine(DshHome, "profiles", "node_modules.lock");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>写入锁文件并把它标成「陈旧」（越过最小年龄门槛，避免误撞 harness 建后即写的窗口）。</summary>
    private void WriteAgedLock(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LockPath)!);
        File.WriteAllText(LockPath, content);
        File.SetLastWriteTimeUtc(LockPath, DateTime.UtcNow.AddDays(-6));
    }

    [Test]
    public async Task TryReclaimOrphan_DeadOwnerProcess_RemovesLock()
    {
        // 实机现场：锁内容 28572、mtime 六天前，持有进程早已不存在。
        WriteAgedLock("28572\n");

        bool reclaimed = HarnessWriterLockGuard.TryReclaimOrphan(DshHome, _ => false);

        await Assert.That(reclaimed).IsTrue();
        await Assert.That(File.Exists(LockPath)).IsFalse();
    }

    [Test]
    public async Task TryReclaimOrphan_LiveOwnerProcess_KeepsLock()
    {
        // 真实持有者（另一实例的 DSH 正在 heal）绝不能被抢：抢锁会让两个进程同时改 fallback。
        WriteAgedLock("4242\n");

        bool reclaimed = HarnessWriterLockGuard.TryReclaimOrphan(DshHome, _ => true);

        await Assert.That(reclaimed).IsFalse();
        await Assert.That(File.Exists(LockPath)).IsTrue();
    }

    [Test]
    public async Task TryReclaimOrphan_FreshLock_KeepsLock()
    {
        // 文件过新：harness 是「创建即写 PID」，只看到空文件的瞬间不可判定归属（不能误删活锁）。
        Directory.CreateDirectory(Path.GetDirectoryName(LockPath)!);
        File.WriteAllText(LockPath, string.Empty);

        bool reclaimed = HarnessWriterLockGuard.TryReclaimOrphan(DshHome, _ => false);

        await Assert.That(reclaimed).IsFalse();
        await Assert.That(File.Exists(LockPath)).IsTrue();
    }

    [Test]
    public async Task TryReclaimOrphan_UnparsableOwner_KeepsLock()
    {
        // 不是本 harness 的锁（内容非 PID）：无法证明归属，不删。
        WriteAgedLock("not-a-pid");

        bool reclaimed = HarnessWriterLockGuard.TryReclaimOrphan(DshHome, _ => false);

        await Assert.That(reclaimed).IsFalse();
        await Assert.That(File.Exists(LockPath)).IsTrue();
    }

    [Test]
    public async Task TryReclaimOrphan_NoLock_NoOp()
    {
        Directory.CreateDirectory(DshHome);

        bool reclaimed = HarnessWriterLockGuard.TryReclaimOrphan(DshHome, _ => false);

        await Assert.That(reclaimed).IsFalse();
    }

    [Test]
    public async Task TryReclaimOrphan_LockUndeletable_ReportsFalseWithoutThrowing()
    {
        // 锁被占用 / 权限不足：删除会抛 IOException —— 必须吞掉并如实返回 false，
        // 绝不能让回收动作本身变成启动路径上的新失败点。
        WriteAgedLock("28572\n");
        using FileStream blocker = new(
            LockPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        bool reclaimed = HarnessWriterLockGuard.TryReclaimOrphan(DshHome, _ => false);

        await Assert.That(reclaimed).IsFalse();
        await Assert.That(File.Exists(LockPath)).IsTrue();
    }

    [Test]
    public async Task TryReclaimOrphan_BlankDshHome_NoOp()
    {
        bool reclaimed = HarnessWriterLockGuard.TryReclaimOrphan("   ", _ => false);

        await Assert.That(reclaimed).IsFalse();
    }
}
