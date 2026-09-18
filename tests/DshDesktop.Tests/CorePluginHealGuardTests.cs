namespace DshDesktop.Tests;

/// <summary>
/// 核心插件自愈的组合根接线守卫（2026-09-18 实机：dshmarket 被外部改出 bundles 后
/// 工作台不可用，而核心插件只读约定让 Desktop 无任何入口救回）。
/// 仅 ListPluginsAsync 读路径自愈不够——自动启动 DSH 时不经插件页，
/// 必须在 InitializeRuntimeAsync 阶段（任何 StartAsync 之前）显式自愈。
/// 测试项目不引 App 项目，故用源码文本锚点守卫（同 ShellTopNavTests 模式）。
/// </summary>
public sealed class CorePluginHealGuardTests
{
    [Test]
    public async Task InitializeRuntimeAsync_HealsCoreBundlesAfterRepositoryConstruction()
    {
        string source = await AppSourceAsync("Composition", "DshCompositionRoot.cs");

        int repositoryIndex = source.IndexOf(
            "_pluginRepository = new PluginProfileRepository", StringComparison.Ordinal);
        int healIndex = source.IndexOf("HealCoreBundlesAsync", StringComparison.Ordinal);
        await Assert.That(repositoryIndex).IsGreaterThan(-1);
        await Assert.That(healIndex).IsGreaterThan(repositoryIndex);

        // 自愈失败不得中断启动引导（同 PnpmProvisioner 约定：装不了/修不好 ≠ 应用起不来）。
        int catchIndex = source.IndexOf("catch", healIndex, StringComparison.Ordinal);
        await Assert.That(catchIndex).IsGreaterThan(healIndex);
    }

    private static async Task<string> AppSourceAsync(params string[] relativeSegments)
    {
        var root = XamlScan.FindRepositoryRoot();
        await Assert.That(root).IsNotNull();

        var path = Path.Combine(new[] { root!, "src", "DshDesktop.App" }.Concat(relativeSegments).ToArray());
        await Assert.That(File.Exists(path)).IsTrue();
        return await File.ReadAllTextAsync(path);
    }
}
