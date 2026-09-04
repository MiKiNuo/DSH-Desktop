namespace DshDesktop.Domain.Plugins;

/// <summary>
/// 表示一个 DSH 插件（CONTEXT.md: Plugin）。
/// </summary>
/// <param name="Name">npm 包名（bundles 数组元素）。</param>
/// <param name="Version">已安装的真实版本（node_modules/&lt;pkg&gt;/package.json）。</param>
/// <param name="IsCore">是否官方核心插件（@deepseek-ai/* scope 或 dshmarket）。核心插件只读。</param>
/// <param name="Enabled">是否启用（在 dsh.profile.bundles 数组中）。</param>
public sealed record PluginInfo(
    string Name,
    string Version,
    bool IsCore,
    bool Enabled);
