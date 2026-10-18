using DshDesktop.Application.Updates;
using DshDesktop.Infrastructure.Updates;

namespace DshDesktop.Tests;

/// <summary>
/// GitHub Runtime 版本发现源测试（ADR-0011）。
/// </summary>
/// <remarks>
/// JSON 样例取自 deepseek-ai/deepseek-harness 实测响应：tag 形如 <c>dsh-v0.2.1-rc.2</c>、
/// 预发布全部标 <c>prerelease: true</c>、<c>assets</c> 为空数组。因此 <c>releases/latest</c>
/// 对该仓库 404（latest 只返回非预发布的最新一个），必须走列表接口。
/// </remarks>
public sealed class GitHubRuntimeVersionSourceTests
{
    /// <summary>实测形态的响应：预发布三连 + 一个同仓但无关的非预发布 tag（多语言 SDK 线）。</summary>
    private const string AllPrereleaseResponse = """
        [
          { "tag_name": "dsh-v0.2.1-rc.2", "prerelease": true, "draft": false, "assets": [] },
          { "tag_name": "dsh-v0.2.1-rc.1", "prerelease": true, "draft": false, "assets": [] },
          { "tag_name": "dsh-v0.2.1-alpha.5", "prerelease": true, "draft": false, "assets": [] },
          { "tag_name": "v1.2.3", "prerelease": false, "draft": false, "assets": [] }
        ]
        """;

    /// <summary>预发布档取全量最高：rc.2 高于 rc.1、alpha.5，且必须剥离 dsh-v 前缀交回 npm 裸 semver。</summary>
    [Test]
    public async Task SelectLatest_PrereleaseChannel_TakesHighestWithPrefixStripped()
    {
        string? latest = GitHubRuntimeVersionSource.SelectLatest(
            AllPrereleaseResponse, RuntimeReleaseChannel.Prerelease);

        await Assert.That(latest).IsEqualTo("0.2.1-rc.2");
    }

    /// <summary>
    /// 稳定档在当前上游形态下无候选（0.2.x 全是预发布）——这正是默认档必须是 prerelease 的原因。
    /// 同仓的 v1.2.3 是别的产品线，不能被当成 Runtime 稳定版。
    /// </summary>
    [Test]
    public async Task SelectLatest_StableChannel_IgnoresPrereleaseAndForeignTags()
    {
        string? latest = GitHubRuntimeVersionSource.SelectLatest(
            AllPrereleaseResponse, RuntimeReleaseChannel.Stable);

        await Assert.That(latest).IsNull();
    }

    [Test]
    public async Task SelectLatest_StableChannel_TakesNonPrereleaseDshTag()
    {
        const string json = """
            [
              { "tag_name": "dsh-v0.2.1-rc.2", "prerelease": true, "draft": false, "assets": [] },
              { "tag_name": "dsh-v0.2.1", "prerelease": false, "draft": false, "assets": [] },
              { "tag_name": "dsh-v0.3.0", "prerelease": false, "draft": true, "assets": [] }
            ]
            """;

        string? latest = GitHubRuntimeVersionSource.SelectLatest(
            json, RuntimeReleaseChannel.Stable);

        await Assert.That(latest).IsEqualTo("0.2.1");
    }

    /// <summary>rc.10 必须压过 rc.2：按列表顺序取第一个（GitHub 按时间倒序）会取错。</summary>
    [Test]
    public async Task SelectLatest_OrdersNumericallyNotByListPosition()
    {
        const string json = """
            [
              { "tag_name": "dsh-v0.2.1-rc.2", "prerelease": true, "draft": false, "assets": [] },
              { "tag_name": "dsh-v0.2.1-rc.10", "prerelease": true, "draft": false, "assets": [] }
            ]
            """;

        string? latest = GitHubRuntimeVersionSource.SelectLatest(
            json, RuntimeReleaseChannel.Prerelease);

        await Assert.That(latest).IsEqualTo("0.2.1-rc.10");
    }

    [Test]
    [Arguments("[]")]
    [Arguments("""[ { "tag_name": "other-v1.0.0" } ]""")]
    [Arguments("""[ { "tag_name": "dsh-v", "prerelease": true } ]""")]
    [Arguments("not json at all")]
    public async Task SelectLatest_NoUsableCandidate_ReturnsNull(string json)
    {
        string? latest = GitHubRuntimeVersionSource.SelectLatest(
            json, RuntimeReleaseChannel.Prerelease);

        await Assert.That(latest).IsNull();
    }
}