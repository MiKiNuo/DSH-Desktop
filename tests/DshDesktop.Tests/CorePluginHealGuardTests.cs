namespace DshDesktop.Tests;

/// <summary>
/// 核心插件自愈的组合根接线守卫（2026-09-18 实机：dshmarket 被外部改出 bundles 后
/// 工作台不可用，而核心插件只读约定让 Desktop 无任何入口救回）。
/// 仅 ListPluginsAsync 读路径自愈不够——自动启动 DSH 时不经插件页，
/// 必须在 InitializeRuntimeAsync 阶段（任何 StartAsync 之前）显式自愈。
/// 自检已收口到 RuntimeBootstrapper（组合根拆分批 1）：组合根在 WirePluginStack() 与
/// CreateRuntimeRepository() 之后统一调用 RunBootHealsAsync。测试项目不引 App 项目，
/// 故用源码文本锚点守卫（同 ShellTopNavTests 模式）。
/// 「核心 bundles 失败只告警不中断」语义内置于 Bootstrapper.RunBootHealsAsync（SwallowOnFailure），
/// 由 RuntimeBootstrapperTests 直接覆盖。
/// </summary>
public sealed class CorePluginHealGuardTests
{
    [Test]
    public async Task InitializeRuntimeAsync_HealsCoreBundlesAfterRepositoryConstruction()
    {
        string source = await AppSourceAsync("Composition", "DshCompositionRoot.cs");

        // 锚定 InitializeRuntimeAsync 方法体：先 WirePluginStack / CreateRuntimeRepository，后 RunBootHealsAsync。
        const string anchor = "public async Task InitializeRuntimeAsync(";
        int start = source.IndexOf(anchor, StringComparison.Ordinal);
        await Assert.That(start >= 0).IsTrue();
        int end = source.IndexOf("\n    private ", start + anchor.Length, StringComparison.Ordinal);
        string body = end < 0 ? source[start..] : source[start..end];

        int wireIndex = body.IndexOf("WirePluginStack();", StringComparison.Ordinal);
        int repositoryIndex = body.IndexOf("CreateRuntimeRepository();", StringComparison.Ordinal);
        int bootHealIndex = body.IndexOf("RunBootHealsAsync", StringComparison.Ordinal);
        await Assert.That(wireIndex).IsGreaterThan(-1);
        await Assert.That(repositoryIndex).IsGreaterThan(wireIndex);
        await Assert.That(bootHealIndex).IsGreaterThan(repositoryIndex);
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
