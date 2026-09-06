using DshDesktop.Domain.Diagnostics;
using DshDesktop.Presentation.Avalonia.Features.Diagnostics;

namespace DshDesktop.Tests;

/// <summary>
/// Live 控制台行投影测试（Phase 8 评审 F10）：着色只看显式 Level，不嗅探消息文本前缀。
/// </summary>
public sealed class DiagnosticRowTests
{
    [Test]
    public async Task Row_SuccessLevel_IsOk()
    {
        var row = new DiagnosticRow(new DiagnosticEvent(
            DateTimeOffset.Now, DiagnosticSource.App, DiagnosticLevel.Success, "Diagnosis.Check.Passed A"));

        await Assert.That(row.IsOk).IsTrue();
        await Assert.That(row.IsWarning).IsFalse();
        await Assert.That(row.IsError).IsFalse();
    }

    [Test]
    public async Task Row_InfoWithCheckmarkPrefix_IsNotOk()
    {
        // F10：✓ 前缀嗅探已移除——Info 级即使带 ✓ 也不着绿色。
        var row = new DiagnosticRow(new DiagnosticEvent(
            DateTimeOffset.Now, DiagnosticSource.App, DiagnosticLevel.Info, "✓ something"));

        await Assert.That(row.IsOk).IsFalse();
    }

    [Test]
    public async Task Row_WarningAndError_MapFromLevel()
    {
        var warning = new DiagnosticRow(new DiagnosticEvent(
            DateTimeOffset.Now, DiagnosticSource.App, DiagnosticLevel.Warning, "w"));
        var error = new DiagnosticRow(new DiagnosticEvent(
            DateTimeOffset.Now, DiagnosticSource.App, DiagnosticLevel.Error, "e"));

        await Assert.That(warning.IsWarning).IsTrue();
        await Assert.That(warning.IsOk).IsFalse();
        await Assert.That(error.IsError).IsTrue();
        await Assert.That(error.IsWarning).IsFalse();
    }
}
