using System.IO;
using DshDesktop.Infrastructure.Plugins;

namespace DshDesktop.Tests;

/// <summary>
/// ResolveRealDirectory 单元测试。
///
/// 该函数负责把"源侧条目（可能是真实目录，也可能是 junction/symlink）"解析为
/// 一个**不含重解析点的真实目录路径**；若无法解析（目标不存在 / 环 / 跳数超限）返回 null。
///
/// 沙箱 host 限制：无法创建真实 symlink/junction（CreateSymbolicLink 静默 no-op），
/// 故 link 跟随分支用**可注入的假 probe** 驱动，纯逻辑全可在沙箱验证；真实 FS 分支
/// （真实目录命中、路径不存在）用临时目录直接验证。
/// </summary>
public sealed class ResolveRealDirectoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dsh-resolve-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static Func<string, (bool exists, bool isDir, bool isReparse, string? linkTarget)> Probe(
        params (string path, bool exists, bool isDir, bool isReparse, string? linkTarget)[] table) => path =>
    {
        // Core 经 GetFullPath 解析出的是反斜杠绝对路径；夹具 key 用正斜杠，需归一化斜杠与大小写再比对。
        string Norm(string s) => s.Replace('/', '\\');
        string np = Norm(path);
        foreach (var row in table)
        {
            if (string.Equals(Norm(row.path), np, StringComparison.OrdinalIgnoreCase))
            {
                return (row.exists, row.isDir, row.isReparse, row.linkTarget);
            }
        }
        return (false, false, false, null);
    };

    /// <summary>源侧是真实目录（非重解析点）：直接返回该路径。</summary>
    [Test]
    public async Task ResolveRealDirectory_RealDirectory_ReturnsEntry()
    {
        string dir = Path.Combine(_root, "real");
        Directory.CreateDirectory(dir);

        string? result = ReparsePointMaterializer.ResolveRealDirectory(dir);

        await Assert.That(result).IsEqualTo(dir);
    }

    /// <summary>路径不存在：无源可复制，返回 null。</summary>
    [Test]
    public async Task ResolveRealDirectory_MissingPath_ReturnsNull()
    {
        string? result = ReparsePointMaterializer.ResolveRealDirectory(Path.Combine(_root, "nope"));

        await Assert.That(result).IsNull();
    }

    /// <summary>相对目标链接（../b）：相对链接自身所在目录解析为绝对路径后命中真实目录。</summary>
    [Test]
    public async Task ResolveRealDirectory_RelativeLink_ResolvesToRealTarget()
    {
        var probe = Probe(
            ("C:/a/link", true, true, true, "../b"),
            ("C:/b", true, true, false, null));

        string? result = ReparsePointMaterializer.ResolveRealDirectory("C:/a/link", probe);

        await Assert.That(result).IsEqualTo(Path.GetFullPath("C:/b"));
    }

    /// <summary>绝对目标链接：直接使用绝对 LinkTarget 命中真实目录。</summary>
    [Test]
    public async Task ResolveRealDirectory_AbsoluteLink_ResolvesToRealTarget()
    {
        var probe = Probe(
            ("C:/a/link", true, true, true, "D:/real"),
            ("D:/real", true, true, false, null));

        string? result = ReparsePointMaterializer.ResolveRealDirectory("C:/a/link", probe);

        await Assert.That(result).IsEqualTo(Path.GetFullPath("D:/real"));
    }

    /// <summary>链式链接（link1 → link2 → target）：逐跳跟随直到真实目录。</summary>
    [Test]
    public async Task ResolveRealDirectory_ChainedLink_FollowsToRealTarget()
    {
        var probe = Probe(
            ("C:/a/link1", true, true, true, "link2"),
            ("C:/a/link2", true, true, true, "target"),
            ("C:/a/target", true, true, false, null));

        string? result = ReparsePointMaterializer.ResolveRealDirectory("C:/a/link1", probe);

        await Assert.That(result).IsEqualTo(Path.GetFullPath("C:/a/target"));
    }

    /// <summary>自引用环（link → link）：有限跳数 + visited 集合防止死循环，返回 null。</summary>
    [Test]
    public async Task ResolveRealDirectory_SelfReferenceCycle_ReturnsNull()
    {
        var probe = Probe(
            ("C:/a/link", true, true, true, "link"));

        string? result = ReparsePointMaterializer.ResolveRealDirectory("C:/a/link", probe);

        await Assert.That(result).IsNull();
    }

    /// <summary>链式环（a → b → a）：返回 null 而非死循环。</summary>
    [Test]
    public async Task ResolveRealDirectory_ChainedCycle_ReturnsNull()
    {
        var probe = Probe(
            ("C:/a/link", true, true, true, "link2"),
            ("C:/a/link2", true, true, true, "link"));

        string? result = ReparsePointMaterializer.ResolveRealDirectory("C:/a/link", probe);

        await Assert.That(result).IsNull();
    }

    /// <summary>跳数上限：起始即为重解析点且 maxHops=0 时放弃，返回 null。</summary>
    [Test]
    public async Task ResolveRealDirectory_HopLimitExceeded_ReturnsNull()
    {
        var probe = Probe(
            ("C:/a/link", true, true, true, "deeper"));

        string? result = ReparsePointMaterializer.ResolveRealDirectory("C:/a/link", probe, maxHops: 0);

        await Assert.That(result).IsNull();
    }

    /// <summary>链接目标不存在（悬空）：返回 null（交由上层校验报错）。</summary>
    [Test]
    public async Task ResolveRealDirectory_DanglingLink_ReturnsNull()
    {
        var probe = Probe(
            ("C:/a/link", true, true, true, "missing"),
            ("C:/a/missing", false, false, false, null));

        string? result = ReparsePointMaterializer.ResolveRealDirectory("C:/a/link", probe);

        await Assert.That(result).IsNull();
    }
}
