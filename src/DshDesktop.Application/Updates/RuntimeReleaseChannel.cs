namespace DshDesktop.Application.Updates;

/// <summary>
/// DSH Runtime 发布通道（ADR-0011）。版本发现源迁到 GitHub Releases 后，npm dist-tag
/// 不再有对应物，通道退化为「是否接纳预发布」这一件事——上游 0.2.x 目前全部是预发布
/// （实测 GitHub Releases 六个 release 的 prerelease 均为 true），故默认档为含预发布。
/// </summary>
public enum RuntimeReleaseChannel
{
    /// <summary>仅正式版：只要 GitHub 上 prerelease=false 的 Release。</summary>
    Stable,

    /// <summary>含预发布：rc / alpha 一并纳入候选（默认档）。</summary>
    Prerelease,
}

/// <summary>
/// 通道字面量解析、迁移与 npm dist-tag 映射（ADR-0011）。
/// </summary>
public static class RuntimeChannel
{
    /// <summary>仅正式版通道的配置字面量。</summary>
    public const string StableLiteral = "stable";

    /// <summary>含预发布通道的配置字面量。</summary>
    public const string PrereleaseLiteral = "prerelease";

    /// <summary>
    /// 默认通道：含预发布。上游至今未发正式版，默认走 Stable 会让所有用户永远看不到更新。
    /// </summary>
    public const RuntimeReleaseChannel Default = RuntimeReleaseChannel.Prerelease;

    /// <summary>UI 可选通道字面量（供 Settings 下拉，避免字面量在视图里各写一份）。</summary>
    public static readonly IReadOnlyList<string> Literals = [StableLiteral, PrereleaseLiteral];

    /// <summary>
    /// 归一化配置中的通道字面量。只有 <c>stable</c> 进正式版档，其余一律落默认档——
    /// 含 ADR-0011 之前的旧 dist-tag 字面（latest / alpha）与历史拼写错误：
    /// 旧 <c>latest</c> 语义是「稳定」，但上游至今无正式版，保持旧义等于保持「看不到 rc」这个原始症状。
    /// </summary>
    /// <param name="raw">配置落盘的通道字面量（可空）。</param>
    /// <returns>归一化后的通道。</returns>
    public static RuntimeReleaseChannel Normalize(string? raw)
        => string.Equals(raw?.Trim(), StableLiteral, StringComparison.OrdinalIgnoreCase)
            ? RuntimeReleaseChannel.Stable
            : RuntimeReleaseChannel.Prerelease;

    /// <summary>
    /// 通道 → npm dist-tag：GitHub 版本发现不可用时的回退查询目标。
    /// 实测 dist-tag：latest=0.2.0-rc.2、next=0.2.1-rc.2、alpha=0.2.1-alpha.5；
    /// 含预发布档取 next 而非 alpha——rc 高于 alpha，alpha 会把回退用户留在更旧的线上。
    /// </summary>
    /// <param name="channel">通道。</param>
    /// <returns>npm dist-tag 字面量。</returns>
    public static string ToDistTag(RuntimeReleaseChannel channel)
        => channel == RuntimeReleaseChannel.Stable ? "latest" : "next";
}