namespace DshDesktop.Application.Bootstrap;

/// <summary>
/// Profile 清单归一化端口（组合根拆分批 1）：把 <c>ProfileManifestFixups.NormalizeToFlatModel</c>
/// （Infrastructure）抽象到 Application 层，供引导期自愈登记册的归一化条目消费，
/// 避免 Application 新增对 Infrastructure 的项目引用。组合根注入适配器转调真实实现。
/// </summary>
public interface IProfileManifestNormalizer
{
    /// <summary>把 profile 清单归一化到扁平 pnpm 模型（剥离代际投影残留 + 悬空 overrides + BOM）。</summary>
    /// <param name="profileDir">profile 目录（含 package.json）。</param>
    void NormalizeToFlatModel(string profileDir);
}
