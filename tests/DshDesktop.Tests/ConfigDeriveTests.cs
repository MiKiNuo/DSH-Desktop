using DshDesktop.Infrastructure.Config;

namespace DshDesktop.Tests;

/// <summary>
/// 配置工具路径推导测试（internal 方法经 InternalsVisibleTo 直测）。
/// 只测确定性用例：DeriveNpmCjsPath 的 PATH 回退分支结果环境相关，不在此断言。
/// </summary>
public sealed class ConfigDeriveTests
{
    [Test]
    public async Task DeriveNpmCjsPath_NodeSiblingNpm_ReturnsSiblingPath()
    {
        string root = Path.Combine(Path.GetTempPath(), "dsh-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            string nodeDir = Path.Combine(root, "node");
            string npmCli = Path.Combine(nodeDir, "node_modules", "npm", "bin", "npm-cli.js");
            Directory.CreateDirectory(Path.GetDirectoryName(npmCli)!);
            string nodePath = Path.Combine(nodeDir, "node.exe");
            File.WriteAllText(nodePath, string.Empty);
            File.WriteAllText(npmCli, string.Empty);

            string? derived = DshDesktopConfigStore.DeriveNpmCjsPath(nodePath);

            await Assert.That(derived).IsEqualTo(npmCli);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task DerivePnpmCjsPath_FullLayout_ReturnsPnpmPath()
    {
        string root = Path.Combine(Path.GetTempPath(), "dsh-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            // 布局：node_modules/@deepseek-ai/dsh/lib/bin.js + node_modules/pnpm/bin/pnpm.cjs
            string dshLib = Path.Combine(root, "node_modules", "@deepseek-ai", "dsh", "lib");
            Directory.CreateDirectory(dshLib);
            string dshEntry = Path.Combine(dshLib, "bin.js");
            File.WriteAllText(dshEntry, string.Empty);
            string pnpmCjs = Path.Combine(root, "node_modules", "pnpm", "bin", "pnpm.cjs");
            Directory.CreateDirectory(Path.GetDirectoryName(pnpmCjs)!);
            File.WriteAllText(pnpmCjs, string.Empty);

            string? derived = DshDesktopConfigStore.DerivePnpmCjsPath(dshEntry);

            await Assert.That(derived).IsEqualTo(pnpmCjs);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task DerivePnpmCjsPath_MissingPnpmDirectory_ReturnsNull()
    {
        string root = Path.Combine(Path.GetTempPath(), "dsh-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            string dshLib = Path.Combine(root, "node_modules", "@deepseek-ai", "dsh", "lib");
            Directory.CreateDirectory(dshLib);
            string dshEntry = Path.Combine(dshLib, "bin.js");
            File.WriteAllText(dshEntry, string.Empty);

            string? derived = DshDesktopConfigStore.DerivePnpmCjsPath(dshEntry);

            await Assert.That(derived).IsNull();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task DerivePnpmCjsPath_NullOrBlank_ReturnsNull()
    {
        await Assert.That(DshDesktopConfigStore.DerivePnpmCjsPath(null)).IsNull();
        await Assert.That(DshDesktopConfigStore.DerivePnpmCjsPath("")).IsNull();
        await Assert.That(DshDesktopConfigStore.DerivePnpmCjsPath("   ")).IsNull();
    }
}
