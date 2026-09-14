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
            var sourceInfo = new DirectoryInfo(sourceEntry);
            if (!sourceInfo.Exists
                || (sourceInfo.Attributes & FileAttributes.Directory) == 0
                || sourceInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                continue; // 源侧也没有可实体化的真实内容。
            }

            // 解除悬空联接点（仅删链接本身，不触碰目标），再复制真实内容。
            info.Delete(); // 非递归：junction 是空壳，删除即去掉联接点。
            CopyDirectory(sourceEntry, entry);
        }
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
