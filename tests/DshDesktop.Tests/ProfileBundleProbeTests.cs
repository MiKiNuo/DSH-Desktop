using DshDesktop.Infrastructure.Plugins;

namespace DshDesktop.Tests;

/// <summary>
/// ProfileBundleProbe 测试（§18 派生）：直接测试 in-box bundle 豁免语义。
/// in-box bundle（@deepseek-ai/dsh-base / @deepseek-ai/dsh-web-app）由 DSH 安装体提供，
/// profile 的 node_modules 中永不存在，必须被 UnresolvedBundles 豁免，否则健康 Profile
/// 的安装也会恒错（2026-09-14 实机故障回归）。
/// </summary>
public sealed class ProfileBundleProbeTests : IDisposable
{
    private readonly string _profileDir =
        Path.Combine(Path.GetTempPath(), "dsh-probe-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_profileDir))
        {
            Directory.Delete(_profileDir, recursive: true);
        }
    }

    private void SeedManifest(string[] bundles, params string[] resolvedOnDisk)
    {
        Directory.CreateDirectory(_profileDir);
        foreach (string bundle in resolvedOnDisk)
        {
            Directory.CreateDirectory(Path.Combine(_profileDir, "node_modules", bundle));
            File.WriteAllText(
                Path.Combine(_profileDir, "node_modules", bundle, "package.json"), "{}");
        }

        string bundleList = string.Join(",", bundles.Select(b => $"\"{b}\""));
        File.WriteAllText(
            Path.Combine(_profileDir, "package.json"),
            $"{{\"dsh\":{{\"profile\":{{\"bundles\":[{bundleList}]}}}}}}");
    }

    [Test]
    public async Task UnresolvedBundles_OnlyInBoxBundles_ReturnsEmpty()
    {
        // 直接回归：实机故障中两个 in-box bundle 在 node_modules 中不存在，
        // 旧实现恒判不可解析 → 插件安装必然失败。
        SeedManifest(
            bundles: ["@deepseek-ai/dsh-base", "@deepseek-ai/dsh-web-app"]);

        string[] unresolved = ProfileBundleProbe.UnresolvedBundles(_profileDir);

        await Assert.That(unresolved).IsEmpty();
    }

    [Test]
    public async Task UnresolvedBundles_DshMarketMissing_StillUnresolved()
    {
        // 防止豁免过宽：dshmarket 在 profile node_modules 中真实存在且需参与校验，
        // 缺失时必须仍判不可解析。
        SeedManifest(bundles: ["dshmarket"]);

        string[] unresolved = ProfileBundleProbe.UnresolvedBundles(_profileDir);

        await Assert.That(unresolved).IsEquivalentTo(["dshmarket"]);
    }

    [Test]
    public async Task UnresolvedBundles_Mixed_ReportsOnlyMissingRealBundle()
    {
        // in-box（豁免）+ 真实可解析 + 真实缺失 → 只报那一个缺失项。
        SeedManifest(
            bundles: ["@deepseek-ai/dsh-base", "dsh-context", "@deepseek-ai/dsh-web-app", "dsh-ghost"],
            resolvedOnDisk: ["dsh-context"]);

        string[] unresolved = ProfileBundleProbe.UnresolvedBundles(_profileDir);

        await Assert.That(unresolved).IsEquivalentTo(["dsh-ghost"]);
    }

    [Test]
    public async Task DeclaredBundles_ReturnsFullListIncludingInBox()
    {
        // DeclaredBundles 不受影响，仍返回完整声明（含 in-box）。
        SeedManifest(
            bundles: ["@deepseek-ai/dsh-base", "@deepseek-ai/dsh-web-app", "dsh-context"],
            resolvedOnDisk: ["dsh-context"]);

        string[] declared = ProfileBundleProbe.DeclaredBundles(_profileDir);

        await Assert.That(declared)
            .IsEquivalentTo(["@deepseek-ai/dsh-base", "@deepseek-ai/dsh-web-app", "dsh-context"]);
    }
}
