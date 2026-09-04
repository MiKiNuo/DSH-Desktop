namespace DshDesktop.Application.Plugins;

/// <summary>
/// 表示 Profile 快照端口（§19 安装事务的回滚单元）。
/// </summary>
public interface IProfileSnapshotter
{
    /// <summary>
    /// 创建清单快照（package.json / pnpm-lock.yaml / cordis.patch.yml / .npmrc），
    /// 并裁剪到保留上限。
    /// </summary>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>快照标识（目录名）。</returns>
    Task<string> CreateSnapshotAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 恢复快照并以 `pnpm install --frozen-lockfile --offline` 重建 node_modules。
    /// </summary>
    /// <param name="snapshotId">快照标识。</param>
    /// <param name="cancellationToken">取消标记。</param>
    Task RestoreAsync(string snapshotId, CancellationToken cancellationToken);
}
