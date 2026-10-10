using DshDesktop.Application.Plugins;
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
    public async Task InstallAsync_StaleBundleDeclaration_IsPrunedAndInstallSucceeds()
    {
        // 2026-10-10 实机：bundles 残留「既不在 dependencies、磁盘也不存在」的声明
        // （历史安装/卸载不完整留下，如 @deepseek-ai/dsh-experimental-agent-team-profile），
        // pnpm 只物化 dependencies，重建 lockfile 永不可能修复它 → 旧实现每次安装都抛错回滚，
        // 所有插件安装被同一残留永久卡死。约定：此类不可恢复声明自动摘除并 WARN，不再阻塞安装。
        // （2026-09-14「本次安装打坏既有 bundle 仍须抛错」的保护改由
        //   InstallAsync_InstallBreaksPreviouslyResolvedBundle_Throws 承载。）
        SeedProfile(
            dependencies: ["dsh-foo"],
            bundles: ["dsh-ghost"]); // dsh-ghost：不在 dependencies、磁盘也不存在
        Directory.CreateDirectory(Path.Combine(ProfileDir, "node_modules", "dsh-newplugin"));
        File.WriteAllText(
            Path.Combine(ProfileDir, "node_modules", "dsh-newplugin", "package.json"),
            "{\"dsh\":{\"bundle\":{\"patch\":{}}}}");

        // 需要一个存在的 pnpm 路径以通过前置检查（真实执行被 runOnce 桩拦截）。
        string pnpm = Path.Combine(_root, "pnpm.cjs");
        File.WriteAllText(pnpm, "");
        List<string[]> pnpmCalls = [];
        List<string> warnings = [];
        Func<string, string, string, string[], CancellationToken, Task<(int, string)>> runOnce =
            (_, _, _, args, _) =>
            {
                pnpmCalls.Add(args);
                return Task.FromResult((0, string.Empty));
            };
        var repository = new PluginProfileRepository(
            ProfileDir, "node", pnpm, runOnce, onWarning: warnings.Add);

        string installed = await repository.InstallAsync("dsh-newplugin", CancellationToken.None);

        await Assert.That(installed).IsEqualTo("dsh-newplugin");
        // 摘除后清单只剩安装 reconcile 写回的 dsh-newplugin，dsh-ghost 不再出现。
        string manifest = File.ReadAllText(Path.Combine(ProfileDir, "package.json"));
        await Assert.That(manifest.Contains("dsh-ghost", StringComparison.Ordinal)).IsFalse();
        // 摘除即干净：无需触发 lockfile 重建（pnpm install）。
        await Assert.That(pnpmCalls.All(args => args[0] == "add")).IsTrue();
        // 摘除动作有 WARN 痕迹可查（通知/诊断链路）。
        await Assert.That(warnings.Count).IsEqualTo(1);
        await Assert.That(warnings[0]).Contains("dsh-ghost");
    }

    [Test]
    public async Task InstallAsync_PreExistingDamagedDependency_WarnsButSucceeds()
    {
        // 既有损坏（声明在 dependencies 里、安装前就不可解析，重建也修不好，如离线缺包）属历史问题：
        // WARN 放行而非抛错回滚——本次无辜安装不应为既有损坏陪葬；
        // Runtime 启动期仍会精确报 cannot resolve profile bundle，不会因放行而掩盖。
        SeedProfile(
            dependencies: ["dsh-myrules", "dsh-newplugin"],
            bundles: ["dsh-myrules"]); // dsh-myrules 在 dependencies 但磁盘缺失（既有损坏）
        WriteInstalledPackage("dsh-newplugin", "{\"dsh\":{\"bundle\":{\"patch\":{}}}}");
        string pnpm = Path.Combine(_root, "pnpm.cjs");
        File.WriteAllText(pnpm, "");
        List<string> warnings = [];
        // 桩：pnpm add / install 都成功但都不恢复 dsh-myrules（修不好的既有损坏）。
        IPluginManager repository = new PluginProfileRepository(
            ProfileDir, "node", pnpm,
            (_, _, _, _, _) => Task.FromResult((0, string.Empty)),
            onWarning: warnings.Add);

        string installed = await repository.InstallAsync("dsh-newplugin@latest", CancellationToken.None);
        IReadOnlyList<PluginInfo> plugins = await repository.ListPluginsAsync(CancellationToken.None);

        await Assert.That(installed).IsEqualTo("dsh-newplugin");
        await Assert.That(plugins.Single(p => p.Name == "dsh-newplugin").Enabled).IsTrue();
        // 在 dependencies 里的声明不属「不可恢复残留」，不得被摘除。
        await Assert.That(plugins.Single(p => p.Name == "dsh-myrules").Enabled).IsTrue();
        await Assert.That(warnings.Any(w => w.Contains("dsh-myrules", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task InstallAsync_InstallBreaksPreviouslyResolvedBundle_Throws()
    {
        // 回归（2026-09-14 实机）：pnpm add 退出码 0 不代表 bundle 可解析——它可能把原本
        // 已物化的真实依赖重建为悬空 junction。「本次安装打坏的 bundle」属新增损坏，
        // 必须抛错回滚，不得被「既有损坏降级 WARN」放行。
        SeedProfile(
            dependencies: ["dsh-myrules"],
            bundles: ["dsh-myrules"]);
        WriteInstalledPackage("dsh-myrules", "{}"); // 安装前可解析
        WriteInstalledPackage("dsh-newplugin", "{\"dsh\":{\"bundle\":{\"patch\":{}}}}");
        string pnpm = Path.Combine(_root, "pnpm.cjs");
        File.WriteAllText(pnpm, "");
        var runOnce = (string np, string tc, string wd, string[] args, CancellationToken ct) =>
        {
            if (args[0] == "add")
            {
                // 模拟 pnpm add 把已物化的 dsh-myrules 打成悬空 junction（且重建修不回来）。
                Directory.Delete(Path.Combine(ProfileDir, "node_modules", "dsh-myrules"), recursive: true);
            }

            return Task.FromResult((0, string.Empty));
        };
        IPluginManager repository = new PluginProfileRepository(ProfileDir, "node", pnpm, runOnce);

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
    public async Task InstallAsync_RebuildResolvesMissingBundle_Succeeds()
    {
        SeedProfile(
            dependencies: ["dsh-myrules", "dsh-newplugin"],
            bundles: ["dsh-myrules"]);
        WriteInstalledPackage("dsh-newplugin", "{\"dsh\":{\"bundle\":{\"patch\":{}}}}");
        string pnpm = Path.Combine(_root, "pnpm.cjs");
        File.WriteAllText(pnpm, "");
        var runOnce = (string np, string tc, string wd, string[] args, CancellationToken ct) =>
        {
            if (args[0] == "install")
            {
                WriteInstalledPackage("dsh-myrules", "{}");
            }

            return Task.FromResult((0, string.Empty));
        };
        IPluginManager repository = new PluginProfileRepository(ProfileDir, "node", pnpm, runOnce);

        string installed = await repository.InstallAsync("dsh-newplugin", CancellationToken.None);
        IReadOnlyList<PluginInfo> plugins = await repository.ListPluginsAsync(CancellationToken.None);

        await Assert.That(installed).IsEqualTo("dsh-newplugin");
        await Assert.That(plugins.Single(p => p.Name == "dsh-myrules").IsResolvable).IsTrue();
        await Assert.That(plugins.Single(p => p.Name == "dsh-newplugin").Enabled).IsTrue();
    }

    [Test]
    public async Task InstallAsync_RebuildFailsAfterResolvingBundles_StillSucceeds()
    {
        SeedProfile(
            dependencies: ["dsh-myrules", "dsh-newplugin"],
            bundles: ["dsh-myrules"]);
        WriteInstalledPackage("dsh-newplugin", "{\"dsh\":{\"bundle\":{\"patch\":{}}}}");
        string pnpm = Path.Combine(_root, "pnpm.cjs");
        File.WriteAllText(pnpm, "");
        var runOnce = (string np, string tc, string wd, string[] args, CancellationToken ct) =>
        {
            if (args[0] == "install")
            {
                WriteInstalledPackage("dsh-myrules", "{}");
                return Task.FromResult((1, "simulated rebuild failure"));
            }

            return Task.FromResult((0, string.Empty));
        };
        IPluginManager repository = new PluginProfileRepository(ProfileDir, "node", pnpm, runOnce);

        string installed = await repository.InstallAsync("dsh-newplugin", CancellationToken.None);
        IReadOnlyList<PluginInfo> plugins = await repository.ListPluginsAsync(CancellationToken.None);

        await Assert.That(installed).IsEqualTo("dsh-newplugin");
        await Assert.That(plugins.Single(p => p.Name == "dsh-myrules").IsResolvable).IsTrue();
        await Assert.That(plugins.Single(p => p.Name == "dsh-newplugin").Enabled).IsTrue();
    }

    [Test]
    public async Task InstallAsync_PackageWithoutBundlePatch_ThrowsWithoutEnabling()
    {
        SeedProfile(dependencies: ["dsh-library"], bundles: []);
        WriteInstalledPackage("dsh-library", "{\"version\":\"1.0.0\"}");
        string pnpm = Path.Combine(_root, "pnpm.cjs");
        File.WriteAllText(pnpm, "");
        IPluginManager repository = new PluginProfileRepository(
            ProfileDir, "node", pnpm, (_, _, _, _, _) => Task.FromResult((0, string.Empty)));

        InvalidOperationException? caught = null;
        try
        {
            await repository.InstallAsync("dsh-library", CancellationToken.None);
        }
        catch (InvalidOperationException exception)
        {
            caught = exception;
        }

        await Assert.That(caught).IsNotNull();
        await Assert.That(caught!.Message).IsEqualTo(
            "dsh-library 不是 DSH 插件（未声明 dsh.bundle.patch），已回滚。");
        IReadOnlyList<PluginInfo> plugins = await repository.ListPluginsAsync(CancellationToken.None);
        await Assert.That(plugins.Single(p => p.Name == "dsh-library").Enabled).IsFalse();
    }

    [Test]
    public async Task InstallAsync_SingleNewDependency_UsesActualNameInsteadOfArchiveName()
    {
        SeedProfile(dependencies: ["dsh-existing"], bundles: ["dsh-existing"]);
        WriteInstalledPackage("dsh-existing", "{}");
        WriteInstalledPackage("dsh-realplugin", "{\"dsh\":{\"bundle\":{\"patch\":{}}}}");
        string source = Path.Combine(_root, "renamed-archive.tgz");
        File.WriteAllText(source, "");
        string pnpm = Path.Combine(_root, "pnpm.cjs");
        File.WriteAllText(pnpm, "");
        var runOnce = (string np, string tc, string wd, string[] args, CancellationToken ct) =>
        {
            if (args[0] == "add")
            {
                SeedProfile(
                    dependencies: ["dsh-existing", "dsh-realplugin"],
                    bundles: ["dsh-existing"]);
            }

            return Task.FromResult((0, string.Empty));
        };
        IPluginManager repository = new PluginProfileRepository(ProfileDir, "node", pnpm, runOnce);

        string installed = await repository.InstallAsync(source, CancellationToken.None);
        IReadOnlyList<PluginInfo> plugins = await repository.ListPluginsAsync(CancellationToken.None);

        await Assert.That(installed).IsEqualTo("dsh-realplugin");
        await Assert.That(plugins.Single(p => p.Name == "dsh-realplugin").Enabled).IsTrue();
    }

    [Test]
    [Arguments("dsh-newplugin@latest", "dsh-newplugin", false)]
    [Arguments("@scope/dsh-plugin@2.0.0", "@scope/dsh-plugin", false)]
    [Arguments("dsh-local.tgz", "dsh-local", true)]
    public async Task InstallAsync_Reinstall_ResolvesNameFromSource(
        string sourceName,
        string pluginName,
        bool localSource)
    {
        SeedProfile(dependencies: [pluginName], bundles: []);
        WriteInstalledPackage(pluginName, "{\"dsh\":{\"bundle\":{\"patch\":{}}}}");
        string source = sourceName;
        if (localSource)
        {
            source = Path.Combine(_root, sourceName);
            File.WriteAllText(source, "");
        }

        string pnpm = Path.Combine(_root, "pnpm.cjs");
        File.WriteAllText(pnpm, "");
        IPluginManager repository = new PluginProfileRepository(
            ProfileDir, "node", pnpm, (_, _, _, _, _) => Task.FromResult((0, string.Empty)));

        string installed = await repository.InstallAsync(source, CancellationToken.None);
        IReadOnlyList<PluginInfo> plugins = await repository.ListPluginsAsync(CancellationToken.None);

        await Assert.That(installed).IsEqualTo(pluginName);
        await Assert.That(plugins.Single(p => p.Name == pluginName).Enabled).IsTrue();
    }

    [Test]
    public async Task InstallAsync_GitSourceWithoutGitOnPath_ThrowsBeforeCallingPnpm()
    {
        // git 协议源（GitHub 仓库地址）依赖系统 PATH 上的 git 拉取；缺失时必须前置失败，
        // 给出可操作的替代方案（tarball URL），且不得调用 pnpm。
        SeedProfile(dependencies: ["dsh-foo"], bundles: ["dsh-foo"]);
        string pnpm = Path.Combine(_root, "pnpm.cjs");
        File.WriteAllText(pnpm, "");
        int pnpmCalls = 0;
        IPluginManager repository = new PluginProfileRepository(
            ProfileDir, "node", pnpm,
            (_, _, _, _, _) => { pnpmCalls++; return Task.FromResult((0, string.Empty)); },
            gitAvailable: _ => Task.FromResult(false));

        InvalidOperationException? caught = null;
        try
        {
            await repository.InstallAsync("https://github.com/owner/dsh-plugin", CancellationToken.None);
        }
        catch (InvalidOperationException exception)
        {
            caught = exception;
        }

        await Assert.That(caught).IsNotNull();
        await Assert.That(caught!.Message).Contains("git");
        await Assert.That(caught.Message).Contains(".tar.gz");
        await Assert.That(pnpmCalls).IsEqualTo(0);
    }

    [Test]
    public async Task InstallAsync_GitShorthandWithoutGitOnPath_Throws()
    {
        // owner/repo 简写同样是 GitHub 源（pnpm 语义），需要 git。
        SeedProfile(dependencies: ["dsh-foo"], bundles: ["dsh-foo"]);
        string pnpm = Path.Combine(_root, "pnpm.cjs");
        File.WriteAllText(pnpm, "");
        IPluginManager repository = new PluginProfileRepository(
            ProfileDir, "node", pnpm,
            (_, _, _, _, _) => Task.FromResult((0, string.Empty)),
            gitAvailable: _ => Task.FromResult(false));

        await Assert.That(async () => await repository.InstallAsync("owner/dsh-plugin", CancellationToken.None))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task InstallAsync_TarballUrlWithoutGitOnPath_StillInstalls()
    {
        // tarball URL 由 pnpm 直接下载解压，不需要 git。
        SeedProfile(dependencies: ["dsh-existing"], bundles: ["dsh-existing"]);
        WriteInstalledPackage("dsh-existing", "{}");
        WriteBuiltGitPackage("dsh-realplugin");
        string pnpm = Path.Combine(_root, "pnpm.cjs");
        File.WriteAllText(pnpm, "");
        string[]? addArgs = null;
        var runOnce = (string np, string tc, string wd, string[] args, CancellationToken ct) =>
        {
            if (args[0] == "add")
            {
                addArgs = args;
                SeedProfile(dependencies: ["dsh-existing", "dsh-realplugin"], bundles: ["dsh-existing"]);
            }

            return Task.FromResult((0, string.Empty));
        };
        IPluginManager repository = new PluginProfileRepository(
            ProfileDir, "node", pnpm, runOnce, gitAvailable: _ => Task.FromResult(false));

        string installed = await repository.InstallAsync(
            "https://github.com/owner/dsh-realplugin/archive/refs/heads/main.tar.gz", CancellationToken.None);

        await Assert.That(installed).IsEqualTo("dsh-realplugin");
        await Assert.That(addArgs).IsNotNull();
        await Assert.That(addArgs!.Contains("--offline")).IsFalse(); // URL 源不挂 --offline
    }

    [Test]
    public async Task InstallAsync_GitSourceWithGitOnPath_CallsPnpmAdd()
    {
        SeedProfile(dependencies: ["dsh-existing"], bundles: ["dsh-existing"]);
        WriteInstalledPackage("dsh-existing", "{}");
        WriteBuiltGitPackage("dsh-realplugin");
        string pnpm = Path.Combine(_root, "pnpm.cjs");
        File.WriteAllText(pnpm, "");
        string[]? addArgs = null;
        var runOnce = (string np, string tc, string wd, string[] args, CancellationToken ct) =>
        {
            if (args[0] == "add")
            {
                addArgs = args;
                SeedProfile(dependencies: ["dsh-existing", "dsh-realplugin"], bundles: ["dsh-existing"]);
            }

            return Task.FromResult((0, string.Empty));
        };
        IPluginManager repository = new PluginProfileRepository(
            ProfileDir, "node", pnpm, runOnce, gitAvailable: _ => Task.FromResult(true));

        string installed = await repository.InstallAsync(
            "https://github.com/owner/dsh-realplugin", CancellationToken.None);

        await Assert.That(installed).IsEqualTo("dsh-realplugin");
        await Assert.That(addArgs).IsNotNull();
        await Assert.That(addArgs!).IsEquivalentTo(new[] { "add", "https://github.com/owner/dsh-realplugin" });
    }

    [Test]
    [Arguments("owner/dsh-plugin", "github:owner/dsh-plugin")]
    [Arguments("https://github.com/owner/dsh-plugin", "git+https://github.com/owner/dsh-plugin.git")]
    [Arguments("git@github.com:owner/dsh-plugin.git", "https://github.com/owner/dsh-plugin")]
    [Arguments(
        "https://github.com/owner/dsh-plugin/archive/refs/heads/main.tar.gz",
        "https://github.com/owner/dsh-plugin/archive/refs/heads/main.tar.gz")]
    public async Task InstallAsync_ReinstallRemoteSource_ResolvesNameFromManifestValue(
        string source,
        string manifestValue)
    {
        // 重装同规格远端源时 dependencies 差集为空，须按清单 dependencies 值反查键名——
        // repo 名 ≠ package 名（如 @scope/dsh-plugin），从 URL 末段猜名必错。
        Directory.CreateDirectory(Path.Combine(ProfileDir, "node_modules"));
        File.WriteAllText(
            Path.Combine(ProfileDir, "package.json"),
            "{\"dependencies\":{\"@scope/dsh-plugin\":\"" + manifestValue
                + "\"},\"dsh\":{\"profile\":{\"bundles\":[\"@scope/dsh-plugin\"]}}}");
        WriteBuiltGitPackage("@scope/dsh-plugin");
        string pnpm = Path.Combine(_root, "pnpm.cjs");
        File.WriteAllText(pnpm, "");
        IPluginManager repository = new PluginProfileRepository(
            ProfileDir, "node", pnpm,
            (_, _, _, _, _) => Task.FromResult((0, string.Empty)), // 重装：pnpm 不改清单，差集为空
            gitAvailable: _ => Task.FromResult(true));

        string installed = await repository.InstallAsync(source, CancellationToken.None);

        await Assert.That(installed).IsEqualTo("@scope/dsh-plugin");
    }

    [Test]
    public async Task InstallAsync_GitSourceWithoutBuildArtifactsOrPrepare_ThrowsWithGuidance()
    {
        // git 源码仓未提供构建：patch 声明指向 ./cordis.patch.yml 但文件不存在、
        // main 入口缺失、manifest 也没有 prepare/prepack —— 是插件作者的责任，
        // 宿主不代为猜测工具链；给出可操作指引后走事务回滚。
        SeedProfile(dependencies: ["dsh-existing"], bundles: ["dsh-existing"]);
        WriteInstalledPackage("dsh-existing", "{}");
        // 新插件：只写 package.json（声明 bundle.patch 但磁盘无该文件、无 main、无 scripts）。
        WriteInstalledPackage("dsh-srcplugin", "{\"main\":\"dist/index.js\",\"dsh\":{\"bundle\":{\"patch\":\"./cordis.patch.yml\"}}}");
        string pnpm = Path.Combine(_root, "pnpm.cjs");
        File.WriteAllText(pnpm, "");
        var runOnce = (string np, string tc, string wd, string[] args, CancellationToken ct) =>
        {
            if (args[0] == "add")
            {
                SeedProfile(dependencies: ["dsh-existing", "dsh-srcplugin"], bundles: ["dsh-existing"]);
            }

            return Task.FromResult((0, string.Empty));
        };
        IPluginManager repository = new PluginProfileRepository(
            ProfileDir, "node", pnpm, runOnce, gitAvailable: _ => Task.FromResult(true));

        InvalidOperationException? caught = null;
        try
        {
            await repository.InstallAsync("https://github.com/owner/dsh-srcplugin", CancellationToken.None);
        }
        catch (InvalidOperationException exception)
        {
            caught = exception;
        }

        await Assert.That(caught).IsNotNull();
        await Assert.That(caught!.Message).Contains("prepare");
        await Assert.That(caught.Message).Contains("dsh-srcplugin");
        await Assert.That(caught.Message).Contains("构建");
    }

    [Test]
    public async Task InstallAsync_GitSourceWithPrepareScriptButNoArtifacts_StillSucceeds()
    {
        // 仓库写了 prepare：构建责任在 pnpm/作者侧，宿主信任该声明，不因产物暂缺而误伤
        // （pnpm 对 git 依赖会自动执行 prepare；此处桩无法真跑脚本，以脚本存在即放行）。
        SeedProfile(dependencies: ["dsh-existing"], bundles: ["dsh-existing"]);
        WriteInstalledPackage("dsh-existing", "{}");
        WriteInstalledPackage(
            "dsh-prepareplugin",
            "{\"scripts\":{\"prepare\":\"tsc -b\"},\"dsh\":{\"bundle\":{\"patch\":\"./cordis.patch.yml\"}}}");
        // prepare 场景：patch 文件由脚本产出——桩里模拟 pnpm 已执行过 prepare 的效果。
        File.WriteAllText(
            Path.Combine(ProfileDir, "node_modules", "dsh-prepareplugin", "cordis.patch.yml"), "");
        string pnpm = Path.Combine(_root, "pnpm.cjs");
        File.WriteAllText(pnpm, "");
        var runOnce = (string np, string tc, string wd, string[] args, CancellationToken ct) =>
        {
            if (args[0] == "add")
            {
                SeedProfile(dependencies: ["dsh-existing", "dsh-prepareplugin"], bundles: ["dsh-existing"]);
            }

            return Task.FromResult((0, string.Empty));
        };
        IPluginManager repository = new PluginProfileRepository(
            ProfileDir, "node", pnpm, runOnce, gitAvailable: _ => Task.FromResult(true));

        string installed = await repository.InstallAsync(
            "https://github.com/owner/dsh-prepareplugin", CancellationToken.None);

        await Assert.That(installed).IsEqualTo("dsh-prepareplugin");
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

    private void WriteInstalledPackage(string name, string manifest)
    {
        string packageDir = Path.Combine(ProfileDir, "node_modules", name);
        Directory.CreateDirectory(packageDir);
        File.WriteAllText(Path.Combine(packageDir, "package.json"), manifest);
    }

    /// <summary>造「已构建」的 git 安装包：main 入口文件 + cordis.patch.yml 都在磁盘。</summary>
    private void WriteBuiltGitPackage(string name)
    {
        WriteInstalledPackage(
            name, "{\"main\":\"index.js\",\"dsh\":{\"bundle\":{\"patch\":\"./cordis.patch.yml\"}}}");
        string packageDir = Path.Combine(ProfileDir, "node_modules", name);
        File.WriteAllText(Path.Combine(packageDir, "index.js"), "");
        File.WriteAllText(Path.Combine(packageDir, "cordis.patch.yml"), "");
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
