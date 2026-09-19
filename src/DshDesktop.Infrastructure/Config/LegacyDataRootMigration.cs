using DshDesktop.Infrastructure.Plugins;

namespace DshDesktop.Infrastructure.Config;

/// <summary>
/// 旧数据根（%LOCALAPPDATA%\DshDesktop\data）→ 新数据根（安装根\data）的一次性迁移（ADR-0009）。
/// 不迁移则回迁后安装版首启退化为全新环境：自建 Runtime、Profile、插件、配置全丢。
/// 形态：复制到同级暂存目录（同盘）→ 逐顶层条目 Move 进新根（config 最后，作为完成标记）
/// → 删旧根。中断可重入：未完成时新根无 config，下次启动重试（<see cref="IsMigrationNeeded"/>）。
/// 目录 junction 按目标实体化复制（pnpm node_modules 大量 junction；原样复制会留下指向旧根的
/// 悬空链接，旧根删除即断——ReparsePointMaterializer 同款教训），环以 visited 集合防御。
/// internal 供 DshDesktop.Tests 直测（InternalsVisibleTo）。
/// </summary>
internal static class LegacyDataRootMigration
{
    /// <summary>
    /// 迁移完成标记的相对路径：存在即视为已迁过 / 新根已被使用，绝不再搬。
    /// </summary>
    private static readonly string ConfigMarkerRelativePath =
        Path.Combine("config", "dsh-desktop.config.json");

    /// <summary>
    /// 判定是否需要迁移：新旧不同路径 + 旧根存在 + 新根无完成标记。
    /// </summary>
    internal static bool IsMigrationNeeded(string legacyRoot, string newRoot)
    {
        if (PathsEqual(legacyRoot, newRoot) || !Directory.Exists(legacyRoot))
        {
            return false;
        }

        return !File.Exists(Path.Combine(newRoot, ConfigMarkerRelativePath));
    }

    /// <summary>
    /// 执行迁移。调用方负责先用 <see cref="IsMigrationNeeded"/> 判定；失败抛异常（暂存目录已尽力
    /// 清理，新根可能部分并入——config 标记最后移动，未完成态下次启动会重试）。
    /// </summary>
    internal static void Migrate(string legacyRoot, string newRoot)
    {
        // 暂存目录必须在**新根内部**：新根有 users-modify ACL（安装器预建），而其父目录
        // （Program Files 下的 {app}）标准用户不可写——暂存放父目录会让迁移在唯一真实场景
        // （安装形态 + 标准用户）必抛 UnauthorizedAccessException（独立评审发现）。
        // 新根可能尚不存在：CreateDirectory(staging) 会连带创建；父目录不可写时如实抛错。
        Directory.CreateDirectory(newRoot);

        // 上次失败遗留的暂存目录：先清理再开工（内容已被并入或已过期，留着只会越积越多）。
        foreach (string leftover in Directory.GetDirectories(newRoot, ".migrating-*"))
        {
            Directory.Delete(leftover, recursive: true);
        }

        string staging = Path.Combine(newRoot, ".migrating-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyTree(legacyRoot, staging, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

            // 并入新根（暂存与新根同卷，Move 是元数据操作）；config 标记最后，保证可重入。
            foreach (string entry in Directory.GetFileSystemEntries(staging)
                         .OrderBy(e => Path.GetFileName(e) == "config" ? 1 : 0))
            {
                string destination = Path.Combine(newRoot, Path.GetFileName(entry));
                if (Directory.Exists(entry))
                {
                    if (Directory.Exists(destination))
                    {
                        // 新根已有同名目录（上次中断的残留）：递归并入而非整目录替换。
                        MergeDirectory(entry, destination);
                    }
                    else
                    {
                        Directory.Move(entry, destination);
                    }
                }
                else if (!File.Exists(destination))
                {
                    File.Move(entry, destination);
                }
            }

            Directory.Delete(staging, recursive: true);
            // 旧根整树删除：junction 只删链接本身（Directory.Delete 不跟随重解析点），不误伤外部目标。
            Directory.Delete(legacyRoot, recursive: true);
        }
        catch
        {
            try
            {
                if (Directory.Exists(staging))
                {
                    Directory.Delete(staging, recursive: true);
                }
            }
            catch (Exception)
            {
                // 清理失败不掩盖原始异常。
            }

            throw;
        }
    }

    /// <summary>
    /// 递归复制；目录 junction 解析为真实目录后按内容复制（无法解析的悬空链接跳过——
    /// 它本就不可用，搬一个死链接没有意义）。
    /// </summary>
    private static void CopyTree(string source, string destination, HashSet<string> visited)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }

        foreach (string dir in Directory.GetDirectories(source))
        {
            string realSource = dir;
            var info = new DirectoryInfo(dir);
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                string? resolved = ReparsePointMaterializer.ResolveRealDirectory(dir);
                if (resolved is null)
                {
                    continue; // 悬空链接：目标已失，跳过。
                }

                realSource = resolved;
            }

            if (!visited.Add(Path.GetFullPath(realSource)))
            {
                continue; // junction 环 / 重复目标：防死循环与重复拷贝。
            }

            CopyTree(realSource, Path.Combine(destination, Path.GetFileName(dir)), visited);
        }
    }

    /// <summary>
    /// 新根已有同名目录时的并入：源内容 Move 进目标，冲突（目标已存在）保留目标。
    /// </summary>
    private static void MergeDirectory(string source, string destination)
    {
        foreach (string entry in Directory.GetFileSystemEntries(source))
        {
            string target = Path.Combine(destination, Path.GetFileName(entry));
            if (Directory.Exists(entry))
            {
                if (Directory.Exists(target))
                {
                    MergeDirectory(entry, target);
                }
                else
                {
                    Directory.Move(entry, target);
                }
            }
            else if (!File.Exists(target))
            {
                File.Move(entry, target);
            }
        }

        Directory.Delete(source, recursive: true);
    }

    private static bool PathsEqual(string left, string right)
        => string.Equals(
            left.TrimEnd(Path.DirectorySeparatorChar),
            right.TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
}
