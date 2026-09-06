using DshDesktop.Domain.Plugins;
using DshDesktop.Infrastructure.Plugins;

namespace DshDesktop.Tests;

/// <summary>
/// Profile 插件仓库测试（§18：纯文件级插件管理；卸载四步的前三步 + cordis 补丁清理）。
/// 第 ④ 步 lockfile 重建需要真实 pnpm，覆盖不到——以 pnpmCjsPath=null 断言诚实失败并验证前三步效果。
/// </summary>
public sealed class PluginProfileRepositoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dsh-test-" + Guid.NewGuid().ToString("N"));

    private string ProfileDir => Path.Combine(_root, "profile");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Test]
    public async Task ListPluginsAsync_ParsesCoreEnabledAndVersion()
    {
        SeedProfile(
            dependencies: ["@deepseek-ai/dsh", "dshmarket", "dsh-foo"],
            bundles: ["@deepseek-ai/dsh", "dsh-foo"]); // dshmarket 不在 bundles = 禁用
        File.WriteAllText(
            Path.Combine(ProfileDir, "node_modules", "dsh-foo", "package.json"),
            "{\"version\":\"2.0.0\"}");
        var repository = new PluginProfileRepository(ProfileDir, "node", null);

        IReadOnlyList<PluginInfo> plugins = await repository.ListPluginsAsync(CancellationToken.None);

        await Assert.That(plugins.Count).IsEqualTo(3);
        PluginInfo core = plugins.Single(p => p.Name == "@deepseek-ai/dsh");
        await Assert.That(core.IsCore).IsTrue();
        PluginInfo market = plugins.Single(p => p.Name == "dshmarket");
        await Assert.That(market.IsCore).IsTrue();
        await Assert.That(market.Enabled).IsFalse();
        PluginInfo foo = plugins.Single(p => p.Name == "dsh-foo");
        await Assert.That(foo.IsCore).IsFalse();
        await Assert.That(foo.Enabled).IsTrue();
        await Assert.That(foo.Version).IsEqualTo("2.0.0");
    }

    [Test]
    public async Task SetEnabledAsync_Disable_RemovesFromBundlesOnly()
    {
        SeedProfile(dependencies: ["dsh-foo"], bundles: ["dsh-foo"]);
        var repository = new PluginProfileRepository(ProfileDir, "node", null);

        await repository.SetEnabledAsync("dsh-foo", false, CancellationToken.None);

        IReadOnlyList<PluginInfo> plugins = await repository.ListPluginsAsync(CancellationToken.None);
        await Assert.That(plugins.Count).IsEqualTo(1); // dependencies 保留
        await Assert.That(plugins[0].Enabled).IsFalse();
    }

    [Test]
    public async Task SetEnabledAsync_Enable_AddsToBundles()
    {
        SeedProfile(dependencies: ["dsh-foo"], bundles: []);
        var repository = new PluginProfileRepository(ProfileDir, "node", null);

        await repository.SetEnabledAsync("dsh-foo", true, CancellationToken.None);

        IReadOnlyList<PluginInfo> plugins = await repository.ListPluginsAsync(CancellationToken.None);
        await Assert.That(plugins[0].Enabled).IsTrue();
    }

    [Test]
    public async Task SetEnabledAsync_EnableNotInstalled_Throws()
    {
        SeedProfile(dependencies: ["dsh-foo"], bundles: ["dsh-foo"]);
        var repository = new PluginProfileRepository(ProfileDir, "node", null);

        await Assert.That(async () => await repository.SetEnabledAsync("dsh-ghost", true, CancellationToken.None))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task SetEnabledAsync_CorePlugin_Throws()
    {
        SeedProfile(dependencies: ["@deepseek-ai/dsh"], bundles: ["@deepseek-ai/dsh"]);
        var repository = new PluginProfileRepository(ProfileDir, "node", null);

        await Assert.That(async () => await repository.SetEnabledAsync("@deepseek-ai/dsh", false, CancellationToken.None))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task UninstallAsync_WithoutPnpm_FailsAfterFirstThreeSteps()
    {
        SeedProfile(dependencies: ["dsh-foo"], bundles: ["dsh-foo"]);
        Directory.CreateDirectory(Path.Combine(ProfileDir, "node_modules", "dsh-foo"));
        File.WriteAllText(
            Path.Combine(ProfileDir, "cordis.patch.yml"),
            "- id: base\n  name: dsh-foo\n- insert:\n  - name: dsh-foo\n  - name: dsh-foo2\n");
        var repository = new PluginProfileRepository(ProfileDir, "node", null);

        // 第 ④ 步（lockfile 重建）无 pnpm → 诚实失败。
        await Assert.That(async () => await repository.UninstallAsync("dsh-foo", CancellationToken.None))
            .Throws<InvalidOperationException>();

        // 但前三步已生效：manifest 清理 + 目录删除 + patch 条目清理。
        IReadOnlyList<PluginInfo> plugins = await repository.ListPluginsAsync(CancellationToken.None);
        await Assert.That(plugins.Count).IsEqualTo(0);
        await Assert.That(Directory.Exists(Path.Combine(ProfileDir, "node_modules", "dsh-foo"))).IsFalse();
        string patch = File.ReadAllText(Path.Combine(ProfileDir, "cordis.patch.yml"));
        await Assert.That(patch.Contains("dsh-foo2")).IsTrue();  // 无关条目保留
        await Assert.That(patch.Contains("name: dsh-foo\n")).IsFalse(); // 该插件条目已清（注意 dsh-foo2 是不同条目）
    }

    [Test]
    public async Task ListPluginsAsync_ReadsDescriptionFromInstalledManifest()
    {
        // Phase 8 评审 F3（Spec a.1）：description 读自 node_modules/<pkg>/package.json，缺失为空串。
        SeedProfile(dependencies: ["dsh-foo", "dsh-bar"], bundles: ["dsh-foo", "dsh-bar"]);
        Directory.CreateDirectory(Path.Combine(ProfileDir, "node_modules", "dsh-bar"));
        File.WriteAllText(
            Path.Combine(ProfileDir, "node_modules", "dsh-foo", "package.json"),
            "{\"version\":\"2.0.0\",\"description\":\"侧栏增强\"}");
        File.WriteAllText(
            Path.Combine(ProfileDir, "node_modules", "dsh-bar", "package.json"),
            "{\"version\":\"1.0.0\"}");
        var repository = new PluginProfileRepository(ProfileDir, "node", null);

        IReadOnlyList<PluginInfo> plugins = await repository.ListPluginsAsync(CancellationToken.None);

        await Assert.That(plugins.Single(p => p.Name == "dsh-foo").Description).IsEqualTo("侧栏增强");
        await Assert.That(plugins.Single(p => p.Name == "dsh-bar").Description).IsEqualTo("");
    }

    /// <summary>
    /// 造最小 Profile：package.json（dependencies + bundles）与 node_modules 目录骨架。
    /// </summary>
    private void SeedProfile(string[] dependencies, string[] bundles)
    {
        Directory.CreateDirectory(Path.Combine(ProfileDir, "node_modules", "dsh-foo"));
        string deps = string.Join(",", dependencies.Select(d => $"\"{d}\":\"*\""));
        string bundleList = string.Join(",", bundles.Select(b => $"\"{b}\""));
        File.WriteAllText(
            Path.Combine(ProfileDir, "package.json"),
            $"{{\"dependencies\":{{{deps}}},\"dsh\":{{\"profile\":{{\"bundles\":[{bundleList}]}}}}}}");
    }
}
