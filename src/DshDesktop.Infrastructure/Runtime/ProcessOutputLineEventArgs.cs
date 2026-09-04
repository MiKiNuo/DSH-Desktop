namespace DshDesktop.Infrastructure.Runtime;

/// <summary>
/// 表示 DSH 进程一行输出的事件参数。
/// </summary>
/// <param name="isError">是否来自 stderr。</param>
/// <param name="line">输出内容（单行）。</param>
public sealed class ProcessOutputLineEventArgs(bool isError, string line) : EventArgs
{
    /// <summary>获取是否来自 stderr。</summary>
    public bool IsError { get; } = isError;

    /// <summary>获取输出内容。</summary>
    public string Line { get; } = line;
}
