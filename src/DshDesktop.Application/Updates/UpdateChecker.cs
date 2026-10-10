using DshDesktop.Domain.Updates;
using Serilog;

namespace DshDesktop.Application.Updates;

/// <summary>聚合两套独立版本的更新检查，并按开关预下载 Desktop 更新。</summary>
public sealed class UpdateChecker(
    IRuntimeRepository runtimeRepository,
    IDesktopUpdater desktopUpdater,
    ILogger logger)
{
    /// <summary>
    /// 串行检查 DSH 与 Desktop；活跃 Runtime 和预下载开关在使用时读取，
    /// 保留检查期间设置变更的观察时机。预下载不应用更新或重启宿主。
    /// </summary>
    public async Task<UpdateCheckResult> CheckAsync(
        string channel,
        Func<string?> activeRuntime,
        Func<bool> autoDownloadUpdates,
        CancellationToken cancellationToken = default)
    {
        string? latestDsh = null;
        try
        {
            latestDsh = await runtimeRepository.GetLatestVersionAsync(channel, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.Warning("Update.Check.DshFailed {Error}", exception.Message);
        }

        string? latestDesktop = null;
        try
        {
            latestDesktop = (await desktopUpdater.CheckForUpdatesAsync(cancellationToken)
                .ConfigureAwait(false))?.Version;
        }
        catch (Exception exception)
        {
            logger.Debug("Update.Check.DesktopSkipped {Error}", exception.Message);
        }

        IReadOnlyList<DshRuntimeInfo> runtimes = await runtimeRepository
            .ListRuntimesAsync(activeRuntime(), cancellationToken).ConfigureAwait(false);
        string? currentDsh = runtimes.FirstOrDefault(r => r.IsActive)?.Version;

        if (latestDesktop is not null && autoDownloadUpdates())
        {
            _ = AutoDownloadDesktopUpdateAsync();
        }

        return new UpdateCheckResult(latestDsh, currentDsh, runtimes, latestDesktop);
    }

    private async Task AutoDownloadDesktopUpdateAsync()
    {
        try
        {
            await desktopUpdater.DownloadAsync(null, CancellationToken.None).ConfigureAwait(false);
            logger.Information("Update.Desktop.AutoDownloaded");
        }
        catch (Exception exception)
        {
            logger.Debug("Update.Desktop.AutoDownloadSkipped {Error}", exception.Message);
        }
    }
}

/// <summary>Application 更新检查结果，由宿主适配为 Mediator 响应。</summary>
public sealed record UpdateCheckResult(
    string? LatestDshVersion,
    string? CurrentDshVersion,
    IReadOnlyList<DshRuntimeInfo> Runtimes,
    string? LatestDesktopVersion);
