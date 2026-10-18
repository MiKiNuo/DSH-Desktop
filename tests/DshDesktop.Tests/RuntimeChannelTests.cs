using DshDesktop.Application.Updates;
using DshDesktop.Infrastructure.Config;

namespace DshDesktop.Tests;

/// <summary>
/// DSH Runtime 发布通道归一化测试（ADR-0011：npm dist-tag → stable / prerelease 两档）。
/// </summary>
public sealed class RuntimeChannelTests
{
    [Test]
    [Arguments("stable")]
    [Arguments("STABLE")]
    [Arguments(" stable ")]
    public async Task Normalize_StableLiteral_IsStable(string raw)
    {
        await Assert.That(RuntimeChannel.Normalize(raw)).IsEqualTo(RuntimeReleaseChannel.Stable);
    }

    /// <summary>
    /// 旧配置里的 dist-tag 字面必须迁入含预发布档：旧 <c>latest</c> 名义是「稳定」，
    /// 但上游至今无正式版（旧 latest 实际指向 0.2.0-rc.2），保持旧义等于保留「看不到 rc」这个原始症状。
    /// </summary>
    [Test]
    [Arguments("prerelease")]
    [Arguments("latest")]
    [Arguments("alpha")]
    [Arguments("")]
    public async Task Normalize_LegacyOrUnknownLiteral_FallsBackToPrerelease(string raw)
    {
        await Assert.That(RuntimeChannel.Normalize(raw)).IsEqualTo(RuntimeReleaseChannel.Prerelease);
    }

    /// <summary>配置字段缺失（反序列化保留默认值）时同样落默认档。</summary>
    [Test]
    public async Task Normalize_Null_FallsBackToPrerelease()
    {
        await Assert.That(RuntimeChannel.Normalize(null)).IsEqualTo(RuntimeReleaseChannel.Prerelease);
    }

    [Test]
    public async Task Default_IsPrerelease()
    {
        await Assert.That(RuntimeChannel.Default).IsEqualTo(RuntimeReleaseChannel.Prerelease);
        await Assert.That(new DshDesktopConfig().DshChannel)
            .IsEqualTo(RuntimeChannel.PrereleaseLiteral);
    }

    /// <summary>回退查询的 dist-tag：稳定档取 latest，预发布档取 next（实测 next=0.2.1-rc.2，alpha 更旧）。</summary>
    [Test]
    public async Task ToDistTag_MapsChannelToDistTag()
    {
        await Assert.That(RuntimeChannel.ToDistTag(RuntimeReleaseChannel.Stable)).IsEqualTo("latest");
        await Assert.That(RuntimeChannel.ToDistTag(RuntimeReleaseChannel.Prerelease)).IsEqualTo("next");
    }
}