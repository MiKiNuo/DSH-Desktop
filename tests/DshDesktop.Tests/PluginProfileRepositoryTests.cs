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
            bundles: ["@deepseek-ai/dsh", "dsh-foo"]); // dshmarket 不在 bundles，但核心插件会自愈回启用
        File.WriteAllText(
            Path.Combine(ProfileDir, "node_modules", "dsh-foo", "package.json"),
            "{\"version\":\"2.0.0\"}");
        Directory.CreateDirectory(Path.Combine(ProfileDir, "node_modules", "dshmarket"));
        File.WriteAllText(
            Path.Combine(ProfileDir, "node_modules", "dshmarket", "package.json"),
            "{\"version\":\"1.45.1\"}");
        var repository = new PluginProfileRepository(ProfileDir, "node", null);

        IReadOnlyList<PluginInfo> plugins = await repository.ListPluginsAsync(CancellationToken.None);

        await Assert.That(plugins.Count).IsEqualTo(3);
        PluginInfo core = plugins.Single(p => p.Name == "@deepseek-ai/dsh");
        await Assert.That(core.IsCore).IsTrue();
        PluginInfo market = plugins.Single(p => p.Name == "dshmarket");
        await Assert.That(market.IsCore).IsTrue();
        await Assert.That(market.Enabled).IsTrue(); // 核心插件自愈回启用（见 CoreMissingFromBundles 用例）
        PluginInfo foo = plugins.Single(p => p.Name == "dsh-foo");
        await Assert.That(foo.IsCore).IsFalse();
        await Assert.That(foo.Enabled).IsTrue();
        await Assert.That(foo.Version).IsEqualTo("2.0.0");
    }

    [Test]
    public async Task ListPluginsAsync_CoreMissingFromBundles_HealsToEnabled()
    {
        // 核心插件只读 ⇒ UI/仓库层都无法禁用它，但清单可能被外部改坏（2026-09-18 实机：
        // dshmarket 不在 bundles，工作台不可用且 Desktop 无任何入口可救回）。
        // 约定：核心插件恒应启用，读取路径发现缺失即自愈写回 bundles。
        SeedProfile(dependencies: ["dshmarket", "dsh-foo"], bundles: ["dsh-foo"]);
        Directory.CreateDirectory(Path.Combine(ProfileDir, "node_modules", "dshmarket"));
        File.WriteAllText(
            Path.Combine(ProfileDir, "node_modules", "dshmarket", "package.json"),
            "{\"version\":\"1.45.1\"}");
        var repository = new PluginProfileRepository(ProfileDir, "node", null);

        IReadOnlyList<PluginInfo> plugins = await repository.ListPluginsAsync(CancellationToken.None);

        await Assert.That(plugins.Single(p => p.Name == "dshmarket").Enabled).IsTrue();
        // 自愈是持久修复：清单 bundles 已写回，下次 DSH 启动即生效。
        string manifest = File.ReadAllText(Path.Combine(ProfileDir, "package.json"));
        var bundles = (System.Text.Json.Nodes.JsonNode.Parse(manifest)!["dsh"]!["profile"]!["bundles"]!
            .AsArray()).Select(node => node!.GetValue<string>()).ToArray();
        await Assert.That(bundles.Contains("dshmarket")).IsTrue();
    }

    [Test]
    public async Task ListPluginsAsync_ThirdPartyMissingFromBundles_StaysDisabled()
    {
        // 对照：自愈只针对核心插件，第三方插件的禁用状态不得被改写。
        SeedProfile(dependencies: ["dsh-foo"], bundles: []);
        var repository = new PluginProfileRepository(ProfileDir, "node", null);

        IReadOnlyList<PluginInfo> plugins = await repository.ListPluginsAsync(CancellationToken.None);

        await Assert.That(plugins.Single(p => p.Name == "dsh-foo").Enabled).IsFalse();
    }

    [Test]
    public async Task ListPluginsAsync_CoreNotInstalledOnDisk_StaysDisabled()
    {
        // 评审回归（Spec 轴 c1）：dependencies 声明了核心插件但 node_modules 缺失时不得
        // 自愈写回 bundles——否则 DSH 启动 resolveBundleDir 抛错（ExitCode=1），
        // 把「禁用」修成「启动崩溃」。
        SeedProfile(dependencies: ["dshmarket"], bundles: []); // 注意：无 node_modules/dshmarket
        var repository = new PluginProfileRepository(ProfileDir, "node", null);

        IReadOnlyList<PluginInfo> plugins = await repository.ListPluginsAsync(CancellationToken.None);

        await Assert.That(plugins.Single(p => p.Name == "dshmarket").Enabled).IsFalse();
        string manifest = File.ReadAllText(Path.Combine(ProfileDir, "package.json"));
        await Assert.That(manifest.Contains("\"dshmarket\"", StringComparison.Ordinal)).IsTrue(); // dependencies 保留
        await Assert.That(manifest.Contains("\"bundles\":[]", StringComparison.Ordinal)).IsTrue(); // bundles 未被改写
    }

    [Test]
    public async Task HealCoreBundlesAsync_CoreMissing_WritesBack()
    {
        // 启动链路专用入口：不等用户访问插件页，InitializeRuntimeAsync 阶段显式自愈。
        SeedProfile(dependencies: ["dshmarket", "dsh-foo"], bundles: ["dsh-foo"]);
        Directory.CreateDirectory(Path.Combine(ProfileDir, "node_modules", "dshmarket"));
        File.WriteAllText(
            Path.Combine(ProfileDir, "node_modules", "dshmarket", "package.json"),
            "{\"version\":\"1.45.1\"}");
        var repository = new PluginProfileRepository(ProfileDir, "node", null);

        await repository.HealCoreBundlesAsync(CancellationToken.None);

        IReadOnlyList<PluginInfo> plugins = await repository.ListPluginsAsync(CancellationToken.None);
        await Assert.That(plugins.Single(p => p.Name == "dshmarket").Enabled).IsTrue();
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
    public async Task InstallAsync_DeclaredBundleUnresolvable_Throws()
    {
        // 回归（2026-09-14 实机）：清单声明了 bundle "dsh-myrules"，但磁盘上无法解析
        // （悬空 junction / 缺失）。pnpm add 退出码 0 不代表 bundle 可解析——
        // 旧实现直接返回成功，错误只在 Runtime 重启时才暴露。
        SeedProfile(
            dependencies: ["dsh-foo"],
            bundles: ["dsh-myrules"]);
        // 模拟 pnpm add 成功装入了 dsh-newplugin（含 dsh.bundle.patch），
        // 但 dsh-myrules 仍不可解析。
        Directory.CreateDirectory(Path.Combine(ProfileDir, "node_modules", "dsh-newplugin"));
        File.WriteAllText(
            Path.Combine(ProfileDir, "node_modules", "dsh-newplugin", "package.json"),
            "{\"dsh\":{\"bundle\":{\"patch\":{}}}}");

        // 需要一个存在的 pnpm 路径以通过前置检查（真实执行被 runOnce 桩拦截）。
        string pnpm = Path.Combine(_root, "pnpm.cjs");
        File.WriteAllText(pnpm, "");
        Func<string, string, string, string[], CancellationToken, Task<(int, string)>> runOnce =
            (_, _, _, _, _) => Task.FromResult((0, string.Empty));
        var repository = new PluginProfileRepository(ProfileDir, "node", pnpm, runOnce);

        InvalidOperationException? caught = null;
        try
        {
            await repository.InstallAsync("dsh-newplugin", CancellationToken.None);
        }
        catch (InvalidOperationException exception)
        {
            caught = exception;
        }

        await Assert.That(caught).IsNotNull();
        await Assert.That(caught!.Message).Contains("dsh-myrules");
    }

    [Test]
    public async Task InstallAsync_AllDeclaredBundlesResolvable_Succeeds()
    {
        // 对照：全部声明 bundle 均可解析时不得误报失败。
        SeedProfile(
            dependencies: ["dsh-foo"],
            bundles: ["dsh-myrules"]);
        Directory.CreateDirectory(Path.Combine(ProfileDir, "node_modules", "dsh-myrules"));
        File.WriteAllText(
            Path.Combine(ProfileDir, "node_modules", "dsh-myrules", "package.json"),
            "{}");
        Directory.CreateDirectory(Path.Combine(ProfileDir, "node_modules", "dsh-newplugin"));
        File.WriteAllText(
            Path.Combine(ProfileDir, "node_modules", "dsh-newplugin", "package.json"),
            "{\"dsh\":{\"bundle\":{\"patch\":{}}}}");

        string pnpm = Path.Combine(_root, "pnpm.cjs");
        File.WriteAllText(pnpm, "");
        Func<string, string, string, string[], CancellationToken, Task<(int, string)>> runOnce =
            (_, _, _, _, _) => Task.FromResult((0, string.Empty));
        var repository = new PluginProfileRepository(ProfileDir, "node", pnpm, runOnce);

        string installed = await repository.InstallAsync("dsh-newplugin", CancellationToken.None);

        await Assert.That(installed).IsEqualTo("dsh-newplugin");
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
