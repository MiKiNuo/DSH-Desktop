using DshDesktop.Application.Bootstrap;

namespace DshDesktop.Infrastructure.Plugins;

/// <summary>
/// 引导期自愈登记册所需的 Profile 清单归一化端口（<see cref="IProfileManifestNormalizer"/>）公共实现：
/// 转发到内部 <see cref="ProfileManifestFixups.NormalizeToFlatModel"/>。
/// <para>
/// 之所以把这一桥做成 <c>public</c>（而非让 App 经 <c>InternalsVisibleTo</c> 直调内部件）：收窄越权面——
/// 仅「归一化」这一内部件经此桥对 Application 层公开，不向组合根敞开整个 Infrastructure 的
/// <c>internal</c> 面（其他 internal 件如 IncompatiblePluginCrashProbe 仍由组合根经接口 + 自身适配器桥接）。
/// Infrastructure → Application 的项目引用方向本就合法（Infrastructure.csproj 已引用 Application）。
/// </para>
/// </summary>
public sealed class ProfileManifestNormalizer : IProfileManifestNormalizer
{
    /// <inheritdoc />
    public void NormalizeToFlatModel(string profileDir)
        => ProfileManifestFixups.NormalizeToFlatModel(profileDir);
}
