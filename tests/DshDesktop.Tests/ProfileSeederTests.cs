using DshDesktop.Infrastructure.Runtime;

namespace DshDesktop.Tests;

/// <summary>
/// Profile 播种测试（Q11-B + ADR-0003 修订）：
/// 种子复制必须得到「跨盘可迁移」且「依赖树可用」的自包含副本。
/// pnpm 的 node_modules 是符号链接图（含旧盘绝对路径的 .pnpm-workspace-state-v1.json /
/// .modules.yaml），.generations 是 package.json 里 link: 约定的目标——二者复制到新盘
/// 会触发 ERR_PNPM_UNEXPECTED_VIRTUAL_STORE 并使 DSH 首启崩溃，故必须排除。
/// 但排除后必须真正重建依赖树：2026-09-13 现场证明「由 DSH 首启自行重建」从未发生，
/// 只复制清单不装依赖会让 DSH resolveBundleDir 抛 "cannot resolve profile bundle"
/// → Runtime ExitCode=1，且旧实现「目录存在即 return」使该状态永久卡死。
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

    [Test]
    public async Task SeedIfNeededAsync_ExcludesPnpmDerivedDirectories()
    {
        // 源 profile 含 pnpm 派生物：node_modules（符号链接图）与 .generations（link: 目标）。
        Directory.CreateDirectory(Path.Combine(SourceProfile, "node_modules", ".pnpm"));
        Directory.CreateDirectory(Path.Combine(SourceProfile, ".generations", "live"));
        File.WriteAllText(Path.Combine(SourceProfile, "package.json"), "{}");
        File.WriteAllText(
            Path.Combine(SourceProfile, "node_modules", ".pnpm-workspace-state-v1.json"),
            "{\"projects\":{\"C:\\\\old\":{}}}");

        // pnpm 路径为 null = 未配置 vendored pnpm，本用例只校验复制排除清单，不触发安装。
        await ProfileSeeder.SeedIfNeededAsync(TargetHome, SourceHome, "node", null, CancellationToken.None);

        // 普通文件应被复制（种子仍有意义）。
        await Assert.That(File.Exists(Path.Combine(TargetProfile, "package.json"))).IsTrue();
        // pnpm 派生物不得被复制，否则跨盘后 pnpm 状态冲突。
        await Assert.That(Directory.Exists(Path.Combine(TargetProfile, "node_modules"))).IsFalse();
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
    /// 依赖树已就绪时不得重复安装（保持一次性语义，避免每次启动都跑 pnpm）。
    /// </summary>
    [Test]
    public async Task SeedIfNeededAsync_DependenciesPresent_DoesNotInstall()
    {
        Directory.CreateDirectory(SourceProfile);
        File.WriteAllText(Path.Combine(SourceProfile, "package.json"), BundlesManifest);

        Directory.CreateDirectory(Path.Combine(TargetProfile, "node_modules"));
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
}
