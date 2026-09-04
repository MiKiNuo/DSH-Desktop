using DshDesktop.Domain.Updates;

namespace DshDesktop.Application.Updates;

/// <summary>
/// 表示 DSH Runtime 仓库端口（§4.4 RuntimeRepository：side-by-side 版本管理 + npm 更新源）。
/// </summary>
public interface IRuntimeRepository
{
    /// <summary>
    /// 列出全部可用 Runtime（借用条目 + 自建 side-by-side 版本）。
    /// </summary>
    /// <param name="activeRuntime">当前激活的自建版本目录名；null = 借用。</param>
    /// <param name="cancellationToken">取消标记。</param>
    Task<IReadOnlyList<DshRuntimeInfo>> ListRuntimesAsync(string? activeRuntime, CancellationToken cancellationToken);

    /// <summary>
    /// 查询指定通道的最新 DSH 版本。
    /// </summary>
    /// <param name="channel">npm dist-tag（latest / alpha）。</param>
    /// <param name="cancellationToken">取消标记。</param>
    Task<string> GetLatestVersionAsync(string channel, CancellationToken cancellationToken);

    /// <summary>
    /// 安装指定版本的 DSH Runtime 到 side-by-side 目录（npm install，Q6 决策）。
    /// </summary>
    /// <param name="version">目标版本。</param>
    /// <param name="cancellationToken">取消标记。</param>
    Task InstallAsync(string version, CancellationToken cancellationToken);

    /// <summary>
    /// 查询插件的最新版本。
    /// </summary>
    /// <param name="name">插件包名。</param>
    /// <param name="cancellationToken">取消标记。</param>
    Task<string?> GetLatestPluginVersionAsync(string name, CancellationToken cancellationToken);
}
