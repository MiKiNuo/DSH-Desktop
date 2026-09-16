namespace DshDesktop.Presentation.Avalonia.Features.Diagnostics;

/// <summary>
/// 诊断日志的剪贴板文本拼装（右键复制）。
/// 纯函数、无 UI 依赖，便于单测；正文一律取自 <see cref="DiagnosticRow.Event"/>，
/// 不做截断或换行归一化，以便多行堆栈可原样粘贴进缺陷报告。
/// </summary>
public static class DiagnosticCopyText
{
    /// <summary>把单行日志拼成「时间戳 级别 正文」。</summary>
    /// <param name="row">日志行。</param>
    /// <returns>可直接写入剪贴板的文本（多行消息保留其自身换行）。</returns>
    public static string ForRow(DiagnosticRow row)
        => $"{row.TimestampText} {row.LevelText} {row.Message}";

    /// <summary>把多行日志按当前顺序逐行拼接（换行符取平台默认，Windows 下为 CRLF）。</summary>
    /// <param name="rows">日志行（通常为当前筛选结果）。</param>
    /// <returns>逐行拼接后的文本；空集合返回空串。</returns>
    public static string ForRows(IEnumerable<DiagnosticRow> rows)
        => string.Join(Environment.NewLine, rows.Select(ForRow));
}
