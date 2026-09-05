namespace DshDesktop.Application.Updates;

/// <summary>
/// 表示一个可用的 Desktop 更新。
/// </summary>
/// <param name="Version">目标版本号。</param>
public sealed record DesktopUpdateInfo(string Version);

/// <summary>
/// 表示 Desktop 自更新端口（§4.4 VelopackUpdater；ADR-0003）。
/// 线性三段流程：检查 → 下载 → 应用重启；检查到的更新由适配器持有（一次性语义）。
/// 未安装形态（dotnet run / 便携解压）下全部操作 no-op。
/// </summary>
public interface IDesktopUpdater
{
    /// <summary>
    /// 获取当前是否为已安装形态（Velopack 安装包部署）。
    /// </summary>
    bool IsInstalled { get; }

    /// <summary>
    /// 检查更新；无更新或未安装返回 null。非 null 返回后该更新被适配器持有，供后续下载/应用。
    /// </summary>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>可用更新；无更新为 null。</returns>
    Task<DesktopUpdateInfo?> CheckForUpdatesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 下载最近检查到的更新。
    /// </summary>
    /// <param name="progress">下载进度（0-100）。</param>
    /// <param name="cancellationToken">取消标记。</param>
    Task DownloadAsync(IProgress<int>? progress, CancellationToken cancellationToken);

    /// <summary>
    /// 应用已下载的更新并重启（当前进程退出，不返回）。
    /// </summary>
    void ApplyAndRestart();
}
