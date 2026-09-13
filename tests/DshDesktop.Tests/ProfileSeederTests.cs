using DshDesktop.Infrastructure.Runtime;

namespace DshDesktop.Tests;

/// <summary>
/// Profile 播种测试（Q11-B + ADR-0003 修订）：
/// 种子复制必须得到「跨盘可迁移」且「依赖树可用」的自包含副本。
/// node_modules 必须随复制一并保留——其中的 dsh-context / dshmarket 等是指向
/// profiles/.generations/live/&lt;genId&gt;/ 的符号链接，robocopy 跟随联接点展开为真实目录，
/// 这是依赖树唯一可行的物化路径；排除它只会让 pnpm install 产出 link: 悬空符号链接
/// （overrides 目标同时被排除），DSH resolveBundleDir 抛 "cannot resolve profile bundle"
/// → Runtime ExitCode=1（2026-09-14 现场）。
/// .generations 本身无需复制，其内容已展开进 node_modules。
/// 依赖判据与 DSH 同源：声明的 bundle 能否在 node_modules 下解析出来——
/// 旧实现「node_modules 目录存在即 return」把空壳现场误判为就绪，使该状态永久卡死。
/// </summary>
public sealed class ProfileSeederTests : IDisposable
{
    private const string BundlesManifest = """
        {
          "dependencies": { "dshmarket": "1.45.1" },
          "dsh": { "profile": { "bundles": ["@deepseek-ai/dsh-base", "dshmarket"] } }
        }
        """;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "dsh-seed-" + Guid.NewGuid().ToString("N"));

    private string SourceHome => Path.Combine(_root, "source-harness");

    private string TargetHome => Path.Combine(_root, "target-dsh-home");

    private string SourceProfile => Path.Combine(SourceHome, "profiles", "web");

    private string TargetProfile => Path.Combine(TargetHome, "profiles", "web");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>
    /// 复制必须保留 node_modules：robocopy 跟随符号链接把依赖展开为真实目录，
    /// 这是依赖树唯一可行的物化路径。.generations 是 link: 目标，内容已展开进
    /// node_modules，无需重复复制。
    /// </summary>
    [Test]
    public async Task SeedIfNeededAsync_KeepsNodeModulesAndExcludesGenerations()
    {
        // 源 profile 同时含依赖树与 link: 目标目录。
        Directory.CreateDirectory(Path.Combine(SourceProfile, "node_modules", "dshmarket"));
        File.WriteAllText(
            Path.Combine(SourceProfile, "node_modules", "dshmarket", "package.json"),
            "{}");
        Directory.CreateDirectory(Path.Combine(SourceProfile, ".generations", "live"));
        File.WriteAllText(Path.Combine(SourceProfile, "package.json"), "{}");

        // pnpm 路径为 null = 未配置 vendored pnpm；清单未声明 bundles，故不触发安装。
        await ProfileSeeder.SeedIfNeededAsync(TargetHome, SourceHome, "node", null, CancellationToken.None);

        // 普通文件应被复制（种子仍有意义）。
        await Assert.That(File.Exists(Path.Combine(TargetProfile, "package.json"))).IsTrue();
        // 依赖树必须随复制落地：排除它会让重装只能产出 link: 悬空符号链接。
        await Assert.That(
                File.Exists(Path.Combine(TargetProfile, "node_modules", "dshmarket", "package.json")))
            .IsTrue();
        // .generations 是 link: 目标，内容已展开进 node_modules，不重复复制。
        await Assert.That(Directory.Exists(Path.Combine(TargetProfile, ".generations"))).IsFalse();
    }

    [Test]
    public async Task SeedIfNeededAsync_TargetProfileExists_SkipsCopy()
    {
        Directory.CreateDirectory(SourceProfile);
        File.WriteAllText(Path.Combine(SourceProfile, "package.json"), "{}");

        // 目标已存在：不应再复制（一次性语义）。
        Directory.CreateDirectory(TargetProfile);

        await ProfileSeeder.SeedIfNeededAsync(TargetHome, SourceHome, "node", null, CancellationToken.None);

        await Assert.That(File.Exists(Path.Combine(TargetProfile, "package.json"))).IsFalse();
    }

    [Test]
    public async Task SeedIfNeededAsync_NullSeedSource_Skips()
    {
        await ProfileSeeder.SeedIfNeededAsync(TargetHome, null, "node", null, CancellationToken.None);

        await Assert.That(Directory.Exists(TargetProfile)).IsFalse();
    }

    /// <summary>
    /// 回归（2026-09-13）：种子排除 node_modules 后，依赖必须被真正重建。
    /// 否则 profile 清单声明了 bundles 但依赖树缺失，DSH 启动时
    /// resolveBundleDir 抛 "cannot resolve profile bundle" → Runtime ExitCode=1。
    /// </summary>
    [Test]
    public async Task SeedIfNeededAsync_SeedDeclaresBundles_InstallsDependencies()
    {
        Directory.CreateDirectory(SourceProfile);
        File.WriteAllText(Path.Combine(SourceProfile, "package.json"), BundlesManifest);

        int installCalls = 0;
        await ProfileSeeder.SeedIfNeededAsync(
            TargetHome,
            SourceHome,
            (_, _) =>
            {
                installCalls++;
                // 模拟 pnpm install 的产物：依赖树落盘。
                Directory.CreateDirectory(Path.Combine(TargetProfile, "node_modules"));
                return Task.FromResult((0, string.Empty));
            },
            CancellationToken.None);

        await Assert.That(File.Exists(Path.Combine(TargetProfile, "package.json"))).IsTrue();
        await Assert.That(installCalls).IsEqualTo(1);
        await Assert.That(Directory.Exists(Path.Combine(TargetProfile, "node_modules"))).IsTrue();
    }

    /// <summary>
    /// 半成功状态必须可修复：目标 profile 已存在但依赖树缺失时，
    /// 不能因「目录已存在」就跳过（旧实现导致永久卡死）。
    /// </summary>
    [Test]
    public async Task SeedIfNeededAsync_TargetProfileExistsButDependenciesMissing_Installs()
    {
        Directory.CreateDirectory(SourceProfile);
        File.WriteAllText(Path.Combine(SourceProfile, "package.json"), BundlesManifest);

        // 半成功现场：复制已完成（清单含 bundles 声明）、依赖树缺失。
        Directory.CreateDirectory(TargetProfile);
        File.WriteAllText(Path.Combine(TargetProfile, "package.json"), BundlesManifest);

        int installCalls = 0;
        await ProfileSeeder.SeedIfNeededAsync(
            TargetHome,
            SourceHome,
            (_, _) =>
            {
                installCalls++;
                Directory.CreateDirectory(Path.Combine(TargetProfile, "node_modules"));
                return Task.FromResult((0, string.Empty));
            },
            CancellationToken.None);

        await Assert.That(installCalls).IsEqualTo(1);
        await Assert.That(Directory.Exists(Path.Combine(TargetProfile, "node_modules"))).IsTrue();
    }

    /// <summary>
    /// 依赖树已就绪（声明的 bundle 均可解析）时不得重复安装（保持一次性语义，
    /// 避免每次启动都跑 pnpm）。就绪判据与 DSH 同源，而非「node_modules 目录是否存在」。
    /// </summary>
    [Test]
    public async Task SeedIfNeededAsync_DependenciesPresent_DoesNotInstall()
    {
        Directory.CreateDirectory(SourceProfile);
        File.WriteAllText(Path.Combine(SourceProfile, "package.json"), BundlesManifest);

        // 依赖树已就绪 = 声明的 bundle 能解析出 package.json（仅目录存在不算就绪）。
        Directory.CreateDirectory(Path.Combine(TargetProfile, "node_modules", "@deepseek-ai", "dsh-base"));
        Directory.CreateDirectory(Path.Combine(TargetProfile, "node_modules", "dshmarket"));
        File.WriteAllText(
            Path.Combine(TargetProfile, "node_modules", "@deepseek-ai", "dsh-base", "package.json"),
            "{}");
        File.WriteAllText(
            Path.Combine(TargetProfile, "node_modules", "dshmarket", "package.json"),
            "{}");
        File.WriteAllText(Path.Combine(TargetProfile, "package.json"), BundlesManifest);

        int installCalls = 0;
        await ProfileSeeder.SeedIfNeededAsync(
            TargetHome,
            SourceHome,
            (_, _) =>
            {
                installCalls++;
                return Task.FromResult((0, string.Empty));
            },
            CancellationToken.None);

        await Assert.That(installCalls).IsEqualTo(0);
    }

    /// <summary>
    /// 未声明 bundles 的 profile 不需要依赖树，不得调用 pnpm（避免无谓安装）。
    /// </summary>
    [Test]
    public async Task SeedIfNeededAsync_NoBundlesDeclared_DoesNotInstall()
    {
        Directory.CreateDirectory(SourceProfile);
        File.WriteAllText(Path.Combine(SourceProfile, "package.json"), """{ "name": "empty" }""");

        int installCalls = 0;
        await ProfileSeeder.SeedIfNeededAsync(
            TargetHome,
            SourceHome,
            (_, _) =>
            {
                installCalls++;
                return Task.FromResult((0, string.Empty));
            },
            CancellationToken.None);

        await Assert.That(installCalls).IsEqualTo(0);
    }

    /// <summary>
    /// 未配置 vendored pnpm 时无法重建依赖树，必须显式失败——静默跳过等于
    /// 把「有清单无依赖」的不可启动状态无痕延续（与 PluginProfileRepository
    /// 「找不到 vendored pnpm」的处置一致）。本用例同时覆盖公开 5 参重载的真实装配链。
    /// </summary>
    [Test]
    public async Task SeedIfNeededAsync_PnpmUnavailable_Throws()
    {
        Directory.CreateDirectory(SourceProfile);
        File.WriteAllText(Path.Combine(SourceProfile, "package.json"), BundlesManifest);

        await Assert.That(async () => await ProfileSeeder.SeedIfNeededAsync(
                TargetHome, SourceHome, "node", null, CancellationToken.None))
            .Throws<InvalidOperationException>();
    }

    /// <summary>
    /// 安装失败必须显式抛出：静默失败会让「有清单无依赖」的不可启动状态无痕延续
    /// （2026-09-13 现场正是这样被误判为「Runtime 自己起不来」）。
    /// </summary>
    [Test]
    public async Task SeedIfNeededAsync_InstallFails_Throws()
    {
        Directory.CreateDirectory(SourceProfile);
        File.WriteAllText(Path.Combine(SourceProfile, "package.json"), BundlesManifest);

        await Assert.That(async () => await ProfileSeeder.SeedIfNeededAsync(
                TargetHome,
                SourceHome,
                (_, _) => Task.FromResult((1, "ERR_PNPM_NO_MATCHING_VERSION")),
                CancellationToken.None))
            .Throws<InvalidOperationException>();
    }

    /// <summary>
    /// 回归（2026-09-14 实机）：node_modules 目录存在、但声明的 bundle 一个都解析不出来
    /// （空壳——真实依赖被埋在 node_modules/node_modules/，或安装中断只留了目录）时，
    /// 必须重装。旧判据「node_modules 存在即 return」让该状态永久无法自愈，
    /// DSH 仍抛 "cannot resolve profile bundle" → Runtime ExitCode=1。
    /// </summary>
    [Test]
    public async Task SeedIfNeededAsync_EmptyShellNodeModules_Installs()
    {
        Directory.CreateDirectory(SourceProfile);
        File.WriteAllText(Path.Combine(SourceProfile, "package.json"), BundlesManifest);

        // 空壳现场：node_modules 存在，但 @deepseek-ai/dsh-base 与 dshmarket 都不在其下。
        Directory.CreateDirectory(Path.Combine(TargetProfile, "node_modules", "node_modules", ".pnpm"));
        File.WriteAllText(Path.Combine(TargetProfile, "package.json"), BundlesManifest);

        int installCalls = 0;
        await ProfileSeeder.SeedIfNeededAsync(
            TargetHome,
            SourceHome,
            (_, _) =>
            {
                installCalls++;
                return Task.FromResult((0, string.Empty));
            },
            CancellationToken.None);

        await Assert.That(installCalls).IsEqualTo(1);
    }

    /// <summary>
    /// 部分 bundle 可解析时不得重装：pnpm install 会用 link: 悬空链接覆盖已有的
    /// 真实依赖目录（overrides 的 .generations 目标在排除后不存在），把可启动现场改成
    /// 不可启动现场。只有「一个都解析不出来」才认定为空壳。
    /// </summary>
    [Test]
    public async Task SeedIfNeededAsync_PartialBundlesResolved_DoesNotInstall()
    {
        Directory.CreateDirectory(SourceProfile);
        File.WriteAllText(Path.Combine(SourceProfile, "package.json"), BundlesManifest);

        // 已 materialize 的现场：node_modules 下能解析出 dshmarket（含 package.json）。
        Directory.CreateDirectory(Path.Combine(TargetProfile, "node_modules", "dshmarket"));
        File.WriteAllText(
            Path.Combine(TargetProfile, "node_modules", "dshmarket", "package.json"),
            "{}");
        File.WriteAllText(Path.Combine(TargetProfile, "package.json"), BundlesManifest);

        int installCalls = 0;
        await ProfileSeeder.SeedIfNeededAsync(
            TargetHome,
            SourceHome,
            (_, _) =>
            {
                installCalls++;
                return Task.FromResult((0, string.Empty));
            },
            CancellationToken.None);

        await Assert.That(installCalls).IsEqualTo(0);
    }
}
