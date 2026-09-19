using DshDesktop.Infrastructure.Runtime;

namespace DshDesktop.Tests;

/// <summary>
/// IncompatiblePluginCrashProbe：从「DSH 进程在就绪前退出」失败消息解析肇事插件包名，
/// 供组合根触发事务化升级自愈。特征与样本均来自 2026-09-19 v0.1.4 实机现场
/// （旧版 dshmarket 静态 import 了 Runtime ≥0.1.2-alpha.1 已删除的 installSettingsSection）。
/// </summary>
public sealed class IncompatiblePluginCrashProbeTests
{
    /// <summary>真实 stderr 尾部样本（v0.1.4 实机日志原文）：loader entry 括号包名命中。</summary>
    [Test]
    public async Task TryParseOffender_RealIncidentSample_ReturnsDshmarket()
    {
        const string message = """
            DSH 进程在就绪前退出（退出码 1）。stderr 末尾：[harness-node] DSH entry failed: Error: dsh: plugin tree failed to load: failed to apply loader entry include (cordis:include): failed to import loader entry dsh-market (dshmarket): The requested module '@deepseek-ai/dsh-settings' does not provide an export named 'installSettingsSection'
            file:///D:/Program%20Files/DSH-Desktop/data/dsh-home/profiles/web/node_modules/dshmarket/lib/settings.js:35
            import { installSettingsSection, settingsNamespace } from '@deepseek-ai/dsh-settings';
            SyntaxError: The requested module '@deepseek-ai/dsh-settings' does not provide an export named 'installSettingsSection'
                at #asyncInstantiate (node:internal/modules/esm/module_job:327:21)
                at async Entry._init (file:///D:/Program%20Files/DSH-Desktop/data/runtime/dsh/0.1.5-rc.2/node_modules/@deepseek-ai/cordis-plugin-loader/lib/index.js:522:39)
            """;

        await Assert.That(IncompatiblePluginCrashProbe.TryParseOffender(message)).IsEqualTo("dshmarket");
    }

    /// <summary>scoped 包：loader entry 括号与 node_modules 两段式路径都要能解析。</summary>
    [Test]
    public async Task TryParseOffender_ScopedPackageViaLoaderEntry_ReturnsScopedName()
    {
        const string message = """
            DSH 进程在就绪前退出（退出码 1）。stderr 末尾：Error: failed to import loader entry dsh-foo (@deepseek-ai/dsh-foo): The requested module '@deepseek-ai/dsh-settings' does not provide an export named 'installSettingsSection'
            """;

        await Assert.That(IncompatiblePluginCrashProbe.TryParseOffender(message)).IsEqualTo("@deepseek-ai/dsh-foo");
    }

    /// <summary>loader entry 行被 4096 字节尾部截断时，回退到 node_modules 路径解析。</summary>
    [Test]
    public async Task TryParseOffender_LoaderEntryTruncated_FallsBackToNodeModulesPath()
    {
        const string message = """
            DSH 进程在就绪前退出（退出码 1）。stderr 末尾：file:///D:/x/data/dsh-home/profiles/web/node_modules/@deepseek-ai/dsh-foo/lib/settings.js:35
            SyntaxError: The requested module '@deepseek-ai/dsh-settings' does not provide an export named 'installSettingsSection'
            """;

        await Assert.That(IncompatiblePluginCrashProbe.TryParseOffender(message)).IsEqualTo("@deepseek-ai/dsh-foo");
    }

    /// <summary>in-box bundle（@deepseek-ai/dsh-base）由 Runtime 安装体提供，profile 装不了 ⇒ 不自愈。</summary>
    [Test]
    public async Task TryParseOffender_InBoxBundle_ReturnsNull()
    {
        const string message = """
            DSH 进程在就绪前退出（退出码 1）。stderr 末尾：Error: failed to import loader entry dsh-base (@deepseek-ai/dsh-base): The requested module '@deepseek-ai/dsh-settings' does not provide an export named 'installSettingsSection'
            """;

        await Assert.That(IncompatiblePluginCrashProbe.TryParseOffender(message)).IsNull();
    }

    /// <summary>无「命名导出缺失」特征的启动失败（如 BOM 崩溃）：不解析、不误导。</summary>
    [Test]
    public async Task TryParseOffender_OtherCrashSignature_ReturnsNull()
    {
        const string message = """
            DSH 进程在就绪前退出（退出码 1）。stderr 末尾：SyntaxError: Unexpected token '锘?', "锘縶" "name"... is not valid JSON
            file:///D:/x/profiles/web/node_modules/dshmarket/package.json
            """;

        await Assert.That(IncompatiblePluginCrashProbe.TryParseOffender(message)).IsNull();
    }

    /// <summary>有特征但两条模式都解析不出插件名 ⇒ null（交人工排查）。</summary>
    [Test]
    public async Task TryParseOffender_SignatureWithoutIdentifiablePlugin_ReturnsNull()
    {
        const string message = """
            DSH 进程在就绪前退出（退出码 1）。stderr 末尾：SyntaxError: The requested module 'dsh-core' does not provide an export named 'foo'
            """;

        await Assert.That(IncompatiblePluginCrashProbe.TryParseOffender(message)).IsNull();
    }

    /// <summary>loader entry 行被截断（包名括号丢失）时，不得误吃后续堆栈行括号里的文件路径。</summary>
    [Test]
    public async Task TryParseOffender_LoaderEntryTruncatedMidLine_DoesNotEatStackFrameParens()
    {
        const string message = """
            DSH 进程在就绪前退出（退出码 1）。stderr 末尾：Error: failed to import loader entry dsh-market: The requested module '@deepseek-ai/dsh-settings' does not provide an export named 'installSettingsSection'
                at async Entry._init (file:///D:/x/runtime/dsh/0.1.5-rc.2/node_modules/@deepseek-ai/cordis-plugin-loader/lib/index.js:522:39)
            """;

        await Assert.That(IncompatiblePluginCrashProbe.TryParseOffender(message)).IsNull();
    }
}
