using DshDesktop.Application.Updates;
using Serilog;
using Velopack;
using Velopack.Sources;

namespace DshDesktop.Infrastructure.Updates;

/// <summary>
/// 表示 <see cref="IDesktopUpdater"/> 的 Velopack 实现（ADR-0003：GitHub Releases 源，
/// 单通道 = 正式 Release；未安装形态整体 no-op）。
/// </summary>
public sealed class VelopackDesktopUpdater : IDesktopUpdater
{
    private const string GitHubRepoUrl = "https://github.com/MiKiNuo/DSH-Desktop";

    private readonly ILogger _logger;
    private readonly UpdateManager? _manager;
    private UpdateInfo? _pendingUpdate;

    /// <summary>
    /// 初始化 Velopack Desktop 更新适配器。
    /// </summary>
    /// <param name="logger">结构化日志。</param>
    public VelopackDesktopUpdater(ILogger logger)
    {
        _logger = logger.ForContext("Source", "DesktopUpdater");

        // 未安装形态（dotnet run / 便携解压）下 UpdateManager 构造或定位失败 → 降级 no-op。
        try
        {
            _manager = new UpdateManager(new GithubSource(GitHubRepoUrl, null, false));
        }
        catch (Exception exception)
        {
            _logger.Debug("Update.Desktop.Unavailable {Error}", exception.Message);
            _manager = null;
        }
    }

    /// <inheritdoc />
    public bool IsInstalled => _manager?.IsInstalled == true;

    /// <inheritdoc />
    public async Task<DesktopUpdateInfo?> CheckForUpdatesAsync(CancellationToken cancellationToken)
    {
        if (!IsInstalled)
        {
            return null;
        }

        UpdateInfo? info = await _manager!.CheckForUpdatesAsync().ConfigureAwait(false);
        _pendingUpdate = info;
        return info is null
            ? null
            : new DesktopUpdateInfo(info.TargetFullRelease.Version.ToString());
    }

    /// <inheritdoc />
    public async Task DownloadAsync(IProgress<int>? progress, CancellationToken cancellationToken)
    {
        if (_pendingUpdate is null || _manager is null)
        {
            return;
        }

        await _manager.DownloadUpdatesAsync(
            _pendingUpdate,
            percent => progress?.Report(percent),
            cancellationToken).ConfigureAwait(false);
        _logger.Information("Update.Desktop.Downloaded {Version}", _pendingUpdate.TargetFullRelease.Version);
    }

    /// <inheritdoc />
    public void ApplyAndRestart()
    {
        if (_pendingUpdate is null || _manager is null)
        {
            return;
        }

        _logger.Information("Update.Desktop.ApplyRestart {Version}", _pendingUpdate.TargetFullRelease.Version);
        _manager.ApplyUpdatesAndRestart(_pendingUpdate);
    }
}
