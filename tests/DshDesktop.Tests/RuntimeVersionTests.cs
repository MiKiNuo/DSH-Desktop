using DshDesktop.Application.Updates;

namespace DshDesktop.Tests;

/// <summary>
/// DSH Runtime 版本 semver 比较测试（ADR-0011）。
/// </summary>
/// <remarks>
/// 这套比较原先只作为 <c>ActiveRuntimeFallback</c> 的私有方法存在，只服务于「回退到哪个自建版本」。
/// 更新源迁到 GitHub 后它成了更新提示的正确性命门：字符串判等会把 <c>rc.10</c> 与 <c>rc.2</c> 判成不同版本，
/// 也会把「通道最新版比本机已装版本旧」判成有更新。
/// </remarks>
public sealed class RuntimeVersionTests
{
    [Test]
    [Arguments("0.2.1-rc.10", "0.2.1-rc.2", 1)]
    [Arguments("0.2.1-rc.2", "0.2.1-rc.10", -1)]
    [Arguments("0.2.1-rc.2", "0.2.0-rc.2", 1)]
    [Arguments("0.2.1", "0.2.1-rc.2", 1)]
    [Arguments("0.2.1-rc.2", "0.2.1", -1)]
    [Arguments("0.1.10", "0.1.9", 1)]
    [Arguments("0.2.1-rc.2", "0.2.1-rc.2", 0)]
    public async Task Compare_OrdersPrereleaseVersions(string left, string right, int expectedSign)
    {
        int actual = RuntimeVersion.Compare(left, right);

        await Assert.That(Math.Sign(actual)).IsEqualTo(expectedSign);
    }

    /// <summary>
    /// 真正的痛点：rc.10 与 rc.2 按字典序会判 "rc.2" 更大，导致最新版永远提示不出。
    /// </summary>
    [Test]
    public async Task IsNewer_RcTenBeatsRcTwo()
    {
        await Assert.That(RuntimeVersion.IsNewer("0.2.1-rc.10", "0.2.1-rc.2")).IsTrue();
    }

    /// <summary>换源后 GitHub 可能落后于本机已装版本，此时不得报「有可用更新」。</summary>
    [Test]
    public async Task IsNewer_SourceOlderThanInstalled_IsFalse()
    {
        await Assert.That(RuntimeVersion.IsNewer("0.2.0-rc.2", "0.2.1-rc.2")).IsFalse();
    }

    [Test]
    public async Task IsNewer_SameVersion_IsFalse()
    {
        await Assert.That(RuntimeVersion.IsNewer("0.2.1-rc.2", "0.2.1-rc.2")).IsFalse();
    }

    /// <summary>不可解析版本按序数兜底，保持确定性（既有 UpdatesStateTests 的 "rc.12"/"rc.13" 伪版本依赖此约定）。</summary>
    [Test]
    public async Task Compare_UnparsableVersions_FallBackToOrdinal()
    {
        await Assert.That(RuntimeVersion.Compare("rc.13", "rc.12")).IsGreaterThan(0);
        await Assert.That(RuntimeVersion.Compare("rc.12", "rc.12")).IsEqualTo(0);
    }
}