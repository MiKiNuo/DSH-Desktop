using DshDesktop.Infrastructure.Runtime;

namespace DshDesktop.Tests;

/// <summary>
/// Profile 播种测试（Q11-B + ADR-0003 修订）：
/// 种子复制必须得到"跨盘可迁移"的自包含副本。
/// pnpm 的 node_modules 是符号链接图（含旧盘绝对路径的 .pnpm-workspace-state-v1.json /
/// .modules.yaml），.generations 是 package.json 里 link: 约定的目标——二者复制到新盘
/// 会触发 ERR_PNPM_UNEXPECTED_VIRTUAL_STORE 并使 DSH 首启崩溃，故必须排除、交由 DSH 重建。
/// </summary>
public sealed class ProfileSeederTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dsh-seed-" + Guid.NewGuid().ToString("N"));

    private string SourceHome => Path.Combine(_root, "source-harness");

    private string TargetHome => Path.Combine(_root, "target-dsh-home");

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
        string sourceProfile = Path.Combine(SourceHome, "profiles", "web");
        Directory.CreateDirectory(Path.Combine(sourceProfile, "node_modules", ".pnpm"));
        Directory.CreateDirectory(Path.Combine(sourceProfile, ".generations", "live"));
        File.WriteAllText(Path.Combine(sourceProfile, "package.json"), "{}");
        File.WriteAllText(
            Path.Combine(sourceProfile, "node_modules", ".pnpm-workspace-state-v1.json"),
            "{\"projects\":{\"C:\\\\old\":{}}}");

        await ProfileSeeder.SeedIfNeededAsync(TargetHome, SourceHome, CancellationToken.None);

        string targetProfile = Path.Combine(TargetHome, "profiles", "web");
        // 普通文件应被复制（种子仍有意义）。
        await Assert.That(File.Exists(Path.Combine(targetProfile, "package.json"))).IsTrue();
        // pnpm 派生物不得被复制，否则跨盘后 pnpm 状态冲突。
        await Assert.That(Directory.Exists(Path.Combine(targetProfile, "node_modules"))).IsFalse();
        await Assert.That(Directory.Exists(Path.Combine(targetProfile, ".generations"))).IsFalse();
    }

    [Test]
    public async Task SeedIfNeededAsync_TargetProfileExists_Skips()
    {
        string sourceProfile = Path.Combine(SourceHome, "profiles", "web");
        Directory.CreateDirectory(sourceProfile);
        File.WriteAllText(Path.Combine(sourceProfile, "package.json"), "{}");

        // 目标已存在：不应再播种（一次性语义）。
        Directory.CreateDirectory(Path.Combine(TargetHome, "profiles", "web"));

        await ProfileSeeder.SeedIfNeededAsync(TargetHome, SourceHome, CancellationToken.None);

        await Assert.That(
            File.Exists(Path.Combine(TargetHome, "profiles", "web", "package.json"))).IsFalse();
    }

    [Test]
    public async Task SeedIfNeededAsync_NullSeedSource_Skips()
    {
        await ProfileSeeder.SeedIfNeededAsync(TargetHome, null, CancellationToken.None);

        await Assert.That(Directory.Exists(Path.Combine(TargetHome, "profiles", "web"))).IsFalse();
    }
}
