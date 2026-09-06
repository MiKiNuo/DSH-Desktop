namespace DshDesktop.Application.Paths;

/// <summary>
/// 表示路径打开端口（Phase 8 Issue 05，§4.1：Presentation 禁止直接起进程）：
/// Application 层只声明"打开"，Windows explorer 实现落在 Infrastructure。
/// </summary>
public interface IPathOpener
{
    /// <summary>
    /// 在系统文件管理器中打开指定目录。
    /// </summary>
    /// <param name="path">目录绝对路径。</param>
    void Open(string path);
}
