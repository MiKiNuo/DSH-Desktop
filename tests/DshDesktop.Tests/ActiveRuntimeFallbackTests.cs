using DshDesktop.Application.Updates;

namespace DshDesktop.Tests;

/// <summary>
/// 借用失效时自动回退激活自建 Runtime 的选择策略测试（2026-09-19 v0.1.3 实机回归）：
/// 借用 Electron 安装被删后 dshEntryPath 被 HealRuntimePaths 清空、activeDshRuntime 仍为 null，
/// 磁盘上自建版本完好——不自动激活则每次启动 ValidateOptions 秒抛，连续 2 次进安全模式且日志无原因。
/// 接缝：<see cref="ActiveRuntimeFallback.Select"/> —— 纯函数（候选目录名由调用方枚举注入），
/// 不触碰文件系统，用例完全确定。
/// </summary>
public sealed class ActiveRuntimeFallbackTests
{
    [Test]
    public async Task Select_ActiveRuntimeAlreadySet_ReturnsNull()
    {
        // 已激活自建版本：尊重现状，绝不抢占。
        string? selected = ActiveRuntimeFallback.Select(
            activeRuntime: "0.1.4",
            borrowedEntryUsable: false,
            selfBuiltVersions: ["0.1.5-rc.1"]);

        await Assert.That(selected).IsNull();
    }

    [Test]
    public async Task Select_BorrowedEntryUsable_ReturnsNull()
    {
        // 借用安装仍可用：借用是默认形态（Q7-A），不自动切走。
        string? selected = ActiveRuntimeFallback.Select(
            activeRuntime: null,
            borrowedEntryUsable: true,
            selfBuiltVersions: ["0.1.5-rc.1"]);

        await Assert.That(selected).IsNull();
    }

    [Test]
    public async Task Select_NoSelfBuiltVersions_ReturnsNull()
    {
        // 无自建候选：留给首启自检弹「下载并安装」，这里无能为力。
        string? selected = ActiveRuntimeFallback.Select(
            activeRuntime: null,
            borrowedEntryUsable: false,
            selfBuiltVersions: []);

        await Assert.That(selected).IsNull();
    }

    [Test]
    public async Task Select_BorrowedDeadAndSingleSelfBuilt_SelectsIt()
    {
        string? selected = ActiveRuntimeFallback.Select(
            activeRuntime: null,
            borrowedEntryUsable: false,
            selfBuiltVersions: ["0.1.5-rc.1"]);

        await Assert.That(selected).IsEqualTo("0.1.5-rc.1");
    }

    [Test]
    public async Task Select_MultipleVersions_SelectsHighestNumeric()
    {
        // 字典序会误判（"0.1.9" > "0.1.10"）：必须按数值版本比较。
        string? selected = ActiveRuntimeFallback.Select(
            activeRuntime: null,
            borrowedEntryUsable: false,
            selfBuiltVersions: ["0.1.9", "0.1.10", "0.1.5-rc.1"]);

        await Assert.That(selected).IsEqualTo("0.1.10");
    }

    [Test]
    public async Task Select_SameNumericVersion_StableReleaseBeatsPrerelease()
    {
        // semver 约定：0.1.5 正式版高于 0.1.5-rc.1 预发布。
        string? selected = ActiveRuntimeFallback.Select(
            activeRuntime: null,
            borrowedEntryUsable: false,
            selfBuiltVersions: ["0.1.5-rc.1", "0.1.5"]);

        await Assert.That(selected).IsEqualTo("0.1.5");
    }

    [Test]
    public async Task Select_PrereleaseSegmentsComparedNumerically()
    {
        // 预发布段同样不能用字典序（"rc.2" > "rc.10" 是错的，独立评审发现）。
        string? selected = ActiveRuntimeFallback.Select(
            activeRuntime: null,
            borrowedEntryUsable: false,
            selfBuiltVersions: ["0.1.5-rc.2", "0.1.5-rc.10"]);

        await Assert.That(selected).IsEqualTo("0.1.5-rc.10");
    }

    [Test]
    public async Task Select_UnparseableVersions_FallsBackToOrdinalMax()
    {
        // 非版本形态目录名（异常残留）不得炸掉选择：序数最大者兜底，保证确定性。
        string? selected = ActiveRuntimeFallback.Select(
            activeRuntime: null,
            borrowedEntryUsable: false,
            selfBuiltVersions: ["broken-dir", "another-broken"]);

        await Assert.That(selected).IsEqualTo("broken-dir");
    }
}
