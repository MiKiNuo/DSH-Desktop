using System.IO;
using System.Text.Json.Nodes;

namespace DshDesktop.Infrastructure.Plugins;

/// <summary>
/// Profile 清单（package.json）的外科式修正：剥离指向不存在目标的 pnpm overrides。
/// 不新增公共 API（internal），所有调用点都在本仓会执行 pnpm 操作的入口。
/// </summary>
internal static class ProfileManifestFixups
{
    private static readonly System.Text.Json.JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>
    /// 移除 profile 的 <c>package.json</c> 中 <c>pnpm.overrides</c>，避免任何 pnpm 操作把已
    /// materialize 的真实依赖目录重建为悬空 junction。
    ///
    /// <b>根因</b>：profile 的 pnpm overrides 指向 <c>link:../.generations/live/&lt;genId&gt;/...</c>，
    /// 而 <c>.generations</c> 永不随 profile 复制（ProfileSeeder.CopyProfileAsync 的 robocopy 带
    /// <c>/XD .generations</c>，且它在种子端是复制根的兄弟目录），目标必悬空。pnpm add/install 按
    /// overrides 把这 5 个包重建为悬空链接、覆盖真实目录 → DSH 启动 cannot resolve profile bundle
    /// → ExitCode=1（"更新插件"打坏 profile 的直接机制）。
    ///
    /// <b>删除安全</b>：① 这 6 个包同时也都在 <c>dependencies</c> 中，删 overrides 后 pnpm 仍按
    /// dependencies 正常解析，不会当孤儿清除；② 另一个完全健康的 profile（web）根本无 <c>pnpm</c>
    /// 字段，4 个包是真实目录、启动正常——即已验证可用形态；③ DSH 运行时与本仓 .NET 侧均不读不写
    /// overrides / generationProjection（全仓零命中）。
    /// </summary>
    /// <param name="profileDir">profile 目录（含 package.json）。</param>
    internal static void StripDanglingOverrides(string profileDir)
    {
        string manifestPath = Path.Combine(profileDir, "package.json");
        if (!File.Exists(manifestPath))
        {
            return; // 幂等：文件缺失静默返回，不抛。
        }

        JsonNode? root = JsonNode.Parse(File.ReadAllText(manifestPath));
        if (root is not JsonObject manifest)
        {
            return;
        }

        if (manifest["pnpm"] is not JsonObject pnpm)
        {
            return; // 无 pnpm 字段：不动文件（不无谓改写时间戳）。
        }

        if (pnpm["overrides"] is null)
        {
            return; // 有 pnpm 但无 overrides：无需改动。
        }

        pnpm.Remove("overrides");
        if (pnpm.Count == 0)
        {
            manifest.Remove("pnpm"); // pnpm 变空 → 整体移除，与健康 profile 形态一致。
        }

        File.WriteAllText(manifestPath, manifest.ToJsonString(Indented));
    }
}
