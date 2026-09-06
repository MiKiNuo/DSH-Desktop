using System.Diagnostics;
using DshDesktop.Application.Paths;

namespace DshDesktop.Infrastructure.Paths;

/// <summary>
/// 表示 <see cref="IPathOpener"/> 的 Windows 实现（Phase 8 Issue 05）：explorer.exe 起进程开目录。
/// ArgumentList 传参避免路径含空格的引号歧义。
/// </summary>
public sealed class ExplorerPathOpener : IPathOpener
{
    /// <inheritdoc />
    public void Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ProcessStartInfo startInfo = new("explorer.exe");
        startInfo.ArgumentList.Add(path);
        _ = Process.Start(startInfo);
    }
}
