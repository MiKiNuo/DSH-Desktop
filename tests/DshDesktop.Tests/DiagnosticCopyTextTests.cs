using DshDesktop.Domain.Diagnostics;
using DshDesktop.Presentation.Avalonia.Features.Diagnostics;

namespace DshDesktop.Tests;

/// <summary>
/// 诊断日志剪贴板文本拼装测试（右键复制）：
/// 单行 = 「时间戳 级别 正文」，多行按平台换行拼接，多行堆栈正文原样保留（不吞换行、不截断）。
/// </summary>
public sealed class DiagnosticCopyTextTests
{
    private static readonly DateTimeOffset Stamp =
        new(2026, 9, 16, 21, 29, 17, 586, TimeSpan.FromHours(8));

    [Test]
    public async Task ForRow_PrefixesTimestampAndLevel()
    {
        var row = Row(DiagnosticLevel.Error, "Plugin hotback restore failed");

        await Assert.That(DiagnosticCopyText.ForRow(row))
            .IsEqualTo("21:29:17.586 ERROR Plugin hotback restore failed");
    }

    [Test]
    public async Task ForRow_KeepsMultiLineMessageVerbatim()
    {
        const string stack =
            "Error: Cannot find module 'pnpm.cjs'\n"
            + "    at Module._load (node:internal/modules/cjs/loader:1423:12)\n"
            + "    at node:internal/main/run_main_module:33:47";
        var row = Row(DiagnosticLevel.Error, stack);

        var text = DiagnosticCopyText.ForRow(row);

        // 堆栈必须逐字保留：换行与缩进都不能被拼接过程改动。
        await Assert.That(text).EndsWith(stack, StringComparison.Ordinal);
        await Assert.That(text.Split('\n').Length).IsEqualTo(3);
    }

    [Test]
    public async Task ForRows_JoinsWithNewLineInOrder()
    {
        DiagnosticRow[] rows =
        [
            Row(DiagnosticLevel.Info, "first"),
            Row(DiagnosticLevel.Warning, "second"),
        ];

        await Assert.That(DiagnosticCopyText.ForRows(rows))
            .IsEqualTo(
                $"21:29:17.586 INFO first{Environment.NewLine}21:29:17.586 WARN second");
    }

    [Test]
    public async Task ForRows_Empty_ReturnsEmptyString()
    {
        await Assert.That(DiagnosticCopyText.ForRows([])).IsEqualTo(string.Empty);
    }

    private static DiagnosticRow Row(DiagnosticLevel level, string message)
        => new(new DiagnosticEvent(Stamp, DiagnosticSource.App, level, message));
}
