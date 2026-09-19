using DshDesktop.Infrastructure.Plugins;

namespace DshDesktop.Infrastructure.Runtime;

/// <summary>
/// 从「DSH 进程在就绪前退出」失败消息（内含 stderr 末尾）解析肇事插件包名，供组合根
/// 触发事务化升级自愈（2026-09-19 v0.1.4 实机：旧版 dshmarket 静态 import 了
/// Runtime ≥0.1.2-alpha.1 已删除的 installSettingsSection → ESM 链接期 SyntaxError
/// → 启动必崩；核心插件无更新入口、SafeMode 不减载插件 ⇒ 永不自愈）。
/// 只认 "does not provide an export" 特征（与 <see cref="RuntimeStderrHints"/> 同源）；
/// 按序尝试两条模式：① loader entry 行括号包名；② profile 内 node_modules 路径
/// （带 profiles/ 段以排除 Runtime 自身依赖的同名路径；stderr 尾部 4096 截断可能切掉①）。
/// in-box bundle（Runtime 安装体提供，profile 装不了）与无法定位 ⇒ null（交人工排查，不误导）。
/// 纯字符串扫描（同 RewriteVirtualStoreDir 风格），不引入正则。
/// </summary>
public static class IncompatiblePluginCrashProbe
{
    private const string Signature = "does not provide an export";
    private const string LoaderEntryAnchor = "failed to import loader entry ";

    /// <summary>识别失败消息中的「命名导出缺失」崩溃并解析肇事插件包名；无法定位返回 null。</summary>
    public static string? TryParseOffender(string startFailureMessage)
    {
        if (!startFailureMessage.Contains(Signature, StringComparison.Ordinal))
        {
            return null;
        }

        string? offender = ParseLoaderEntry(startFailureMessage) ?? ParseProfileNodeModulesPath(startFailureMessage);
        return offender is null || ProfileBundleProbe.IsInBoxBundle(offender) ? null : offender;
    }

    // "failed to import loader entry dsh-market (dshmarket)" ⇒ 取行内最后一对括号内的包名。
    // 锚点含 "import"，不会误中同栈的 "failed to apply loader entry include (cordis:include)"。
    // 括号必须与锚点同行：stderr 尾部 4096 截断可能把包名括号切掉，跨行找 '(' 会误吃
    // 堆栈帧里的文件路径（如 "(file:///.../cordis-plugin-loader/lib/index.js:522:39)"）。
    private static string? ParseLoaderEntry(string message)
    {
        int anchor = message.IndexOf(LoaderEntryAnchor, StringComparison.Ordinal);
        if (anchor < 0)
        {
            return null;
        }

        int searchFrom = anchor + LoaderEntryAnchor.Length;
        int open = message.IndexOf('(', searchFrom);
        int close = open < 0 ? -1 : message.IndexOf(')', open + 1);
        int lineEnd = message.IndexOf('\n', searchFrom);
        if (open < 0 || close <= open + 1 || (lineEnd >= 0 && lineEnd < open))
        {
            return null;
        }

        string name = message[(open + 1)..close].Trim();
        return name.Length == 0 || name.Any(char.IsWhiteSpace) ? null : name;
    }

    // profile 插件路径 "profiles/<profile>/node_modules/<pkg>/"（支持 @scope/pkg 两段式）；
    // profiles/ 段把 profile 插件与 Runtime 自身 node_modules（runtime/dsh/<ver>/...）区分开。
    private static string? ParseProfileNodeModulesPath(string message)
    {
        const string marker = "/node_modules/";
        int profiles = message.IndexOf("profiles/", StringComparison.Ordinal);
        int nodeModules = profiles < 0
            ? -1
            : message.IndexOf(marker, profiles, StringComparison.Ordinal);
        if (nodeModules < 0)
        {
            return null;
        }

        string rest = message[(nodeModules + marker.Length)..];
        string first = TakeSegment(rest);
        if (first.Length == 0)
        {
            return null;
        }

        // @scope/pkg 两段式：首段以 @ 开头则再取一段。
        if (first.StartsWith('@'))
        {
            string second = TakeSegment(rest[first.Length..].TrimStart('/', '\\'));
            return second.Length == 0 ? null : $"{first}/{second}";
        }

        return first;
    }

    /// <summary>取路径首段（遇 / \ 或行尾为止）；空串表示无有效段。</summary>
    private static string TakeSegment(string text)
    {
        int end = text.IndexOfAny(['/', '\\', '\r', '\n']);
        return end < 0 ? text : text[..end];
    }
}
