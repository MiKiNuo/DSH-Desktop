using DshDesktop.Infrastructure.Runtime;

namespace DshDesktop.Tests;

/// <summary>
/// harness 跨进程写锁/运行记录孤儿回收测试（2026-09-19 v0.1.6 实机）：
/// DSH 的 <c>dsh-atomic-write.withFileLock</c> 以 <c>wx</c> 独占创建 &lt;DSH_HOME&gt;\profiles\node_modules.lock、
/// 默认只等 2s，且**从不删除他人锁**（源码注释：orphan recovery is an operator action）。
/// 于是任何一次进程被强杀（更新器覆盖安装、任务管理器结束）留下的锁，都会让此后每次启动
/// 以 ExitCode=1 失败——本接缝就是那个 operator action。数据根迁移还会把旧根孤儿锁整树搬走，
/// 使该状态在新根永久复现。
///
/// 🚨 2026-10-11 实机扩展：harness 0.2.0-rc.2 的插件管理另有**两处**同类残留，
/// 它们不拦启动、只拦插件操作（用户表现为「官方插件与第三方市场全都不可用」），
/// 同样由 Desktop 强杀 Runtime（<c>Process.Kill(entireProcessTree)</c>）留下：
/// ① <c>&lt;profile&gt;/.plugin-manager/run.json</c>——<c>dsh-plugin-manager</c> 的
/// <c>runProfilePnpm</c> 第一步 <c>activeRecordedRun()</c> 命中它就 return exitCode=1、
/// <b>连 pnpm 都不启动</b>（判据：.plugin-manager/logs/operation-*/pnpm.log 全为 0 字节）。
/// ② <c>&lt;profile&gt;package.json.lock</c>——清单原子写的 wx 兄弟锁。
/// 二者格式不同：run.json 是 JSON 对象 <c>{"pid":N,"grouped":B}</c>，不是裸 PID。
/// 接缝：<see cref="HarnessWriterLockGuard"/>（internal 经 InternalsVisibleTo 直测），
/// 进程存活判定注入以保证用例确定性。
/// </summary>
public sealed class HarnessWriterLockGuardTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dsh-lock-" + Guid.NewGuid().ToString("N"));

    private string DshHome => Path.Combine(_root, "dsh-home");

    private string ProfileDir => Path.Combine(DshHome, "profiles", "web");

    private string LockPath => Path.Combine(DshHome, "profiles", "node_modules.lock");

    private string RunRecordPath => Path.Combine(ProfileDir, ".plugin-manager", "run.json");

    private string ManifestLockPath => Path.Combine(ProfileDir, "package.json.lock");

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

    // ===== 2026-10-11：插件管理两处残留（run.json / package.json.lock）=====

    /// <summary>写入插件侧残留文件并标成「陈旧」，越过最小年龄门槛。</summary>
    private void WriteAgedResidue(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-6));
    }

    /// <summary>写入当前时刻的残留文件（未越过年龄门槛）。</summary>
    private void WriteFreshResidue(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Test]
    public async Task TryReclaimOrphan_DeadPidInRunRecord_RemovesIt()
    {
        // 实机现场：{"pid":36664,"grouped":false}，mtime 六天前，进程早已不存在。
        // 判据：run.json 存在 ⇒ 此后每次插件 pnpm 操作都 exitCode=1 且 pnpm 根本不启动。
        WriteAgedResidue(RunRecordPath, "{\"pid\":36664,\"grouped\":false}");

        bool reclaimed = HarnessWriterLockGuard.TryReclaimOrphan(DshHome, _ => false);

        await Assert.That(reclaimed).IsTrue();
        await Assert.That(File.Exists(RunRecordPath)).IsFalse();
    }

    [Test]
    public async Task TryReclaimOrphan_LivePidInRunRecord_KeepsIt()
    {
        // 另一实例的 harness 正持有这次 pnpm 运行：抢记录会让两个 pnpm 同时改 profile。
        WriteAgedResidue(RunRecordPath, "{\"pid\":4242,\"grouped\":true}");

        bool reclaimed = HarnessWriterLockGuard.TryReclaimOrphan(DshHome, _ => true);

        await Assert.That(reclaimed).IsFalse();
        await Assert.That(File.Exists(RunRecordPath)).IsTrue();
    }

    [Test]
    public async Task TryReclaimOrphan_FreshRunRecord_KeepsIt()
    {
        // 过新：harness 可能刚写入、正在交接，误删会让在飞操作失去记录。
        WriteFreshResidue(RunRecordPath, "{\"pid\":9999,\"grouped\":false}");

        bool reclaimed = HarnessWriterLockGuard.TryReclaimOrphan(DshHome, _ => false);

        await Assert.That(reclaimed).IsFalse();
        await Assert.That(File.Exists(RunRecordPath)).IsTrue();
    }

    [Test]
    public async Task TryReclaimOrphan_UnparsableRunRecord_KeepsIt()
    {
        // 不是 harness 写的记录（无 pid 字段 / 损坏 JSON）：无法证明归属，不删。
        WriteAgedResidue(RunRecordPath, "not-json");

        bool reclaimed = HarnessWriterLockGuard.TryReclaimOrphan(DshHome, _ => false);

        await Assert.That(reclaimed).IsFalse();
        await Assert.That(File.Exists(RunRecordPath)).IsTrue();
    }

    [Test]
    public async Task TryReclaimOrphan_DeadPidInManifestLock_RemovesIt()
    {
        // 实机现场：package.json.lock 内容 "39312"，mtime 六天前。
        WriteAgedResidue(ManifestLockPath, "39312");

        bool reclaimed = HarnessWriterLockGuard.TryReclaimOrphan(DshHome, _ => false);

        await Assert.That(reclaimed).IsTrue();
        await Assert.That(File.Exists(ManifestLockPath)).IsFalse();
    }

    [Test]
    public async Task TryReclaimOrphan_LivePidInManifestLock_KeepsIt()
    {
        WriteAgedResidue(ManifestLockPath, "4242");

        bool reclaimed = HarnessWriterLockGuard.TryReclaimOrphan(DshHome, _ => true);

        await Assert.That(reclaimed).IsFalse();
        await Assert.That(File.Exists(ManifestLockPath)).IsTrue();
    }

    [Test]
    public async Task TryReclaimOrphan_AllThreeResidues_ReclaimsEveryOne()
    {
        // 三处同时残留（强杀一次即可造成）：一次调用必须全部清掉，
        // 否则用户会「删一个、试一次、又卡住」地反复往返。
        WriteAgedResidue(LockPath, "28572");
        WriteAgedResidue(RunRecordPath, "{\"pid\":36664,\"grouped\":false}");
        WriteAgedResidue(ManifestLockPath, "39312");

        bool reclaimed = HarnessWriterLockGuard.TryReclaimOrphan(DshHome, _ => false);

        await Assert.That(reclaimed).IsTrue();
        await Assert.That(File.Exists(LockPath)).IsFalse();
        await Assert.That(File.Exists(RunRecordPath)).IsFalse();
        await Assert.That(File.Exists(ManifestLockPath)).IsFalse();
    }

    [Test]
    public async Task TryReclaimOrphan_ModuleLockLiveButRunRecordDead_KeepsLiveAndReclaimsDead()
    {
        // 混合现场之一：模块锁（28572）活 → 必须留下；run 记录（4242）死 → 回收。
        WriteAgedResidue(LockPath, "28572");
        WriteAgedResidue(RunRecordPath, "{\"pid\":4242,\"grouped\":false}");

        bool reclaimed = HarnessWriterLockGuard.TryReclaimOrphan(DshHome, pid => pid == 28572);

        await Assert.That(reclaimed).IsTrue();
        await Assert.That(File.Exists(LockPath)).IsTrue();
        await Assert.That(File.Exists(RunRecordPath)).IsFalse();
    }

    [Test]
    public async Task TryReclaimOrphan_ModuleLockDeadButRunRecordLive_KeepsLiveAndReclaimsDead()
    {
        // 同一混合现场的对称面：模块锁（死）应被回收、run 记录（活）必须留下。
        // 两次用例合起来钉住「逐路径独立判定」，防止实现退化成「整体存活才动」或「全删」。
        WriteAgedResidue(LockPath, "28572");
        WriteAgedResidue(RunRecordPath, "{\"pid\":4242,\"grouped\":false}");

        bool reclaimed = HarnessWriterLockGuard.TryReclaimOrphan(DshHome, pid => pid == 4242);

        await Assert.That(reclaimed).IsTrue();
        await Assert.That(File.Exists(LockPath)).IsFalse();
        await Assert.That(File.Exists(RunRecordPath)).IsTrue();
    }
}
