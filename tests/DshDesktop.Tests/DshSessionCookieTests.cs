using DshDesktop.Domain.Runtime;

namespace DshDesktop.Tests;

/// <summary>
/// 陈旧 dsh 会话 Cookie 的判定规则（2026-10-12 实机 431 故障的根因修复）。
///
/// <para>
/// dsh 的会话 Cookie 名 = <c>dsh-auth-&lt;base64url(sha256(authority))&gt;</c>，
/// authority 含端口（<c>dsh-client-connection</c> 的 <c>cookieName</c> / <c>requestAuthority</c>），
/// 宿主每次启动端口都变 ⇒ <b>每次启动都新增一条全新名字的 Cookie</b>，旧的不覆盖、也不过期。
/// 实机累积到 <b>70 条 / 25883 字节</b>（名长度恒为 52）后，WebView2 请求头越过 Node
/// <c>--max-http-header-size</c> 默认 16KB，dsh 直回 <b>431 Request Header Fields Too Large</b>，
/// 工作台整页变成 Edge 的 431 错误页。
/// </para>
///
/// <para>
/// 修复走 WebView2 自带的 <c>NativeWebView.TryGetCookieManager()</c>（Avalonia 12.1.0 提供），
/// 按名字精确删除 <c>dsh-auth-*</c>，不碰用户其它站点 Cookie —— 实机另有 57 条
/// YouTube / Google / GitHub / OpenAI 必须原样保留。
/// </para>
/// </summary>
public sealed class DshSessionCookieTests
{
    /// <summary>dsh 会话 Cookie 前缀（<c>dsh-client-connection</c> 的 <c>COOKIE_PREFIX</c>）。</summary>
    private const string Prefix = "dsh-auth-";

    /// <summary>
    /// 只删 <c>dsh-auth-</c> 前缀：其余站点的 Cookie 必须原样保留。
    /// 这是「不许顺手清用户浏览器数据」的硬约束。
    /// </summary>
    [Test]
    public async Task IsDshSessionCookie_OnlyMatchesDshAuthPrefix()
    {
        // 实机样本（长度恒为 52 = 9 前缀 + 43 base64url(sha256)）。
        await Assert.That(DshSessionCookie.IsDshSessionCookie(
            "dsh-auth-0M5d9Djz-1eyDF2zyg3u5SZM1Jfi8w5tfFKHZ0vMK3E")).IsTrue();

        await Assert.That(DshSessionCookie.IsDshSessionCookie("_ga")).IsFalse();
        await Assert.That(DshSessionCookie.IsDshSessionCookie("SID")).IsFalse();
        await Assert.That(DshSessionCookie.IsDshSessionCookie("__Secure-3PSID")).IsFalse();
        // 前缀大小写混写仍应命中（cookie 名不区分大小写语义上的稳妥做法）。
        await Assert.That(DshSessionCookie.IsDshSessionCookie("DSH-AUTH-abc")).IsTrue();
    }

    /// <summary>空名 / null 不得误判为 dsh 会话 Cookie（否则会误删）。</summary>
    [Test]
    public async Task IsDshSessionCookie_RejectsEmptyAndNull()
    {
        await Assert.That(DshSessionCookie.IsDshSessionCookie("")).IsFalse();
        await Assert.That(DshSessionCookie.IsDshSessionCookie(null)).IsFalse();
        // 只到前缀、后面没有 hash 的也要删（宁可多删一条 dsh 自己的，也不漏）。
        await Assert.That(DshSessionCookie.IsDshSessionCookie("dsh-auth-")).IsTrue();
    }

    /// <summary>
    /// 累积量达到阈值就必须清理：Cookie 总头一旦越过 16KB 就会 431。
    /// 实机 70 条 / 25883 字节即触发；这里用更保守的条数阈值兜底。
    /// </summary>
    [Test]
    public async Task NeedsPurge_TriggersOnAccumulatedCookies()
    {
        // 单条 cookie 也要清：换端口即成陈旧，永远不会被复用。
        await Assert.That(DshSessionCookie.NeedsPurge(1)).IsTrue();
        await Assert.That(DshSessionCookie.NeedsPurge(70)).IsTrue();
        // 一条都没有 ⇒ 无需动作。
        await Assert.That(DshSessionCookie.NeedsPurge(0)).IsFalse();
    }

    /// <summary>
    /// 保留策略：只留**最新一条**（当前 authority 的），其余全删。
    /// 这样同一 authority 反复启动也不会累积。
    /// </summary>
    [Test]
    public async Task SelectStaleNames_DropsAllButNewest()
    {
        // 按观测顺序给出（WebView2 枚举序），最后一条即当前 authority。
        var stale = DshSessionCookie.SelectStaleNames(
            ["dsh-auth-aaa", "dsh-auth-bbb", "dsh-auth-ccc"], newest: "dsh-auth-ccc");

        await Assert.That(stale.Count).IsEqualTo(2);
        await Assert.That(stale).DoesNotContain("dsh-auth-ccc");
        await Assert.That(stale).Contains("dsh-auth-aaa");
    }

    /// <summary>非 dsh 的 cookie 永不进入待删集合。</summary>
    [Test]
    public async Task SelectStaleNames_IgnoresForeignCookies()
    {
        var stale = DshSessionCookie.SelectStaleNames(
            ["SID", "dsh-auth-aaa", "_ga"], newest: "dsh-auth-aaa");

        await Assert.That(stale.Count).IsEqualTo(0);
    }
}
