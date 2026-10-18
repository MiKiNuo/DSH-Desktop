namespace DshDesktop.Application.Updates;

/// <summary>
/// Runtime 版本发现源端口（ADR-0011）。与 <see cref="IRuntimeRepository"/> 分离：
/// 本端口只回答「通道内最高版本是多少」，不碰 npm 进程与文件系统；
/// 仓库负责在源不可用时回退到 npm dist-tag。
/// </summary>
public interface IRuntimeVersionSource
{
    /// <summary>
    /// 查询指定通道的最高 DSH 版本。
    /// </summary>
    /// <param name="channel">发布通道。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>
    /// 版本号（npm 裸 semver）；源不可用（限流 / 网络 / 仓库改名）或该通道无候选版本时返回 null，
    /// 由调用方决定回退——返回 null 不等于「无更新」。
    /// </returns>
    Task<string?> TryGetLatestAsync(RuntimeReleaseChannel channel, CancellationToken cancellationToken);
}