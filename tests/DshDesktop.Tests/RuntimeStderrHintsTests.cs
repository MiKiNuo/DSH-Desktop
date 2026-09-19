using DshDesktop.Infrastructure.Runtime;

namespace DshDesktop.Tests;

/// <summary>
/// RuntimeStderrHints：把 DSH 进程 stderr 末尾的已知崩溃特征翻译为可操作的排查提示。
/// 覆盖 2026-09-19 实机两类现场：
/// ① 插件 package.json 带 UTF-8 BOM → JSON.parse "is not valid JSON"（乱码 "锘?"）；
/// ② 旧版插件静态 import 已被 Runtime 删除的命名导出 → "does not provide an export"。
/// </summary>
public sealed class RuntimeStderrHintsTests
{
    /// <summary>命名导出缺失（插件与 Runtime 版本不兼容）：提示升级/卸载插件。</summary>
    [Test]
    public async Task Describe_MissingNamedExport_HintsPluginRuntimeMismatch()
    {
        const string stderr = """
            [harness-node] DSH entry failed: Error: dsh: plugin tree failed to load: failed to import loader entry dsh-market (dshmarket): The requested module '@deepseek-ai/dsh-settings' does not provide an export named 'installSettingsSection'
            SyntaxError: The requested module '@deepseek-ai/dsh-settings' does not provide an export named 'installSettingsSection'
            """;

        string? hint = RuntimeStderrHints.Describe(stderr);

        await Assert.That(hint).IsNotNull();
        await Assert.That(hint!).Contains("不兼容");
        await Assert.That(hint!).Contains("插件");
    }

    /// <summary>JSON.parse 失败（插件 package.json 含 BOM 或损坏）：提示 BOM/JSON 损坏方向。</summary>
    [Test]
    public async Task Describe_InvalidJson_HintsBomOrCorruptManifest()
    {
        const string stderr = """
            [harness-node] DSH entry failed: SyntaxError: Unexpected token '锘?', "锘縶"
              "name"... is not valid JSON
                at JSON.parse (<anonymous>)
                at loadProfileDirectory (file:///D:/x/node_modules/@deepseek-ai/dsh-app-boot/lib/index.js:849:25)
            """;

        string? hint = RuntimeStderrHints.Describe(stderr);

        await Assert.That(hint).IsNotNull();
        await Assert.That(hint!).Contains("BOM");
    }

    /// <summary>无关 stderr：不附加提示（不误导）。</summary>
    [Test]
    public async Task Describe_UnrelatedStderr_ReturnsNull()
    {
        await Assert.That(RuntimeStderrHints.Describe("some random failure\nat foo (bar.js:1:1)")).IsNull();
    }

    /// <summary>空 stderr：不附加提示。</summary>
    [Test]
    public async Task Describe_Empty_ReturnsNull()
    {
        await Assert.That(RuntimeStderrHints.Describe("")).IsNull();
    }
}
