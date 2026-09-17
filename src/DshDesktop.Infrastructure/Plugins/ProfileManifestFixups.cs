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
    /// 把 profile 清单归一化到扁平 pnpm 模型：剥离代际投影残留与悬空 overrides。
    /// 两者同源（旧 Electron harness 的代际模型在我们环境无 .generations 支撑），必须成对清理——
    /// 所有会对 profile 执行 pnpm 操作或启动 Runtime 的入口都调用本方法，而非只调其中一半。
    /// </summary>
    /// <param name="profileDir">profile 目录（含 package.json）。</param>
    internal static void NormalizeToFlatModel(string profileDir)
    {
        StripGenerationProjection(profileDir);
        StripDanglingOverrides(profileDir);
    }

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

    /// <summary>
    /// 移除 profile 的 <c>package.json</c> 中 <c>dsh.desktop.generationProjection</c>（旧 Electron
    /// harness 的代际投影模型元数据）；<c>dsh.desktop</c> 因此变空时整层移除，<c>dsh.profile</c>
    /// （bundles 等）与其余字段不动。
    ///
    /// <b>根因</b>：projection 登记的插件被 .desktop-bin 的 pnpm-runner 在每次 pnpm 运行前从
    /// dependencies/overrides 视图临时摘除，工作台市场遂无法经 pnpm 更新它们，转而走代际路径
    /// 改写 node_modules——而我们环境永无 <c>.generations</c>（ProfileSeeder 复制时排除），
    /// 结果是实目录被删、bundle 不可解析 → DSH 启动 cannot resolve profile bundle → ExitCode=1，
    /// 连续失败触发自动安全模式全量禁用（2026-09-17 实机现场）。
    ///
    /// <b>删除安全</b>：DSH 运行时（0.1.5-rc.1）与本仓 .NET 侧均不读不写该字段；剥离后市场对
    /// 这些插件退化为普通 pnpm 依赖，与已验证可用的健康 profile（无 pnpm/desktop 字段）同形态。
    /// </summary>
    /// <param name="profileDir">profile 目录（含 package.json）。</param>
    internal static void StripGenerationProjection(string profileDir)
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

        if (manifest["dsh"] is not JsonObject dsh
            || dsh["desktop"] is not JsonObject desktop
            || desktop["generationProjection"] is null)
        {
            return; // 无投影残留：不动文件（不无谓改写时间戳）。
        }

        desktop.Remove("generationProjection");
        if (desktop.Count == 0)
        {
            dsh.Remove("desktop"); // desktop 变空 → 整层移除，与健康 profile 形态一致。
        }

        File.WriteAllText(manifestPath, manifest.ToJsonString(Indented));
    }
}
