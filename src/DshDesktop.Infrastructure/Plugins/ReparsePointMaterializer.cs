using System.IO;
using System.Runtime.InteropServices;

namespace DshDesktop.Infrastructure.Plugins;

/// <summary>
/// 重解析点（junction/symlink）实体化：把 node_modules 下「目标无法解析」的联接点
/// 替换为源目录的真实内容。修复 robocopy 把源端 junction 原样复制成悬空 junction 的现场
/// （2026-09-14 实机：悬空 junction 使 DSH resolveBundleDir 抛
/// "cannot resolve profile bundle" → Runtime ExitCode=1；且后续 robocopy 无法在其中建子目录）。
/// <see cref="Materialize(string,string?)"/> 供 ProfileSeeder 复制后、PluginProfileRepository 安装后、
/// ProfileSnapshotter 回滚后共用同一步骤。
/// </summary>
internal static class ReparsePointMaterializer
{
    /// <summary>
    /// 遍历 nodeModulesDir 顶层，将目标无法解析的重解析点解除并复制 sourceNodeModulesDir 同名项的真实内容。
    /// sourceNodeModulesDir 为 null 时回退到默认 harness 种子路径查找（经
    /// <c>DSH_DESKTOP_TEST_SEED_NODE_MODULES</c> 环境变量覆盖便于测试），仍无源则不做处理——
    /// 无法无中生有，交由上层磁盘校验（PluginProfileRepository 安装后校验）报错而非静默成功。
    /// </summary>
    internal static void Materialize(string nodeModulesDir, string? sourceNodeModulesDir)
    {
        if (!Directory.Exists(nodeModulesDir))
        {
            return;
        }

        foreach (string entry in Directory.GetFileSystemEntries(nodeModulesDir))
        {
            var info = new DirectoryInfo(entry);
            if ((info.Attributes & FileAttributes.Directory) == 0
                || !info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                continue;
            }

            // 目标仍可解析：已是自包含实体（robocopy 跟随链接 materialize 的产物），无需处理。
            if (info.LinkTarget is { } target && Directory.Exists(target))
            {
                continue;
            }

            // 未显式传种子源时回退默认 harness 路径；仍无源则不做处理。
            string? source = sourceNodeModulesDir ?? ResolveDefaultSeedNodeModulesDir();
            if (source is null)
            {
                continue;
            }

            string sourceEntry = Path.Combine(source, info.Name);
            string? realSource = ResolveRealDirectory(sourceEntry);
            if (realSource is null)
            {
                continue; // 源侧无真实内容可实体化（含悬空/环/跳数超限）。
            }

            // 解除悬空联接点（仅删链接本身，不触碰目标），再从源侧真实内容复制。
            info.Delete(); // 非递归：junction 是空壳，删除即去掉联接点。
            CopyDirectory(realSource, entry);
        }
    }

    /// <summary>
    /// 把源侧条目（可能是真实目录，也可能是 junction/symlink）解析为「不含重解析点的真实目录」路径；
    /// 无法解析（目标不存在 / 环 / 跳数超限）返回 null，交由上层校验报错。
    /// probe 默认走真实文件系统；测试可注入假 probe 覆盖链接跟随分支。
    /// 链接目标可能相对，需相对链接自身所在目录解析为绝对路径才准。
    /// </summary>
    internal static string? ResolveRealDirectory(
        string entry,
        Func<string, (bool exists, bool isDir, bool isReparse, string? linkTarget)>? probe = null,
        int maxHops = 8)
        => ResolveRealDirectoryCore(entry, probe ?? RealProbe, maxHops);

    private static readonly Func<string, (bool exists, bool isDir, bool isReparse, string? linkTarget)> RealProbe =
        path =>
        {
            DirectoryInfo info;
            try
            {
                info = new DirectoryInfo(path);
            }
            catch
            {
                return (false, false, false, null);
            }

            if (!info.Exists)
            {
                // 悬空联接点：自身存在但目标不存在 → 无真实内容可复制。
                return (false, false, false, null);
            }

            bool isDir = (info.Attributes & FileAttributes.Directory) != 0;
            bool isReparse = info.Attributes.HasFlag(FileAttributes.ReparsePoint);
            string? linkTarget = isReparse ? info.LinkTarget : null;
            return (info.Exists, isDir, isReparse, linkTarget);
        };

    private static string? ResolveRealDirectoryCore(
        string start,
        Func<string, (bool exists, bool isDir, bool isReparse, string? linkTarget)> probe,
        int maxHops)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? current = start;
        int hops = 0;
        while (current is not null)
        {
            // 用 GetFullPath 归一化（斜杠/大小写）作环判定 key，避免 C:/a 与 C:\a 被当成两条路径。
            if (!visited.Add(Path.GetFullPath(current)))
            {
                return null; // 环：已访问路径再次出现 → 放弃，避免死循环。
            }

            var (exists, isDir, isReparse, linkTarget) = probe(current);
            if (!exists) return null;       // 目标不存在 → 无源。
            if (!isDir) return null;        // 不是目录 → 无法复制。
            if (!isReparse) return current; // 真实目录 → 命中。

            if (hops >= maxHops)
            {
                return null; // 跳数上限 → 放弃（链式链接过长/疑似环）。
            }

            hops++;
            current = Path.IsPathRooted(linkTarget!)
                ? Path.GetFullPath(linkTarget!)
                : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(current)!, linkTarget!));
        }

        return null;
    }

    /// <summary>
    /// 解析默认 harness 种子 node_modules 路径。测试可经 <c>DSH_DESKTOP_TEST_SEED_NODE_MODULES</c>
    /// 环境变量覆盖；否则按 Windows Roaming 布局拼出
    /// <c>%APPDATA%\dsh-desktop\harness\profiles\web\node_modules</c>。仅 Windows；
    /// 非 Windows 或路径不存在时返回 null。
    /// </summary>
    private static string? ResolveDefaultSeedNodeModulesDir()
    {
        static string? ExistsOrNull(string path) => Directory.Exists(path) ? path : null;

        string? testOverride = Environment.GetEnvironmentVariable("DSH_DESKTOP_TEST_SEED_NODE_MODULES");
        if (!string.IsNullOrEmpty(testOverride))
        {
            return ExistsOrNull(testOverride);
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return null;
        }

        string candidate = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "dsh-desktop", "harness", "profiles", "web", "node_modules");
        return ExistsOrNull(candidate);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }

        foreach (string dir in Directory.GetDirectories(source))
        {
            CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
        }
    }
}
