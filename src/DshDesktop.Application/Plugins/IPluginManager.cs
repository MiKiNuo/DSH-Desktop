using DshDesktop.Domain.Plugins;

namespace DshDesktop.Application.Plugins;

/// <summary>
/// 表示插件管理端口（§18：DSH 无法启动时也必须可用，故全部为纯文件级操作）。
/// </summary>
public interface IPluginManager
{
    /// <summary>
    /// 列出 Profile 中的全部插件。
    /// </summary>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>插件清单。</returns>
    Task<IReadOnlyList<PluginInfo>> ListPluginsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 启用或禁用第三方插件（bundles 数组过滤/恢复，Electron disableInManifest 同款）。
    /// </summary>
    /// <param name="name">插件包名。</param>
    /// <param name="enabled">目标启用状态。</param>
    /// <param name="cancellationToken">取消标记。</param>
    Task SetEnabledAsync(string name, bool enabled, CancellationToken cancellationToken);

    /// <summary>
    /// 卸载第三方插件（复刻 Electron detachLegacyPlugin 四步：
    /// 改 package.json → 清 cordis.patch.yml 条目 → 删实目录 → 重建 lockfile）。
    /// </summary>
    /// <param name="name">插件包名。</param>
    /// <param name="cancellationToken">取消标记。</param>
    Task UninstallAsync(string name, CancellationToken cancellationToken);

    /// <summary>
    /// 安装插件（pnpm add + reconcile bundles）。
    /// </summary>
    /// <param name="source">npm 包名（可带版本）或本地 .tgz 文件路径。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>实际安装的插件包名。</returns>
    Task<string> InstallAsync(string source, CancellationToken cancellationToken);
}
