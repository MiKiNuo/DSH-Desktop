using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DshDesktop.Infrastructure.Plugins;

/// <summary>
/// Profile bundle 可解析性探针：读取清单声明的 bundle，并判定其能否在 node_modules 上解析。
/// 判据与 DSH 的 resolveBundleDir 同源（node_modules/&lt;bundle&gt;/package.json 存在即就绪），
/// 供 ProfileSeeder 与 PluginProfileRepository 复用同一判据，避免两处维护漂移
/// （2026-09-14 实机：清单声明了 bundle 但磁盘解析不出 → Runtime 启动期抛
/// "cannot resolve profile bundle" → ExitCode=1）。
/// </summary>
internal static class ProfileBundleProbe
{
    /// <summary>
    /// 清单 <c>dsh.profile.bundles</c> 声明的 bundle 名称；未声明或清单损坏时返回空。
    /// </summary>
    internal static string[] DeclaredBundles(string profileDir)
    {
        string manifestPath = Path.Combine(profileDir, "package.json");
        if (!File.Exists(manifestPath))
        {
            return [];
        }

        try
        {
            if (JsonNode.Parse(File.ReadAllText(manifestPath)) is JsonObject manifest
                && manifest["dsh"]?["profile"]?["bundles"] is JsonArray { Count: > 0 } bundles)
            {
                return [.. bundles.Select(bundle => bundle?.GetValue<string>()).OfType<string>()];
            }
        }
        catch (JsonException)
        {
        }

        return [];
    }

    /// <summary>
    /// node_modules/&lt;bundle&gt;/package.json 是否存在——与 DSH resolveBundleDir 同源的就绪判据。
    /// 仅目录存在不算数（空壳/中断安装残留是「目录在、包不在」）。
    /// </summary>
    internal static bool IsBundleResolved(string profileDir, string bundle)
        => File.Exists(Path.Combine(profileDir, "node_modules", bundle, "package.json"));

    /// <summary>
    /// 声明了却无法在磁盘解析的 bundle（用于安装后校验：存在即表明 Profile 不可启动）。
    /// </summary>
    internal static string[] UnresolvedBundles(string profileDir)
    {
        string[] bundles = DeclaredBundles(profileDir);
        return [.. bundles.Where(bundle => !IsInBoxBundle(bundle) && !IsBundleResolved(profileDir, bundle))];
    }

    /// <summary>
    /// DSH in-box bundle：由 DSH 安装体（installation）提供，profile 的 node_modules 中永不含其
    /// package.json，DSH 的 resolveBundleDir 会先在 installation 侧查找。故 UnresolvedBundles 对其豁免。
    /// 显式名单、不按 scope 过滤（@openviking/... 等第三方 scope 仍须校验）；不计入 dshmarket——
    /// dshmarket 是真实 dependency，profile node_modules 中确实存在且需参与校验，豁免它会漏检真实损坏。
    /// </summary>
    internal static bool IsInBoxBundle(string bundle)
        => bundle is "@deepseek-ai/dsh-base" or "@deepseek-ai/dsh-web-app";
}
