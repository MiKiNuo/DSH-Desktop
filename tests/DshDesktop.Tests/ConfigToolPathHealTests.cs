using System.IO;
using DshDesktop.Infrastructure.Config;

namespace DshDesktop.Tests;

/// <summary>
/// PnpmCjsPath / NpmCjsPath 陈旧路径修复测试（HealToolPaths，internal 经 InternalsVisibleTo 直测）。
/// 缺陷：旧守卫仅在字段为空时才重推导；字段为非空但文件已失效时永不修正，致插件安装 / 回滚重建失败。
/// 修复后：为空或指向文件不存在都重推导；推导失败则清除（置 null）。
/// </summary>
public sealed class ConfigToolPathHealTests
{
    [Test]
    public async Task LoadOrDetect_StalePnpmCjsPath_MissingFile_IsClearedOrRederived()
    {
        string root = NewTempDir();
        try
        {
            // 完整布局，pnpm.cjs 可推导。
            string dshLib = Path.Combine(root, "node_modules", "@deepseek-ai", "dsh", "lib");
            Directory.CreateDirectory(dshLib);
            string dshEntry = Path.Combine(dshLib, "bin.js");
            File.WriteAllText(dshEntry, string.Empty);
            string pnpmCjs = Path.Combine(root, "node_modules", "pnpm", "bin", "pnpm.cjs");
            Directory.CreateDirectory(Path.GetDirectoryName(pnpmCjs)!);
            File.WriteAllText(pnpmCjs, string.Empty);

            string stale = Path.Combine(root, "gone", "pnpm.cjs");
            DshDesktopConfig config = new() { DshEntryPath = dshEntry, PnpmCjsPath = stale };
            await Assert.That(File.Exists(stale)).IsFalse(); // 守卫前提：陈旧路径文件确实不存在

            DshDesktopConfigStore.HealToolPaths(config);

            // 不再指向失效路径；此处应被重推导为有效存在的 pnpm.cjs。
            await Assert.That(config.PnpmCjsPath == stale).IsFalse();
            await Assert.That(config.PnpmCjsPath).IsEqualTo(pnpmCjs);
            await Assert.That(File.Exists(config.PnpmCjsPath)).IsTrue();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public async Task LoadOrDetect_StalePnpmCjsPath_MissingFile_NoDerive_ClearedToNull()
    {
        string root = NewTempDir();
        try
        {
            // 有 dsh 入口但无 pnpm 目录 ⇒ 推导失败，陈旧值应被清除为 null。
            string dshLib = Path.Combine(root, "node_modules", "@deepseek-ai", "dsh", "lib");
            Directory.CreateDirectory(dshLib);
            string dshEntry = Path.Combine(dshLib, "bin.js");
            File.WriteAllText(dshEntry, string.Empty);

            string stale = Path.Combine(root, "gone", "pnpm.cjs");
            DshDesktopConfig config = new() { DshEntryPath = dshEntry, PnpmCjsPath = stale };

            DshDesktopConfigStore.HealToolPaths(config);

            await Assert.That(config.PnpmCjsPath).IsNull();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public async Task LoadOrDetect_StaleNpmCjsPath_MissingFile_IsClearedOrRederived()
    {
        string root = NewTempDir();
        try
        {
            // node 旁有 npm ⇒ 可推导。
            string nodeDir = Path.Combine(root, "node");
            Directory.CreateDirectory(nodeDir);
            string nodeExe = Path.Combine(nodeDir, "node.exe");
            File.WriteAllText(nodeExe, string.Empty);
            string npmCli = Path.Combine(nodeDir, "node_modules", "npm", "bin", "npm-cli.js");
            Directory.CreateDirectory(Path.GetDirectoryName(npmCli)!);
            File.WriteAllText(npmCli, string.Empty);

            string stale = Path.Combine(root, "gone", "npm-cli.js");
            DshDesktopConfig config = new() { NodePath = nodeExe, NpmCjsPath = stale };
            await Assert.That(File.Exists(stale)).IsFalse();

            DshDesktopConfigStore.HealToolPaths(config);

            // 不再指向失效路径，且被重推导为有效存在的 npm-cli.js。
            await Assert.That(config.NpmCjsPath == stale).IsFalse();
            await Assert.That(config.NpmCjsPath).IsEqualTo(npmCli);
            await Assert.That(File.Exists(config.NpmCjsPath)).IsTrue();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public async Task LoadOrDetect_ValidPnpmCjsPath_Preserved()
    {
        string root = NewTempDir();
        try
        {
            // 有效存在的 pnpm.cjs 必须原样保留（反向用例：防止把好路径也清掉）。
            string pnpmCjs = Touch(root, "pnpm", "pnpm.cjs");
            string dshLib = Path.Combine(root, "node_modules", "@deepseek-ai", "dsh", "lib");
            Directory.CreateDirectory(dshLib);
            string dshEntry = Path.Combine(dshLib, "bin.js");
            File.WriteAllText(dshEntry, string.Empty);

            DshDesktopConfig config = new() { DshEntryPath = dshEntry, PnpmCjsPath = pnpmCjs };
            await Assert.That(File.Exists(pnpmCjs)).IsTrue();

            DshDesktopConfigStore.HealToolPaths(config);

            await Assert.That(config.PnpmCjsPath).IsEqualTo(pnpmCjs);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
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
