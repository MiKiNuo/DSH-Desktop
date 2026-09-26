namespace DshDesktop.Application.Bootstrap;

/// <summary>
/// pnpm 自举端口（组合根拆分批 2a）：把 <c>PnpmProvisioner</c>（Infrastructure）抽象到 Application 层，
/// 供启动编排经配置加载端口调用。返回可用的 pnpm.cjs 绝对路径；无法确保可用时返回 null（绝不抛异常，
/// 与「装不了插件 ≠ 应用起不来」约定一致）。
/// </summary>
public interface IPnpmProvisioner
{
    /// <summary>返回可用的 pnpm.cjs 绝对路径；无法确保可用时返回 null。</summary>
    /// <param name="dataRoot">数据根；产物落在 &lt;dataRoot&gt;\tools\pnpm。</param>
    /// <param name="nodePath">node.exe 路径（可为 PATH 上的裸名）。</param>
    /// <param name="npmCjsPath">npm-cli.js 路径；不可用时无法自举。</param>
    /// <param name="currentPnpmCjsPath">配置中现有的 pnpm.cjs 路径（优先复用）。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>可用 pnpm.cjs 路径；失败为 null。</returns>
    Task<string?> EnsureAvailableAsync(
        string dataRoot,
        string nodePath,
        string? npmCjsPath,
        string? currentPnpmCjsPath,
        CancellationToken cancellationToken);
}
