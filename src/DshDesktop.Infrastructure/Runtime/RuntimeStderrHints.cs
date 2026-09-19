namespace DshDesktop.Infrastructure.Runtime;

/// <summary>
/// 把 DSH 进程 stderr 末尾的已知崩溃特征翻译为可操作的排查提示，附加在启动失败消息上。
/// 纯函数、无状态：只认两类已实机确认的特征（2026-09-19）：
/// ① 插件 package.json 带 UTF-8 BOM → DSH loadProfileDirectory 裸 JSON.parse 抛
///    "is not valid JSON"（GBK 控制台显示乱码 "锘?"）；
/// ② 旧版插件静态 import 了已被 Runtime 删除的命名导出 → ESM 模块求值期
///    "does not provide an export"（dsh ≥ 0.1.2-alpha.1 删了 installSettingsSection 等）。
/// 均无法自动恢复出可用 Runtime，给出人工排查方向；未知特征不附提示（不误导）。
/// </summary>
internal static class RuntimeStderrHints
{
    /// <summary>识别 stderr 尾部文本的已知崩溃特征，返回中文排查提示；未知/为空返回 null。</summary>
    internal static string? Describe(string stderrTail)
    {
        if (stderrTail.Length == 0)
        {
            return null;
        }

        if (stderrTail.Contains("does not provide an export", StringComparison.Ordinal))
        {
            // 肇事者可能是核心插件（dshmarket，2026-09-19 v0.1.4 实机）：旧文案的「卸载」
            // 对核心插件是死路（UI 只读）；自愈链（IncompatiblePluginCrashProbe → 事务化
            // 升级）会先尝试自动升级，人工兜底只剩「更新」。
            return "提示：疑似插件与当前 Runtime 版本不兼容（插件依赖的模块导出已变更）。"
                + "应用将尝试自动升级肇事插件并重启；若仍失败，请到「插件」页手动更新该插件。";
        }

        if (stderrTail.Contains("is not valid JSON", StringComparison.Ordinal))
        {
            return "提示：疑似插件的 package.json 含 UTF-8 BOM 或 JSON 已损坏。"
                + "请卸载最近安装的插件后重试；若刚手工编辑过插件文件，请以「UTF-8（无 BOM）」重新保存。";
        }

        return null;
    }
}
