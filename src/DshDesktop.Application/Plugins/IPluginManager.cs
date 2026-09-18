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

    /// <summary>
    /// 自愈核心插件启用状态：核心插件（@deepseek-ai/* 与 dshmarket）恒应启用，
    /// 清单被外部改出 bundles 时写回（2026-09-18 实机：dshmarket 被改坏后工作台不可用，
    /// 而核心插件只读约定让 Desktop 无任何入口救回）。磁盘上不存在（不可解析）的
    /// 核心插件不写入——否则 DSH 启动 resolveBundleDir 抛错，把「禁用」修成「启动崩溃」。
    /// </summary>
    /// <param name="cancellationToken">取消标记。</param>
    Task HealCoreBundlesAsync(CancellationToken cancellationToken)
    {
        // 默认空实现：测试假实现无需逐个补齐；生产实现（PluginProfileRepository）覆盖。
        return Task.CompletedTask;
    }
}
