using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Serilog;

namespace DshDesktop.Infrastructure.Runtime;

/// <summary>
/// harness 跨进程写锁与插件运行记录的孤儿回收。
/// </summary>
/// <remarks>
/// 背景一（2026-09-19 v0.1.6）：harness 侧 <c>dsh-atomic-write.withFileLock</c> 以 <c>wx</c>
/// 独占创建 <c>&lt;DSH_HOME&gt;\profiles\node_modules.lock</c>，冲突即退避重试，默认只等 2s；
/// 且**从不删除他人锁**（源码注释：orphan recovery is an operator action）。于是任何一次进程被
/// 强杀（更新器覆盖安装、任务管理器结束）留下的锁都会让此后每次启动以 ExitCode=1 失败：
/// <c>healProfilesModuleFallback</c> 拿不到锁 → 启动链直接终止，而日志只显示锁路径。
/// 数据根迁移还会把旧根的孤儿锁整树搬走，使该状态在新根被"继承"，用户侧表现为更新后彻底无法启动。
///
/// 背景二（2026-10-11 实机）：harness 0.2.0-rc.2 的插件管理另有**两处**同类残留。
/// 它们不拦启动、只拦插件操作——用户表现为「官方插件与第三方市场全都不可用」，而宿主日志无 ERR：
/// ① <c>&lt;profile&gt;\.plugin-manager\run.json</c>：<c>dsh-plugin-manager</c> 的
/// <c>runProfilePnpm</c> 第一步 <c>activeRecordedRun()</c> 命中它即 return exitCode=1、
/// **连 pnpm 都不启动**；判据是 <c>.plugin-manager\logs\operation-*\pnpm.log</c> 全为 0 字节。
/// ② <c>&lt;profile&gt;\package.json.lock</c>：清单原子写的 wx 兄弟锁。
/// 二者同样由 Desktop 停止时的 <c>Process.Kill(entireProcessTree: true)</c> 留下。
///
/// 本类承担那个 operator action：记录里的 PID 已不存在 ⇒ 删除。
/// 三处的所有权判据一致（记录自己的持有者 pid），但**格式不同**：模块锁与清单锁是裸 PID 文本，
/// <c>run.json</c> 是 JSON 对象 <c>{"pid":N,"grouped":B}</c>（见 dsh-plugin-manager 的
/// <c>recordRun</c>），故解析分两路。任一处解析不出 PID 一律不删：无法证明归属时保守优先，
/// 误删活锁比留锁更危险（会让两个进程同时改 profile）。
/// 只处理这三处已知路径——上游新增锁点时在此同步扩展。
/// </remarks>
internal static class HarnessWriterLockGuard
{
    /// <summary>锁文件相对 DSH_HOME 的路径（= healProfilesModuleFallback 的 withFileLock(modulesDir)）。</summary>
    private static readonly string ModuleLockRelativePath = Path.Combine("profiles", "node_modules.lock");

    /// <summary>插件运行的 pnpm 进程记录相对 DSH_HOME 的路径（dsh-plugin-manager 的 recordRun）。</summary>
    private static readonly string RunRecordRelativePath =
        Path.Combine("profiles", "web", ".plugin-manager", "run.json");

    /// <summary>Profile 清单原子写的 wx 兄弟锁相对 DSH_HOME 的路径。</summary>
    private static readonly string ManifestLockRelativePath = Path.Combine("profiles", "web", "package.json.lock");

