using DshDesktop.Infrastructure.Plugins;

namespace DshDesktop.Tests;

/// <summary>
/// ReparsePointMaterializer 测试。
///
/// 背景（2026-09-14 实机教训）：Profile 的 node_modules 里 overrides 包是悬空 symlink/junction
/// 时 DSH 启动期 resolveBundleDir 抛 cannot resolve profile bundle → Runtime ExitCode=1。
/// 安装/回滚入口调 <c>Materialize(nodeModulesDir, sourceNodeModulesDir: null)</c> 是无种子源的，
/// materializer 完全 no-op，pnpm 重置 symlink 后必留下悬空 → 问题复发。
///
/// 修复：未显式传种子源时，materializer 必须回退到默认 harness 种子路径查找真实内容。
/// 默认路径由 <c>DSH_DESKTOP_TEST_SEED_NODE_MODULES</c> 环境变量覆盖（仅测试使用），
/// 否则按 Windows Roaming 布局拼出 %APPDATA%\dsh-desktop\harness\profiles\web\node_modules。
///
/// ⚠️ 沙箱 host 限制：Directory.CreateSymbolicLink 不抛错但磁盘上不创建 entry（SAFE_DELETE
/// 同款策略），悬空 symlink 形态在单元测试里不可达 → 「悬空 symlink 自动实体化」链路
/// 无法在沙箱写红测试。仅守「happy path（无 dangling symlink）下 materializer no-op 不报错」
/// 以锁住 fallback wiring 不破坏既有 happy path。
/// </summary>
public sealed class ReparsePointMaterializerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dsh-test-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("DSH_DESKTOP_TEST_SEED_NODE_MODULES", null);
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>
    /// node_modules 内无重解析点时，materializer 必须完全 no-op：
    /// 不创建/不删除/不修改任何真实目录。锁住 fallback wiring 不破坏 happy path。
    /// </summary>
    [Test]
    public async Task Materialize_NoDanglingReparsePoint_IsNoOp()
    {
        // profile 端真实目录（无 symlink 无 junction）。
        string profileNodeModules = Path.Combine(_root, "profile", "node_modules");
        string profilePackage = Path.Combine(profileNodeModules, "dsh-context");
        Directory.CreateDirectory(profilePackage);
        File.WriteAllText(Path.Combine(profilePackage, "package.json"), "{\"name\":\"dsh-context\"}");

        // 种子端也存在（fallback 路径可读，但 happy path 下不应触碰）。
        string seedNodeModules = Path.Combine(_root, "seed", "node_modules");
        string seedPackage = Path.Combine(seedNodeModules, "other-package");
        Directory.CreateDirectory(seedPackage);
        Environment.SetEnvironmentVariable("DSH_DESKTOP_TEST_SEED_NODE_MODULES", seedNodeModules);

        ReparsePointMaterializer.Materialize(profileNodeModules, sourceNodeModulesDir: null);

        // happy path：目录与文件保持原状，未被 fallback 触碰。
        await Assert.That(File.Exists(Path.Combine(profilePackage, "package.json"))).IsTrue();
        await Assert.That(File.ReadAllText(Path.Combine(profilePackage, "package.json")))
            .Contains("\"name\":\"dsh-context\"");
        await Assert.That(Directory.GetFileSystemEntries(profileNodeModules).Length).IsEqualTo(1);
    }

    /// <summary>
    /// 显式传种子源时按显式源处理（即使 env var 也设了默认路径）。
    /// 这条守「显式优先于 fallback」的 wiring 不被反转。
    /// </summary>
    [Test]
    public async Task Materialize_ExplicitSourceTakesPrecedence_IsNoOp()
    {
        string profileNodeModules = Path.Combine(_root, "profile", "node_modules");
        string profilePackage = Path.Combine(profileNodeModules, "dsh-context");
        Directory.CreateDirectory(profilePackage);
        File.WriteAllText(Path.Combine(profilePackage, "package.json"), "{\"name\":\"explicit\"}");

        string explicitSeed = Path.Combine(_root, "explicit", "node_modules");
        Directory.CreateDirectory(Path.Combine(explicitSeed, "other"));

        string fallbackSeed = Path.Combine(_root, "fallback", "node_modules");
        Directory.CreateDirectory(Path.Combine(fallbackSeed, "other"));
        Environment.SetEnvironmentVariable("DSH_DESKTOP_TEST_SEED_NODE_MODULES", fallbackSeed);

        ReparsePointMaterializer.Materialize(profileNodeModules, explicitSeed);

        await Assert.That(File.ReadAllText(Path.Combine(profilePackage, "package.json")))
            .Contains("\"name\":\"explicit\"");
        await Assert.That(Directory.GetFileSystemEntries(profileNodeModules).Length).IsEqualTo(1);
    }
}
