using System.Text.Json.Nodes;
using DshDesktop.Infrastructure.Plugins;

namespace DshDesktop.Tests;

/// <summary>
/// P1：剥离指向不存在目标的 pnpm.overrides。
/// profile 的 overrides 指向 link:../.generations/live/&lt;genId&gt;/...，而 .generations 永不随
/// profile 复制，目标必悬空；任何 pnpm 操作会按 overrides 把已 materialize 的真实依赖重建为悬空
/// junction → DSH 启动 cannot resolve profile bundle → ExitCode=1（"更新插件"打坏 profile 的机制）。
/// 删除安全：同名包已在 dependencies 中、健康 profile 无 pnpm 字段、DSH 不读 overrides。
/// 夹具用真实 package.json + 临时目录。
/// </summary>
public sealed class ProfileManifestFixupsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dsh-fixup-" + Guid.NewGuid().ToString("N"));

    private string ProfileDir => Path.Combine(_root, "profile");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>无 pnpm 字段：不动文件（不无谓改写时间戳，逐字节语义不变）。</summary>
    [Test]
    public async Task StripDanglingOverrides_NoPnpmField_LeavesFileUntouched()
    {
        Directory.CreateDirectory(ProfileDir);
        const string pkg = """
            {
              "name": "web",
              "private": true,
              "dependencies": { "dshmarket": "1.45.1" },
              "dsh": { "profile": { "bundles": ["dshmarket"] } }
            }
            """;
        File.WriteAllText(Path.Combine(ProfileDir, "package.json"), pkg);

        ProfileManifestFixups.StripDanglingOverrides(ProfileDir);

        await Assert.That(await File.ReadAllTextAsync(Path.Combine(ProfileDir, "package.json"))).IsEqualTo(pkg);
    }

    /// <summary>有 overrides（且 pnpm 还有其它键）：仅移除 overrides，pnpm 字段保留，其它字段不变。</summary>
    [Test]
    public async Task StripDanglingOverrides_RemovesOverridesOnly()
    {
        Directory.CreateDirectory(ProfileDir);
        File.WriteAllText(Path.Combine(ProfileDir, "package.json"), ProfileWithOverrides());

        ProfileManifestFixups.StripDanglingOverrides(ProfileDir);

        JsonNode? node = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(ProfileDir, "package.json")));
        await Assert.That(node["pnpm"]?["overrides"]).IsNull();
        await Assert.That(node["pnpm"]?["strict-peer-dependencies"]?.GetValue<bool>()).IsTrue(); // pnpm 保留
        // 其它字段语义不变：dependencies 与 dsh 含同名包，必须逐字节语义不变。
        await Assert.That(node["dependencies"]?["dshmarket"]?.GetValue<string>()).IsEqualTo("1.45.1");
        await Assert.That(node["dsh"]?["profile"]?["bundles"]).IsNotNull();
    }

    /// <summary>pnpm 仅含 overrides：移除后 pnpm 变空 → 整个 pnpm 字段一并移除（与健康 profile 形态一致）。</summary>
    [Test]
    public async Task StripDanglingOverrides_EmptyPnpmRemoved()
    {
        Directory.CreateDirectory(ProfileDir);
        const string pkg = """
            {
              "name": "web",
              "pnpm": { "overrides": { "dshmarket": "link:../.generations/live/dshmarket+1.45.1/node_modules/dshmarket" } }
            }
            """;
        File.WriteAllText(Path.Combine(ProfileDir, "package.json"), pkg);

        ProfileManifestFixups.StripDanglingOverrides(ProfileDir);

        JsonNode? node = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(ProfileDir, "package.json")));
        await Assert.That(node["pnpm"]).IsNull(); // 整个 pnpm 字段移除
        await Assert.That(node["name"]?.GetValue<string>()).IsEqualTo("web");
    }

    /// <summary>pnpm 存在但无 overrides：无需改动，不动文件。</summary>
    [Test]
    public async Task StripDanglingOverrides_PnpmWithoutOverrides_Unchanged()
    {
        Directory.CreateDirectory(ProfileDir);
        const string pkg = """
            {
              "name": "web",
              "pnpm": { "strict-peer-dependencies": true }
            }
            """;
        File.WriteAllText(Path.Combine(ProfileDir, "package.json"), pkg);

        ProfileManifestFixups.StripDanglingOverrides(ProfileDir);

        await Assert.That(await File.ReadAllTextAsync(Path.Combine(ProfileDir, "package.json"))).IsEqualTo(pkg);
    }

    /// <summary>幂等：重复调用无副作用；无 pnpm 字段后再次调用也不改写。</summary>
    [Test]
    public async Task StripDanglingOverrides_Idempotent()
    {
        Directory.CreateDirectory(ProfileDir);
        File.WriteAllText(Path.Combine(ProfileDir, "package.json"), ProfileWithOverrides());

        ProfileManifestFixups.StripDanglingOverrides(ProfileDir);
        string first = await File.ReadAllTextAsync(Path.Combine(ProfileDir, "package.json"));
        ProfileManifestFixups.StripDanglingOverrides(ProfileDir);
        string second = await File.ReadAllTextAsync(Path.Combine(ProfileDir, "package.json"));

        await Assert.That(second).IsEqualTo(first);
        JsonNode? node = JsonNode.Parse(second);
        await Assert.That(node["pnpm"]?["overrides"]).IsNull(); // 幂等：overrides 已被剥离、且不再改写
        await Assert.That(node["pnpm"]?["strict-peer-dependencies"]?.GetValue<bool>()).IsTrue();
    }

    /// <summary>profile 目录存在但无 package.json：静默返回（幂等，不抛）。</summary>
    [Test]
    public async Task StripDanglingOverrides_MissingFile_SilentlyReturns()
    {
        Directory.CreateDirectory(ProfileDir);

        ProfileManifestFixups.StripDanglingOverrides(ProfileDir);

        await Assert.That(Directory.Exists(ProfileDir)).IsTrue();
    }

    private static string ProfileWithOverrides() => """
        {
          "name": "web",
          "private": true,
          "dependencies": {
            "@openviking/dsh-memory-plugin": "^0.3.0",
            "dsh-context": "0.49.1",
            "dsh-myrules": "0.1.1",
            "dsh-plugin-subscriptions": "0.8.0",
            "dshmarket": "1.45.1",
            "@vectorize-io/hindsight-coding-agents": "0.5.3"
          },
          "dsh": { "profile": { "bundles": [] }, "desktop": { "generationProjection": {} } },
          "pnpm": {
            "strict-peer-dependencies": true,
            "overrides": {
              "dsh-context": "link:../.generations/live/dsh-context+0.49.1/node_modules/dsh-context",
              "dshmarket": "link:../.generations/live/dshmarket+1.45.1/node_modules/dshmarket"
            }
          }
        }
        """;
}
