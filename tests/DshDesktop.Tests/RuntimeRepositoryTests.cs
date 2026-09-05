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
}
