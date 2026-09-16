using DshDesktop.Domain.Runtime;

namespace DshDesktop.Tests;

/// <summary>
/// Session URL 打码（CONTEXT.md：Session URL 仅存内存、日志中 token 强制打码）。
/// 打码规则原先私有于组合根（TokenRedactRegex），只覆盖进程输出日志；
/// Workbench 导航失败日志曾把含一次性 token 的完整 URL 明文写入 data/logs（2026-09-15 架构审查发现）。
/// 收编为 Domain 共享模块后，两条日志路径必须走同一规则。
/// </summary>
public sealed class SessionUrlRedactorTests
{
    [Test]
    public async Task TokenQueryParam_IsMasked()
    {
        await Assert.That(
                SessionUrlRedactor.Redact("http://127.0.0.1:5123/?token=abc123XYZ"))
            .IsEqualTo("http://127.0.0.1:5123/?token=***");
    }

    [Test]
    public async Task TokenFollowedByOtherParams_KeepsOtherParams()
    {
        await Assert.That(
                SessionUrlRedactor.Redact("http://127.0.0.1:5123/?token=secret&theme=dark"))
            .IsEqualTo("http://127.0.0.1:5123/?token=***&theme=dark");
    }

    [Test]
    public async Task TokenKey_IsCaseInsensitive()
    {
        await Assert.That(
                SessionUrlRedactor.Redact("see TOKEN=UpperCaseValue here"))
            .IsEqualTo("see TOKEN=*** here");
    }

    [Test]
    public async Task NoToken_TextPassesThrough()
    {
        const string text = "http://127.0.0.1:5123/workbench?theme=dark";
        await Assert.That(SessionUrlRedactor.Redact(text)).IsEqualTo(text);
    }

    [Test]
    public async Task NullOrEmpty_ReturnsAsIs()
    {
        await Assert.That(SessionUrlRedactor.Redact(null)).IsNull();
        await Assert.That(SessionUrlRedactor.Redact(string.Empty)).IsEqualTo(string.Empty);
    }
}
