using DshDesktop.Domain.Diagnostics;

namespace DshDesktop.Application.Diagnostics;

/// <summary>
/// 表示一项命名健康检查（Phase 8 Issue 06）：探测委托由组合根装配（进程/HTTP/Profile/插件），
/// Runner 不感知具体探测手段。
/// </summary>
/// <param name="Name">检查名（写入诊断事件 Message）。</param>
/// <param name="Run">探测委托；返回 false 或抛异常均记为失败。</param>
public sealed record DiagnosisCheck(string Name, Func<CancellationToken, Task<bool>> Run);

/// <summary>
/// 表示运行诊断编排器（原型 221-243 行 "运行诊断" 按钮）：按序执行健康检查序列，
/// 把结构化事件（<see cref="DiagnosticEventNames"/> 的 Diagnosis.* 常量）发布到诊断流。
/// </summary>
/// <param name="hub">诊断事件中枢。</param>
public sealed class DiagnosisRunner(DiagnosticsHub hub)
{
    /// <summary>
    /// 按序执行检查序列并发布 Diagnosis.Started → Check.Passed/Failed × N → Completed。
    /// </summary>
    /// <param name="checks">检查序列。</param>
    /// <param name="cancellationToken">取消标记。</param>
    public async Task RunAsync(IReadOnlyList<DiagnosisCheck> checks, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checks);

        Publish(DiagnosticLevel.Info, DiagnosticEventNames.DiagnosisStarted);

        int failures = 0;
        foreach (DiagnosisCheck check in checks)
        {
            bool passed;
            string? error = null;
            try
            {
                passed = await check.Run(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw; // 取消不是检查失败，透传中止序列。
            }
            catch (Exception exception)
            {
                passed = false;
                error = exception.Message;
            }

            if (passed)
            {
                Publish(DiagnosticLevel.Success, $"✓ {DiagnosticEventNames.DiagnosisCheckPassed} {check.Name}");
            }
            else
            {
                failures++;
                string detail = error is null ? string.Empty : $"：{error}";
                Publish(DiagnosticLevel.Warning, $"✗ {DiagnosticEventNames.DiagnosisCheckFailed} {check.Name}{detail}");
            }
        }

        Publish(
            failures == 0 ? DiagnosticLevel.Info : DiagnosticLevel.Warning,
            failures == 0
                ? $"{DiagnosticEventNames.DiagnosisCompleted} 未发现异常"
                : $"{DiagnosticEventNames.DiagnosisCompleted} 发现 {failures} 项异常");
    }

    private void Publish(DiagnosticLevel level, string message)
    {
        hub.Publish(new DiagnosticEvent(DateTimeOffset.Now, DiagnosticSource.App, level, message));
    }
}
