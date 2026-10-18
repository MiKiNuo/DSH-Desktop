using DshDesktop.Application.Updates;
using DshDesktop.Domain.Updates;
using DshDesktop.Infrastructure.Updates;

namespace DshDesktop.Tests;

/// <summary>
/// Runtime 仓库测试（§36 side-by-side 列表：借用安装 + 自建版本的激活标记）。
/// 离线可测：用临时目录伪造包结构，不触 npm。
/// </summary>
public sealed class RuntimeRepositoryTests
{
    [Test]
    public async Task ListRuntimesAsync_SelfBuiltActive_MarksBorrowedInactive()
    {
        string root = Path.Combine(Path.GetTempPath(), "dsh-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            string borrowedEntry = CreateBorrowedInstall(root, "0.1.0-borrowed");
            string runtimeRoot = CreateSelfBuiltRuntime(root, "0.1.2");

            var repository = new RuntimeRepository(runtimeRoot, "node", null, borrowedEntry);
            IReadOnlyList<DshRuntimeInfo> runtimes = await repository.ListRuntimesAsync("0.1.2", CancellationToken.None);

            await Assert.That(runtimes.Count).IsEqualTo(2);
            await Assert.That(runtimes[0].IsBorrowed).IsTrue();
            await Assert.That(runtimes[0].Version).IsEqualTo("0.1.0-borrowed");
            await Assert.That(runtimes[0].IsActive).IsFalse();
            await Assert.That(runtimes[1].Version).IsEqualTo("0.1.2");
            await Assert.That(runtimes[1].IsActive).IsTrue();
            await Assert.That(runtimes[1].IsBorrowed).IsFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task ListRuntimesAsync_NoActiveRuntime_BorrowedIsActive()
    {
        string root = Path.Combine(Path.GetTempPath(), "dsh-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            string borrowedEntry = CreateBorrowedInstall(root, "0.1.0-borrowed");
            string runtimeRoot = CreateSelfBuiltRuntime(root, "0.1.2");

            var repository = new RuntimeRepository(runtimeRoot, "node", null, borrowedEntry);
            IReadOnlyList<DshRuntimeInfo> runtimes = await repository.ListRuntimesAsync(null, CancellationToken.None);

            await Assert.That(runtimes[0].IsActive).IsTrue();
            await Assert.That(runtimes[1].IsActive).IsFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task ListRuntimesAsync_BorrowedEntryEmpty_SkipsBorrowed()
    {
        // 借用安装被删除后 HealRuntimePaths 会把 dshEntryPath 清空（全新无借用环境同形态）：
        // 空路径不得让列表整体崩溃，只跳过借用条目（2026-09-17 更新中心 ArgumentNullException(path1) 回归）。
        string root = Path.Combine(Path.GetTempPath(), "dsh-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            string runtimeRoot = CreateSelfBuiltRuntime(root, "0.1.2");

            var repository = new RuntimeRepository(runtimeRoot, "node", null, string.Empty);
            IReadOnlyList<DshRuntimeInfo> runtimes = await repository.ListRuntimesAsync("0.1.2", CancellationToken.None);

            await Assert.That(runtimes.Count).IsEqualTo(1);
            await Assert.That(runtimes[0].IsBorrowed).IsFalse();
            await Assert.That(runtimes[0].Version).IsEqualTo("0.1.2");
            await Assert.That(runtimes[0].IsActive).IsTrue();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// 伪造借用安装：&lt;root&gt;/borrowed/package.json + lib/bin.js（入口的上级上级即包目录）。
    /// </summary>
    private static string CreateBorrowedInstall(string root, string version)
    {
        string packageDir = Path.Combine(root, "borrowed");
        Directory.CreateDirectory(Path.Combine(packageDir, "lib"));
        File.WriteAllText(Path.Combine(packageDir, "lib", "bin.js"), string.Empty);
        File.WriteAllText(Path.Combine(packageDir, "package.json"), $"{{\"version\":\"{version}\"}}");
        return Path.Combine(packageDir, "lib", "bin.js");
    }

    /// <summary>
    /// 伪造自建 Runtime：&lt;root&gt;/runtimes/&lt;version&gt;/node_modules/@deepseek-ai/dsh/package.json。
    /// </summary>
    private static string CreateSelfBuiltRuntime(string root, string version)
    {
        string runtimeRoot = Path.Combine(root, "runtimes");
        string dshDir = Path.Combine(runtimeRoot, version, "node_modules", "@deepseek-ai", "dsh");
        Directory.CreateDirectory(dshDir);
        File.WriteAllText(Path.Combine(dshDir, "package.json"), $"{{\"version\":\"{version}\"}}");
        return runtimeRoot;
    }

    /// <summary>
    /// GitHub 版本发现优先：命中后不得再走 npm dist-tag（ADR-0011 的核心修复——
    /// dist-tag 停在 0.2.0-rc.2 而 tag 已到 0.2.1-rc.2）。
    /// </summary>
    [Test]
    public async Task GetLatestVersionAsync_SourceResolves_PrefersItOverNpm()
    {
        string root = Path.Combine(Path.GetTempPath(), "dsh-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var source = new StubVersionSource { Result = "0.2.1-rc.2" };
            bool npmQueried = false;
            var repository = new RuntimeRepository(
                root, "node", null, string.Empty, source,
                (_, _) => { npmQueried = true; return Task.FromResult("0.2.0-rc.2"); });

            string version = await repository.GetLatestVersionAsync(
                RuntimeChannel.PrereleaseLiteral, CancellationToken.None);

            await Assert.That(version).IsEqualTo("0.2.1-rc.2");
            await Assert.That(npmQueried).IsFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>源不可用（限流 / 断网 / 仓库改名）时 fail-open 回退 dist-tag 查询。</summary>
    [Test]
    public async Task GetLatestVersionAsync_SourceUnavailable_FallsBackToNpm()
    {
        string root = Path.Combine(Path.GetTempPath(), "dsh-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            string? queriedTag = null;
            var repository = new RuntimeRepository(
                root, "node", null, string.Empty,
                new StubVersionSource { Result = null },
                (tag, _) => { queriedTag = tag; return Task.FromResult("0.2.1-rc.2"); });

            string version = await repository.GetLatestVersionAsync(
                RuntimeChannel.PrereleaseLiteral, CancellationToken.None);

            await Assert.That(version).IsEqualTo("0.2.1-rc.2");
            await Assert.That(queriedTag).IsEqualTo("next");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>旧 dist-tag 字面必须先归一化再进源与回退查询，否则迁移后的用户仍停在旧通道上。</summary>
    [Test]
    public async Task GetLatestVersionAsync_LegacyChannelLiteral_NormalizesToPrerelease()
    {
        string root = Path.Combine(Path.GetTempPath(), "dsh-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var source = new StubVersionSource { Result = null };
            string? queriedTag = null;
            var repository = new RuntimeRepository(
                root, "node", null, string.Empty, source,
                (tag, _) => { queriedTag = tag; return Task.FromResult("0.2.1-rc.2"); });

            await repository.GetLatestVersionAsync("latest", CancellationToken.None);

            await Assert.That(source.ObservedChannel).IsEqualTo(RuntimeReleaseChannel.Prerelease);
            await Assert.That(queriedTag).IsEqualTo("next");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>显式 stable 档回退到 latest（而非默认档的 next）。</summary>
    [Test]
    public async Task GetLatestVersionAsync_StableChannel_FallsBackToLatestDistTag()
    {
        string root = Path.Combine(Path.GetTempPath(), "dsh-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            string? queriedTag = null;
            var repository = new RuntimeRepository(
                root, "node", null, string.Empty,
                new StubVersionSource { Result = null },
                (tag, _) => { queriedTag = tag; return Task.FromResult("0.2.0-rc.2"); });

            await repository.GetLatestVersionAsync(RuntimeChannel.StableLiteral, CancellationToken.None);

            await Assert.That(queriedTag).IsEqualTo("latest");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>版本发现源桩：记录收到的通道，按预设返回版本或 null（null = 源不可用）。</summary>
    private sealed class StubVersionSource : IRuntimeVersionSource
    {
        /// <summary>待返回的版本；null 表示源不可用。</summary>
        public string? Result { get; init; }

        /// <summary>最近一次查询收到的通道。</summary>
        public RuntimeReleaseChannel? ObservedChannel { get; private set; }

        public Task<string?> TryGetLatestAsync(
            RuntimeReleaseChannel channel,
            CancellationToken cancellationToken)
        {
            ObservedChannel = channel;
            return Task.FromResult(Result);
        }
    }
}
