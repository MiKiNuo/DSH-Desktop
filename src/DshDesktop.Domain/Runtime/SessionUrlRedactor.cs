using System.Text.RegularExpressions;

namespace DshDesktop.Domain.Runtime;

/// <summary>
/// Session URL 打码（CONTEXT.md：Session URL 仅存内存，日志中 token 强制打码）。
/// 全仓唯一打码规则——进程输出日志（组合根）与 Workbench 导航日志共用，
/// 此前后者把含一次性 token 的完整 URL 明文落盘（2026-09-15 架构审查）。
/// </summary>
public static partial class SessionUrlRedactor
{
    /// <summary>把文本中所有 <c>token=…</c> 的值替换为 <c>***</c>；null 原样返回。</summary>
    public static string? Redact(string? text)
    {
        return text is null ? null : TokenValueRegex().Replace(text, "$1***");
    }

    [GeneratedRegex(@"(?i)(token=)[^\s&]+")]
    private static partial Regex TokenValueRegex();
}
