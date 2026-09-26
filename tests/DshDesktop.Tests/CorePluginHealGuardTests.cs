namespace DshDesktop.Tests;

/// <summary>
/// 核心插件自愈的组合根接线守卫（2026-09-18 实机：dshmarket 被外部改出 bundles 后
/// 工作台不可用，而核心插件只读约定让 Desktop 无任何入口救回）。
/// 仅 ListPluginsAsync 读路径自愈不够——自动启动 DSH 时不经插件页，
/// 必须在 InitializeRuntimeAsync 阶段（任何 StartAsync 之前）显式自愈。
/// 启动编排已收口到 RuntimeBootstrapper（组合根拆分批 2a）：组合根本体只委托
/// RunAsync（自愈登记册为编排最后一步，栈装配之后——顺序真测试见 RuntimeBootstrapperRunTests）。
/// 测试项目不引 App 项目，故此处只锁「组合根确已委托」这一胶水语义（文本锚点，同 ShellTopNavTests 模式）。
/// 「核心 bundles 失败只告警不中断」语义内置于 Bootstrapper（SwallowOnFailure），由真测试直接覆盖。
/// </summary>
public sealed class CorePluginHealGuardTests
{
    [Test]
    public async Task InitializeRuntimeAsync_DelegatesBootstrapToRuntimeBootstrapper()
    {
        string source = await AppSourceAsync("Composition", "DshCompositionRoot.cs");

        // 锚定 InitializeRuntimeAsync 方法体：路由注册之后必须委托 Bootstrapper.RunAsync
        // （自愈登记册随编排执行；删掉委托 = 自愈与栈装配全部消失）。
        const string anchor = "public async Task InitializeRuntimeAsync(";
        int start = source.IndexOf(anchor, StringComparison.Ordinal);
        await Assert.That(start >= 0).IsTrue();
        int end = source.IndexOf("\n    private ", start + anchor.Length, StringComparison.Ordinal);
        string body = end < 0 ? source[start..] : source[start..end];

        int registerIndex = body.IndexOf("RegisterRoutes();", StringComparison.Ordinal);
        int runIndex = body.IndexOf(".RunAsync(", StringComparison.Ordinal);
        await Assert.That(registerIndex).IsGreaterThan(-1);
        await Assert.That(runIndex).IsGreaterThan(registerIndex);
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
