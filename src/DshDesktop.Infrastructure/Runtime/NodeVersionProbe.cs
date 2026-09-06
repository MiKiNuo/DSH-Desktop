using System.Diagnostics;

namespace DshDesktop.Infrastructure.Runtime;

/// <summary>
/// 表示 Node 版本探针（Phase 8 Issue 03：Dashboard 统计卡 footer 数据源）：
/// 读 node.exe 文件版本（FileVersionInfo，AOT 兼容，不起进程）。
/// </summary>
public static class NodeVersionProbe
{
    /// <summary>
    /// 尝试读取 node 二进制的文件版本。
    /// </summary>
    /// <param name="nodePath">node.exe 路径（可为 PATH 上的裸名，此时返回 null）。</param>
    /// <returns>版本文本（如 "24.9.0"）；不可读为 null。</returns>
    public static string? TryGetVersion(string? nodePath)
    {
        if (string.IsNullOrWhiteSpace(nodePath) || !File.Exists(nodePath))
        {
            return null;
        }

        try
        {
            string? version = FileVersionInfo.GetVersionInfo(nodePath).ProductVersion;
            return string.IsNullOrWhiteSpace(version) ? null : version;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
