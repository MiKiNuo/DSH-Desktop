namespace DshDesktop.Domain.Runtime;

/// <summary>
/// dsh 会话 Cookie 的判定规则（2026-10-12 实机 431 故障的根因）。
/// </summary>
/// <remarks>
/// dsh 的会话 Cookie 名 = <c>dsh-auth-&lt;base64url(sha256(authority))&gt;</c>，
/// authority 含端口（<c>dsh-client-connection</c> 的 <c>cookieName</c> / <c>requestAuthority</c>），
/// 而宿主每次启动端口都变 ⇒ <b>每次启动都新增一条全新名字的 Cookie</b>，旧的不覆盖、也不过期。
/// 实机累积到 <b>70 条 / 25883 字节</b>（名长度恒为 52）后，WebView2 请求头越过 Node
/// <c>--max-http-header-size</c> 默认 16KB，dsh 直回 <b>431 Request Header Fields Too Large</b>，
/// 工作台整页变成 Edge 的 431 错误页。
/// <para>
/// 清理走 WebView2 自带的 <c>NativeWebView.TryGetCookieManager()</c>，按名字精确删除，
/// 不碰用户其它站点 Cookie（实机另有 57 条 YouTube / Google / GitHub / OpenAI）。
/// 本类只放纯判定规则，供基础设施层调用与直测。
/// </para>
/// </remarks>
public static class DshSessionCookie
{
    /// <summary>dsh 会话 Cookie 前缀（<c>dsh-client-connection</c> 的 <c>COOKIE_PREFIX</c>）。</summary>
    public const string NamePrefix = "dsh-auth-";

    /// <summary>该 Cookie 名是否属于 dsh 会话（可安全删除）。</summary>
    public static bool IsDshSessionCookie(string? name) =>
        !string.IsNullOrEmpty(name)
        && name.StartsWith(NamePrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 是否需要清理：只要存在一条 dsh 会话 Cookie 就有陈旧项——
    /// 换端口即成陈旧，任何一条都不该被下一轮复用。
    /// </summary>
    public static bool NeedsPurge(int sessionCookieCount) => sessionCookieCount > 0;

    /// <summary>
    /// 从枚举结果里挑出该删除的 cookie 名：全部 dsh 会话 Cookie，<b>保留最新的那条</b>。
    /// </summary>
    /// <param name="observedNames">WebView2 枚举出的 cookie 名（顺序不限）。</param>
    /// <param name="newest">当前 authority 对应的最新 cookie 名；不在列表中时全部删除。</param>
    public static IReadOnlyList<string> SelectStaleNames(
        IReadOnlyCollection<string> observedNames,
        string? newest)
    {
        ArgumentNullException.ThrowIfNull(observedNames);

        List<string> stale = [];
        foreach (string name in observedNames)
        {
            if (!IsDshSessionCookie(name))
            {
                continue;
            }

            if (newest is not null
                && string.Equals(name, newest, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            stale.Add(name);
        }

        return stale;
    }
}
