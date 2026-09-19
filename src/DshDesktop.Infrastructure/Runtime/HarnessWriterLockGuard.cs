using System.Diagnostics;
using System.Globalization;
using Serilog;

namespace DshDesktop.Infrastructure.Runtime;

/// <summary>
/// DSH harness 跨进程写锁（<c>&lt;DSH_HOME&gt;\profiles\node_modules.lock</c>）的孤儿回收。
/// </summary>
/// <remarks>
/// harness 侧 <c>dsh-atomic-write.withFileLock</c> 以 <c>wx</c> 独占创建该锁，冲突即退避重试，
/// 默认只等 2s；且**从不删除他人锁**（源码注释：orphan recovery is an operator action）。
/// 于是任何一次进程被强杀（更新器覆盖安装、任务管理器结束）留下的锁都会让此后每次启动
/// 以 ExitCode=1 失败：<c>healProfilesModuleFallback</c> 拿不到锁 → 启动链直接终止，
/// 而日志只显示锁路径（2026-09-19 v0.1.6 实机）。数据根迁移还会把旧根的孤儿锁整树搬走，
/// 使该状态在新根被"继承"，用户侧表现为更新后彻底无法启动。
/// 本类承担那个 operator action：锁记录的 PID 已不存在 ⇒ 删除。
/// 只处理这一个路径——harness 的长生命周期跨进程锁仅此一处（模块 fallback）；
/// 其余 <c>&lt;file&gt;.lock</c> 均为单次原子写的瞬时锁（ponytail: 单一已知路径，上游新增锁点时同步扩展）。
/// </remarks>
internal static class HarnessWriterLockGuard
{
    /// <summary>锁文件相对 DSH_HOME 的路径（= healProfilesModuleFallback 的 withFileLock(modulesDir)）。</summary>
    private static readonly string RelativeLockPath = Path.Combine("profiles", "node_modules.lock");

    /// <summary>
    /// 文件过新不下手：harness 是先创建后写入 PID，只看到空文件的瞬间无法证明归属；
    /// 5s 远大于该写入窗口，也远小于任何真实启动的持锁时长。
    /// </summary>
    private static readonly TimeSpan MinimumAge = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 回收孤儿锁（PID 已不存在）并记日志；返回是否真的删除了锁。
    /// </summary>
    /// <param name="dshHome">DSH_HOME 目录（空值或锁缺失、内容非 PID、持有者存活时一律不动）。</param>
    /// <param name="isProcessAlive">进程存活判定（默认走真实 OS；测试注入以保证确定性）。</param>
    /// <returns>是否回收了一个孤儿锁。</returns>
    internal static bool TryReclaimOrphan(string dshHome, Func<int, bool>? isProcessAlive = null)
    {
        if (string.IsNullOrWhiteSpace(dshHome))
        {
            return false;
        }

        string lockPath = Path.Combine(dshHome, RelativeLockPath);
        try
        {
            var info = new FileInfo(lockPath);
            if (!info.Exists || DateTime.UtcNow - info.LastWriteTimeUtc < MinimumAge)
            {
                return false;
            }

            if (!int.TryParse(
                    File.ReadAllText(lockPath).Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int pid))
            {
                return false; // 内容不是 PID：无法证明是本 harness 的锁，不删。
            }

            if ((isProcessAlive ?? IsProcessAlive)(pid))
            {
                return false;
            }

            File.Delete(lockPath);
            Log.Logger.Warning(
                "Runtime.HarnessWriterLock.OrphanReclaimed {LockPath} Pid={Pid}", lockPath, pid);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 删不掉（权限 / 占用）不是本机制能解决的，交给随后的启动失败如实暴露。
            return false;
        }
    }

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false; // 无此 PID = 孤儿锁。
        }
        catch (Exception)
        {
            return true; // 无法判定（权限 / 竞态）：保守视为存活——误删活锁比留锁更危险（双写 fallback）。
        }
    }
}