    /// <summary>
    /// 文件过新不下手：harness 是先创建后写入 PID，只看到空文件的瞬间无法证明归属；
    /// 5s 远大于该写入窗口，也远小于任何真实启动的持锁时长。
    /// </summary>
    private static readonly TimeSpan MinimumAge = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 回收孤儿锁与插件运行记录（PID 已不存在）并记日志；返回是否真的删除了其中之一。
    /// </summary>
    /// <param name="dshHome">DSH_HOME 目录（空值、记录缺失、内容无 PID、持有者存活时一律不动）。</param>
    /// <param name="isProcessAlive">进程存活判定（默认走真实 OS；测试注入以保证确定性）。</param>
    /// <returns>是否回收了一个孤儿记录。</returns>
    internal static bool TryReclaimOrphan(string dshHome, Func<int, bool>? isProcessAlive = null)
    {
        if (string.IsNullOrWhiteSpace(dshHome))
        {
            return false;
        }

        Func<int, bool> alive = isProcessAlive ?? IsProcessAlive;

        // 三处都要试、不能短路：一次强杀会同时留下全部三处，只回收第一处会让用户
        // 「删一个、试一次、又卡住」地反复往返。
        bool reclaimed = TryReclaimPlainPidRecord(Path.Combine(dshHome, ModuleLockRelativePath), alive)
            | TryReclaimPlainPidRecord(Path.Combine(dshHome, ManifestLockRelativePath), alive)
            | TryReclaimRunRecord(Path.Combine(dshHome, RunRecordRelativePath), alive);

        return reclaimed;
    }

    /// <summary>
    /// 裸 PID 文本记录（模块锁 / 清单锁）的孤儿回收。文件过新、内容非 PID、持有者存活时不删。
    /// </summary>
    private static bool TryReclaimPlainPidRecord(string path, Func<int, bool> isProcessAlive)
        => TryReclaim(path, isProcessAlive, ReadPid, "Runtime.HarnessWriterLock.OrphanReclaimed");

    /// <summary>
    /// <c>run.json</c>（<c>{"pid":N,"grouped":B}</c>）的孤儿回收；解析不出 pid 时不删。
    /// </summary>
    private static bool TryReclaimRunRecord(string path, Func<int, bool> isProcessAlive)
        => TryReclaim(
            path,
            isProcessAlive,
            ReadRunRecordPid,
            "Runtime.HarnessWriterLock.OrphanRunRecordReclaimed");

    /// <summary>
    /// 三处共用的回收流程：年龄门槛 → 取 PID → 存活判定 → 删除。
    /// 删不掉（权限 / 占用）不是本机制能解决的，交给随后的启动失败如实暴露。
    /// </summary>
    private static bool TryReclaim(
        string path,
        Func<int, bool> isProcessAlive,
        Func<string, int?> readPid,
        string eventName)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || DateTime.UtcNow - info.LastWriteTimeUtc < MinimumAge)
            {
                return false;
            }

            int? pid = readPid(File.ReadAllText(path));
            if (pid is not > 0 || isProcessAlive(pid.Value))
            {
                return false;
            }

            File.Delete(path);
            Log.Logger.Warning(eventName + " {LockPath} Pid={Pid}", path, pid.Value);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static int? ReadPid(string content)
        => int.TryParse(content.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int pid)
            ? pid
            : null;

    /// <summary>
    /// 从 <c>run.json</c> 取 pnpm 进程 pid；结构不符（损坏 JSON / 非对象 / pid 缺失或非法）时返回 null。
    /// pid 在 harness 侧是 <c>JSON.stringify</c> 后的<strong>数字</strong>（<c>{"pid":36664,...}</c>）。
    /// 走 <see cref="JsonDocument"/> 而非 <c>Deserialize&lt;T&gt;</c>：record 主构造的初始化属性
    /// 在 STJ 下不参与属性绑定，反序列化会静默产出默认值（实测 <c>pid=0</c>）而非报错——
    /// 那会让回收机制对真实残留也「判不出归属」而永不生效。不抛：解析失败即无法证明归属，
    /// 与裸 PID 路径的保守语义一致。
    /// </summary>
    private static int? ReadRunRecordPid(string content)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("pid", out JsonElement pidElement)
                || !pidElement.TryGetInt32(out int pid))
            {
                return null;
            }

            return pid > 0 ? pid : null;
        }
        catch (JsonException)
        {
            return null;
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