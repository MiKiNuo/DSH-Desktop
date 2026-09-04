namespace DshDesktop.Domain.Updates;

/// <summary>
/// 表示更新状态机（架构文档 §23，禁止布尔组合）。
/// </summary>
public enum UpdateStatus
{
    /// <summary>空闲。</summary>
    Idle,

    /// <summary>检查中。</summary>
    Checking,

    /// <summary>有可用更新。</summary>
    Available,

    /// <summary>下载中。</summary>
    Downloading,

    /// <summary>已就绪待安装。</summary>
    ReadyToInstall,

    /// <summary>安装中。</summary>
    Installing,

    /// <summary>失败。</summary>
    Failed,
}

/// <summary>
/// 表示一个可用的 DSH Runtime（借用的外部安装或自建 side-by-side 版本）。
/// </summary>
/// <param name="Version">版本号。</param>
/// <param name="IsActive">是否当前激活。</param>
/// <param name="IsBorrowed">是否借用的外部安装（只读，不可卸载）。</param>
public sealed record DshRuntimeInfo(
    string Version,
    bool IsActive,
    bool IsBorrowed);

/// <summary>
/// 表示一个插件的可用更新。
/// </summary>
/// <param name="Name">插件包名。</param>
/// <param name="CurrentVersion">当前安装版本。</param>
/// <param name="LatestVersion">npm 最新版本。</param>
public sealed record PluginUpdateInfo(
    string Name,
    string CurrentVersion,
    string LatestVersion);
