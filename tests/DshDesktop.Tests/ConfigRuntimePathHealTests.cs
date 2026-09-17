using System.IO;
using DshDesktop.Infrastructure.Config;

namespace DshDesktop.Tests;

/// <summary>
/// NodePath / DshEntryPath / WorkingDirectory 陈旧路径自愈测试（HealRuntimePaths，internal 经
/// InternalsVisibleTo 直测）。
/// 缺陷现场（2026-09-17）：借用的外部 Electron 安装目录被删除/改名后，配置仍指向旧路径，
/// 仅 PnpmCjsPath/NpmCjsPath 有自愈（HealToolPaths），NodePath 与 DshEntryPath 永不修正，
/// 致 Runtime 与插件工具链长期不可用。
/// 修复后语义：失效且重探测成功 → 重锚到新安装；失效且探测不到 → DshEntryPath 清空
/// （与 Detect 未探测到时同语义，启动链诚实失败）、NodePath 回退 "node"（PATH 系统 node）。
/// </summary>
public sealed class ConfigRuntimePathHealTests
{
    [Test]
    public async Task HealRuntimePaths_ValidPaths_Untouched()
    {
        string root = NewTempDir();
        try
        {
            string nodeExe = Touch(root, "node", "node.exe");
            string dshEntry = Touch(root, "dsh", "bin.js");
            DshDesktopConfig config = new() { NodePath = nodeExe, DshEntryPath = dshEntry, WorkingDirectory = root };

            bool dirty = DshDesktopConfigStore.HealRuntimePaths(config, () => null);

            await Assert.That(dirty).IsFalse();
            await Assert.That(config.NodePath).IsEqualTo(nodeExe);
            await Assert.That(config.DshEntryPath).IsEqualTo(dshEntry);
            await Assert.That(config.WorkingDirectory).IsEqualTo(root);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Test]
    public async Task HealRuntimePaths_StaleEntry_RedetectFound_ReanchorsEntryAndWorkingDirectory()
    {
        string root = NewTempDir();
        try
        {
            // 重探测到的安装布局：resources\app\node_modules\@deepseek-ai\dsh\lib\bin.js。
            string resources = Path.Combine(root, "resources");
            string appDir = Path.Combine(resources, "app");
            string newEntry = Touch(appDir, Path.Combine("node_modules", "@deepseek-ai", "dsh", "lib"), "bin.js");

            DshDesktopConfig config = new()
            {
                NodePath = Touch(root, "node", "node.exe"), // node 仍有效：不应被牵连改动
                DshEntryPath = Path.Combine(root, "gone", "bin.js"),
                WorkingDirectory = Path.Combine(root, "gone"),
            };

            bool dirty = DshDesktopConfigStore.HealRuntimePaths(config, () => resources);

            await Assert.That(dirty).IsTrue();
            await Assert.That(config.DshEntryPath).IsEqualTo(newEntry);
            await Assert.That(config.WorkingDirectory).IsEqualTo(appDir);
            await Assert.That(config.NodePath).IsEqualTo(Path.Combine(root, "node", "node.exe"));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Test]
    public async Task HealRuntimePaths_StaleEntry_RedetectMissing_ClearedToEmpty()
    {
        string root = NewTempDir();
        try
        {
            DshDesktopConfig config = new()
            {
                NodePath = Touch(root, "node", "node.exe"),
                DshEntryPath = Path.Combine(root, "gone", "bin.js"),
            };

            bool dirty = DshDesktopConfigStore.HealRuntimePaths(config, () => null);

            // 清空与 Detect 未探测到安装时的空路径同语义：启动链以明确错误反馈，而非抱着死路径。
            await Assert.That(dirty).IsTrue();
            await Assert.That(config.DshEntryPath).IsEqualTo(string.Empty);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Test]
    public async Task HealRuntimePaths_StaleNode_RedetectFound_UsesVendoredNode()
    {
        string root = NewTempDir();
        try
        {
            string resources = Path.Combine(root, "resources");
            string vendoredNode = Touch(
                Path.Combine(resources, "app"), Path.Combine("node_modules", "node", "bin"), "node.exe");

            DshDesktopConfig config = new()
            {
                NodePath = Path.Combine(root, "gone", "node.exe"),
                DshEntryPath = Touch(root, "dsh", "bin.js"), // 入口有效：不应被牵连改动
            };

            bool dirty = DshDesktopConfigStore.HealRuntimePaths(config, () => resources);

            await Assert.That(dirty).IsTrue();
            await Assert.That(config.NodePath).IsEqualTo(vendoredNode);
            await Assert.That(config.DshEntryPath).IsEqualTo(Path.Combine(root, "dsh", "bin.js"));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Test]
    public async Task HealRuntimePaths_StaleNode_RedetectMissing_FallsBackToSystemNode()
    {
        string root = NewTempDir();
        try
        {
            DshDesktopConfig config = new()
            {
                NodePath = Path.Combine(root, "gone", "node.exe"),
                DshEntryPath = Touch(root, "dsh", "bin.js"),
            };

            bool dirty = DshDesktopConfigStore.HealRuntimePaths(config, () => null);

            // 与 Detect 同语义：vendored node 不可得时回退 PATH 上的系统 node。
            await Assert.That(dirty).IsTrue();
            await Assert.That(config.NodePath).IsEqualTo("node");
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Test]
    public async Task HealRuntimePaths_NodePathLiteralNode_NotTreatedAsStale()
    {
        string root = NewTempDir();
        try
        {
            // "node" 是 PATH 解析的合法值（Detect 的回退产物），不是文件路径，不得判失效。
            DshDesktopConfig config = new()
            {
                NodePath = "node",
                DshEntryPath = Touch(root, "dsh", "bin.js"),
            };

            bool dirty = DshDesktopConfigStore.HealRuntimePaths(config, () => null);

            await Assert.That(dirty).IsFalse();
            await Assert.That(config.NodePath).IsEqualTo("node");
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Test]
    public async Task HealRuntimePaths_EmptyEntry_NotTouched()
    {
        // 入口从未配置（空串）属于「未探测到」的合法状态，不是陈旧路径：不动。
        DshDesktopConfig config = new() { DshEntryPath = string.Empty, NodePath = "node" };

        bool dirty = DshDesktopConfigStore.HealRuntimePaths(config, () => null);

        await Assert.That(dirty).IsFalse();
        await Assert.That(config.DshEntryPath).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task HealRuntimePaths_HealthyConfig_DetectorNotInvoked()
    {
        string root = NewTempDir();
        try
        {
            // 路径全部有效时不得触发重探测（FindElectronResourcesDir 是全盘扫描，健康启动不该付这个价）。
            DshDesktopConfig config = new()
            {
                NodePath = Touch(root, "node", "node.exe"),
                DshEntryPath = Touch(root, "dsh", "bin.js"),
            };
            bool invoked = false;

            bool dirty = DshDesktopConfigStore.HealRuntimePaths(config, () => { invoked = true; return null; });

            await Assert.That(dirty).IsFalse();
            await Assert.That(invoked).IsFalse();
        }
        finally
        {
            Cleanup(root);
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

    private static void Cleanup(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
