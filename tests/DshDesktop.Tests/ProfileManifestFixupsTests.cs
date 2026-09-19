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

    /// <summary>
    /// 代际投影残留剥离：dsh.desktop.generationProjection 是旧 Electron harness 的代际模型元数据，
    /// 复制到我们环境后 .generations 必缺席；残留会让 pnpm-runner 在市场操作时把投影插件隔离出
    /// dependencies 视图，市场遂走代际路径删除实目录（2026-09-17 实机：更新后 5 插件「未安装」）。
    /// 剥离后 dsh.profile（bundles 等）必须原样保留。
    /// </summary>
    [Test]
    public async Task StripGenerationProjection_RemovesProjection_KeepsProfile()
    {
        Directory.CreateDirectory(ProfileDir);
        File.WriteAllText(Path.Combine(ProfileDir, "package.json"), ProfileWithProjection());

        ProfileManifestFixups.StripGenerationProjection(ProfileDir);

        JsonNode? node = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(ProfileDir, "package.json")));
        await Assert.That(node["dsh"]?["desktop"]).IsNull(); // desktop 仅剩 generationProjection → 整层移除
        await Assert.That(node["dsh"]?["profile"]?["bundles"]).IsNotNull(); // dsh.profile 保留
        await Assert.That(node["dependencies"]?["dshmarket"]?.GetValue<string>()).IsEqualTo("1.45.1");
    }

    /// <summary>dsh.desktop 除 generationProjection 外还有其它键：仅移除 projection，desktop 保留。</summary>
    [Test]
    public async Task StripGenerationProjection_DesktopWithOtherKeys_KeepsDesktop()
    {
        Directory.CreateDirectory(ProfileDir);
        const string pkg = """
            {
              "name": "web",
              "dsh": {
                "profile": { "bundles": ["dshmarket"] },
                "desktop": { "generationProjection": { "version": 1 }, "futureKey": true }
              }
            }
            """;
        File.WriteAllText(Path.Combine(ProfileDir, "package.json"), pkg);

        ProfileManifestFixups.StripGenerationProjection(ProfileDir);

        JsonNode? node = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(ProfileDir, "package.json")));
        await Assert.That(node["dsh"]?["desktop"]?["generationProjection"]).IsNull();
        await Assert.That(node["dsh"]?["desktop"]?["futureKey"]?.GetValue<bool>()).IsTrue();
    }

    /// <summary>无 dsh.desktop 字段：不动文件（不无谓改写时间戳，逐字节语义不变）。</summary>
    [Test]
    public async Task StripGenerationProjection_NoDesktopField_LeavesFileUntouched()
    {
        Directory.CreateDirectory(ProfileDir);
        const string pkg = """
            {
              "name": "web",
              "private": true,
              "dsh": { "profile": { "bundles": ["dshmarket"] } }
            }
            """;
        File.WriteAllText(Path.Combine(ProfileDir, "package.json"), pkg);

        ProfileManifestFixups.StripGenerationProjection(ProfileDir);

        await Assert.That(await File.ReadAllTextAsync(Path.Combine(ProfileDir, "package.json"))).IsEqualTo(pkg);
    }

    /// <summary>幂等：重复调用无副作用。</summary>
    [Test]
    public async Task StripGenerationProjection_Idempotent()
    {
        Directory.CreateDirectory(ProfileDir);
        File.WriteAllText(Path.Combine(ProfileDir, "package.json"), ProfileWithProjection());

        ProfileManifestFixups.StripGenerationProjection(ProfileDir);
        string first = await File.ReadAllTextAsync(Path.Combine(ProfileDir, "package.json"));
        ProfileManifestFixups.StripGenerationProjection(ProfileDir);
        string second = await File.ReadAllTextAsync(Path.Combine(ProfileDir, "package.json"));

        await Assert.That(second).IsEqualTo(first);
        await Assert.That(JsonNode.Parse(second)["dsh"]?["desktop"]).IsNull();
    }

    /// <summary>profile 目录存在但无 package.json：静默返回（幂等，不抛）。</summary>
    [Test]
    public async Task StripGenerationProjection_MissingFile_SilentlyReturns()
    {
        Directory.CreateDirectory(ProfileDir);

        ProfileManifestFixups.StripGenerationProjection(ProfileDir);

        await Assert.That(Directory.Exists(ProfileDir)).IsTrue();
    }

    private static string ProfileWithProjection() => """
        {
          "name": "web",
          "private": true,
          "dependencies": { "dshmarket": "1.45.1" },
          "dsh": {
            "profile": { "bundles": ["dshmarket"] },
            "desktop": {
              "generationProjection": {
                "version": 1,
                "plugins": { "dshmarket": { "generationId": "dshmarket+1.45.1+24ae4d06b2b5" } }
              }
            }
          }
        }
        """;

    // ---- BOM 剥离（2026-09-19 实机：种子复制来的 profile 中某 bundle 的 package.json 带
    // UTF-8 BOM，DSH loadProfileDirectory 裸 JSON.parse 抛 SyntaxError（乱码 "锘?"）→
    // Runtime 就绪前退出 ExitCode=1，连续失败进 SafeMode；.NET 侧 File.ReadAllText 解码时
    // 自动剥 BOM 所以本仓无感，必须按原始字节判定）----

    /// <summary>profile 清单带 BOM：剥离 BOM，其余字节不变。</summary>
    [Test]
    public async Task StripUtf8ByteOrderMarks_ManifestWithBom_StripsBomPreservingBytes()
    {
        Directory.CreateDirectory(ProfileDir);
        const string pkg = "{\n  \"name\": \"web\"\n}\n";
        string manifestPath = Path.Combine(ProfileDir, "package.json");
        File.WriteAllText(manifestPath, pkg, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        ProfileManifestFixups.StripUtf8ByteOrderMarks(ProfileDir);

        byte[] bytes = await File.ReadAllBytesAsync(manifestPath);
        await Assert.That(bytes[0]).IsEqualTo((byte)'{');
        await Assert.That(System.Text.Encoding.UTF8.GetString(bytes)).IsEqualTo(pkg);
    }

    /// <summary>清单声明的 bundle 的 package.json 带 BOM：一并剥离（DSH 按 bundles 逐个 JSON.parse）。</summary>
    [Test]
    public async Task StripUtf8ByteOrderMarks_DeclaredBundleWithBom_StripsIt()
    {
        Directory.CreateDirectory(ProfileDir);
        File.WriteAllText(Path.Combine(ProfileDir, "package.json"),
            """{ "dsh": { "profile": { "bundles": ["@scope/dsh-foo"] } } }""");
        string bundleManifest = Path.Combine(ProfileDir, "node_modules", "@scope/dsh-foo", "package.json");
        Directory.CreateDirectory(Path.GetDirectoryName(bundleManifest)!);
        const string bundlePkg = "{\n  \"name\": \"@scope/dsh-foo\"\n}\n";
        File.WriteAllText(bundleManifest, bundlePkg, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        ProfileManifestFixups.StripUtf8ByteOrderMarks(ProfileDir);

        byte[] bytes = await File.ReadAllBytesAsync(bundleManifest);
        await Assert.That(bytes[0]).IsEqualTo((byte)'{');
        await Assert.That(System.Text.Encoding.UTF8.GetString(bytes)).IsEqualTo(bundlePkg);
    }

    /// <summary>全部无 BOM：逐字节不动（不无谓改写时间戳）。</summary>
    [Test]
    public async Task StripUtf8ByteOrderMarks_NoBom_LeavesBytesUntouched()
    {
        Directory.CreateDirectory(ProfileDir);
        const string pkg = "{\n  \"name\": \"web\",\n  \"dsh\": { \"profile\": { \"bundles\": [\"dsh-foo\"] } }\n}\n";
        string manifestPath = Path.Combine(ProfileDir, "package.json");
        File.WriteAllText(manifestPath, pkg);
        string bundleManifest = Path.Combine(ProfileDir, "node_modules", "dsh-foo", "package.json");
        Directory.CreateDirectory(Path.GetDirectoryName(bundleManifest)!);
        const string bundlePkg = "{\n  \"name\": \"dsh-foo\"\n}\n";
        File.WriteAllText(bundleManifest, bundlePkg);

        ProfileManifestFixups.StripUtf8ByteOrderMarks(ProfileDir);

        await Assert.That(await File.ReadAllTextAsync(manifestPath)).IsEqualTo(pkg);
        await Assert.That(await File.ReadAllTextAsync(bundleManifest)).IsEqualTo(bundlePkg);
    }

    /// <summary>profile 目录/文件缺失：静默返回不抛（幂等，与兄弟修正方法同约定）。</summary>
    [Test]
    public async Task StripUtf8ByteOrderMarks_MissingFiles_DoesNotThrow()
    {
        ProfileManifestFixups.StripUtf8ByteOrderMarks(Path.Combine(_root, "nope"));

        Directory.CreateDirectory(ProfileDir);
        File.WriteAllText(Path.Combine(ProfileDir, "package.json"),
            """{ "dsh": { "profile": { "bundles": ["dsh-ghost"] } } }"""); // 声明了 bundle 但磁盘无实目录
        ProfileManifestFixups.StripUtf8ByteOrderMarks(ProfileDir);

        await Task.CompletedTask; // 不抛异常即通过
    }

    /// <summary>NormalizeToFlatModel 入口同样剥离 BOM：所有 pnpm/启动入口都走它，不能漏。</summary>
    [Test]
    public async Task NormalizeToFlatModel_AlsoStripsBom()
    {
        Directory.CreateDirectory(ProfileDir);
        string manifestPath = Path.Combine(ProfileDir, "package.json");
        File.WriteAllText(manifestPath, "{\n  \"name\": \"web\"\n}\n",
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        ProfileManifestFixups.NormalizeToFlatModel(ProfileDir);

        byte[] bytes = await File.ReadAllBytesAsync(manifestPath);
        await Assert.That(bytes[0]).IsEqualTo((byte)'{');
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
