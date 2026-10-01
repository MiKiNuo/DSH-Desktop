using DshDesktop.Infrastructure.Runtime;

namespace DshDesktop.Tests;

/// <summary>
/// PluginActivationWarningProbe：从 DSH 启动期 stdout 里识别「有 entry 未激活」，并解析肇事插件包名。
/// 特征与样本均来自 2026-09-28 实机现场（dsh 0.1.7-rc.2 × dsh-myrules@0.1.1）：
/// 0.1.7-rc.2 的 dsh-typert-loader 新增硬校验（strict codec 必须有 create() 工厂），而 dsh-myrules
/// 是手写 typert manifest、codec 缺 create() ⇒ 校验抛错 ⇒ **整个 typert-loader 插件**激活失败 ⇒
/// app-boot 只打 WARN「1 entry did not activate」。
///
/// 这类故障**不抛启动失败**，所以宿主既不会走 IncompatiblePluginCrashProbe 的启动失败自愈链路，
/// 也不会把 Runtime 判成 Failed；但 dsh 侧经 cordis 的 fiber 归属回滚撤回了全部 strict 描述符，
/// api-gateway 随即拒绝客户端所有远程调用 ⇒ 工作台整页空白。故必须在「启动成功」路径上补这一环。
/// </summary>
public sealed class PluginActivationWarningProbeTests
{
    /// <summary>实机日志原文（data/logs/dsh-desktop-20260928.log 07:14:01.987 段，截去堆栈帧）。</summary>
    private const string RealTypertIncident = """
        dsh: warning: 1 entry did not activate
        typert-loader (@deepseek-ai/dsh-typert-loader): AggregateError: typert contributor(s) failed to register:
          - typert-loader: dsh-myrules invocation "dsh-myrules#myRules/readGlobalRules" parameter codec has no create() factory
        """;

    /// <summary>实机 typert 告警：肇事者是插件本身（dsh-myrules），不是报错的 typert-loader。</summary>
    [Test]
    public async Task TryParse_RealTypertIncident_ReturnsOffendingPlugin()
    {
        IReadOnlyList<string>? offenders = PluginActivationWarningProbe.TryParse(RealTypertIncident);

        await Assert.That(offenders).IsNotNull();
        await Assert.That(offenders!.Count).IsEqualTo(1);
        await Assert.That(offenders[0]).IsEqualTo("dsh-myrules");
    }

    /// <summary>不得把告警块首行的 typert-loader（报错方）当成肇事插件——会让用户禁用错的插件。</summary>
    [Test]
    public async Task TryParse_RealTypertIncident_DoesNotBlameTheReportingLoader()
    {
        IReadOnlyList<string>? offenders = PluginActivationWarningProbe.TryParse(RealTypertIncident);

        await Assert.That(offenders!.Contains("@deepseek-ai/dsh-typert-loader", StringComparer.Ordinal)).IsFalse();
    }

    /// <summary>正常启动输出（含就绪行）不命中 ⇒ null，宿主不得据此误报。</summary>
    [Test]
    public async Task TryParse_NormalStartupOutput_ReturnsNull()
    {
        const string output = """
            [harness-node] runtime node=v24.19.0 platform=win32 arch=x64
            [harness-node] DSH entry loaded
            Plugin.Install.Installed dshmarket
            dsh web: http://127.0.0.1:54036/?token=***
            """;

        await Assert.That(PluginActivationWarningProbe.TryParse(output)).IsNull();
    }

    /// <summary>同栈的 loader include 行不含 invocation 子句，不得凭空产出肇事者。</summary>
    [Test]
    public async Task TryParse_LoaderIncludeLine_DoesNotHit()
    {
        const string output = """
            [harness-node] DSH entry failed: Error: dsh: plugin tree failed to load: failed to apply loader entry include (cordis:include)
            """;

        await Assert.That(PluginActivationWarningProbe.TryParse(output)).IsNull();
    }

    /// <summary>命中「未激活」但提取不到插件名 ⇒ 空集合（非 null）：语义是「有降级、但无法定位」。</summary>
    [Test]
    public async Task TryParse_AnchorWithoutIdentifiablePlugin_ReturnsEmptyList()
    {
        const string output = """
            dsh: warning: 2 entries did not activate
            dsh-market (dshmarket): Error: cannot find module '@deepseek-ai/dsh-settings'
            """;

        IReadOnlyList<string>? offenders = PluginActivationWarningProbe.TryParse(output);

        await Assert.That(offenders).IsNotNull();
        await Assert.That(offenders!.Count).IsEqualTo(0);
    }

    /// <summary>同一插件的多条 invocation（read/write）只报一次，避免 UI 文案里重复列同一个插件。</summary>
    [Test]
    public async Task TryParse_SamePluginTwice_Deduplicates()
    {
        const string output = """
            dsh: warning: 1 entry did not activate
              - typert-loader: dsh-myrules invocation "dsh-myrules#myRules/readGlobalRules" parameter codec has no create() factory
              - typert-loader: dsh-myrules invocation "dsh-myrules#myRules/writeGlobalRules" parameter codec has no create() factory
            """;

        IReadOnlyList<string>? offenders = PluginActivationWarningProbe.TryParse(output);

        await Assert.That(offenders).IsNotNull();
        await Assert.That(offenders!.Count).IsEqualTo(1);
        await Assert.That(offenders[0]).IsEqualTo("dsh-myrules");
    }

    /// <summary>多插件时保持出现顺序，便于宿主原样展示。</summary>
    [Test]
    public async Task TryParse_TwoPlugins_KeepsOccurrenceOrder()
    {
        const string output = """
            dsh: warning: 2 entries did not activate
              - typert-loader: dsh-myrules invocation "dsh-myrules#myRules/readGlobalRules" parameter codec has no create() factory
              - typert-loader: dsh-other invocation "dsh-other#things/read" parameter codec has no create() factory
            """;

        IReadOnlyList<string>? offenders = PluginActivationWarningProbe.TryParse(output);

        await Assert.That(offenders).IsNotNull();
        await Assert.That(offenders!.Count).IsEqualTo(2);
        await Assert.That(offenders[0]).IsEqualTo("dsh-myrules");
        await Assert.That(offenders[1]).IsEqualTo("dsh-other");
    }

    /// <summary>scoped 包名（@scope/pkg）不得被 `#` 截断逻辑切坏。</summary>
    [Test]
    public async Task TryParse_ScopedPlugin_ReturnsScopedName()
    {
        const string output = """
            dsh: warning: 1 entry did not activate
              - typert-loader: @scope/dsh-thing invocation "@scope/dsh-thing#api/read" parameter codec has no create() factory
            """;

        IReadOnlyList<string>? offenders = PluginActivationWarningProbe.TryParse(output);

        await Assert.That(offenders).IsNotNull();
        await Assert.That(offenders!.Count).IsEqualTo(1);
        await Assert.That(offenders[0]).IsEqualTo("@scope/dsh-thing");
    }

    /// <summary>空输入 ⇒ null（无信息，不得当作降级）。</summary>
    [Test]
    public async Task TryParse_EmptyInput_ReturnsNull()
    {
        await Assert.That(PluginActivationWarningProbe.TryParse(string.Empty)).IsNull();
        await Assert.That(PluginActivationWarningProbe.TryParse("   ")).IsNull();
    }
}
