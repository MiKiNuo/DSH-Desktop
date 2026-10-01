namespace DshDesktop.Infrastructure.Runtime;

/// <summary>
/// 从 DSH **启动成功**的 stdout 里识别「有 plugin entry 未激活」，并解析肇事插件包名。
/// </summary>
/// <remarks>
/// 与 <see cref="IncompatiblePluginCrashProbe"/> 的分工是「失败 vs 降级」：
/// 后者只在启动抛异常时被调用（插件树加载失败 ⇒ 进程起不来）；而本探针处理的是
/// **进程起来了、HTTP 也通，但插件没激活**这一类故障，它不产生启动失败，
/// 因此既不进自愈链路，也不会让 Runtime 判成 Failed。
///
/// 实机现场（2026-09-28，dsh 0.1.7-rc.2 × dsh-myrules@0.1.1）：0.1.7-rc.2 的
/// dsh-typert-loader 新增硬校验（strict codec 必须有 create() 工厂），而 dsh-myrules 是手写
/// typert manifest、codec 缺 create() ⇒ 校验抛错 ⇒ **整个 typert-loader 插件**激活失败
/// ⇒ app-boot 只打 WARN「1 entry did not activate」。后果远比这条 WARN 严重：cordis 的
/// Service 会把 <c>ctx.effect</c> 归属到调用方 fiber，loader 激活失败即连带撤回它注册的
/// 全部 strict 描述符，而 DescriptorStore.hasSeen 仍为 true ⇒ dsh-api-gateway 对所有端点抛
/// gateway/definition-unavailable（SRC 兜底被禁）⇒ 工作台的 WebView 整页空白。
///
/// 解析策略：**纯字符串扫描，不用正则**（同 RewriteVirtualStoreDir / IncompatiblePluginCrashProbe 风格）。
/// 肇事者取自 ` invocation "&lt;pkg&gt;#..."` 子句里的包名，而**不取**同块首行
/// `typert-loader (@deepseek-ai/dsh-typert-loader): AggregateError...` 的括号包名——那是**报错方**
/// （loader 自己），拿它去提示用户会让人禁用错的插件，比不给名字更糟。
/// </remarks>
internal static class PluginActivationWarningProbe
{
    /// <summary>
    /// dsh-app-boot 的告警行锚点。刻意**不含数量名词**：dsh 会按件数写成
    /// `1 entry did not activate` / `2 entries did not activate`，只取后半句可同时覆盖单复数。
    /// </summary>
    private const string Anchor = "did not activate";

    /// <summary>告警诊断块里点名肇事插件的子句锚点。</summary>
    private const string InvocationAnchor = "invocation \"";

    /// <summary>
    /// 识别启动输出中的「未激活」特征并解析肇事插件名。
    /// </summary>
    /// <param name="startupOutput">DSH 启动期 stdout（尾部缓冲即可）。</param>
    /// <returns>
    /// 未命中特征 ⇒ <c>null</c>；命中 ⇒ 去重并保持出现顺序的插件名集合，
    /// **可能为空集合**（有降级但解析不出名字，调用方应回退到"未定位"文案，不得当作"无降级"）。
    /// </returns>
    internal static IReadOnlyList<string>? TryParse(string startupOutput)
    {
        if (string.IsNullOrWhiteSpace(startupOutput) ||
            !startupOutput.Contains(Anchor, StringComparison.Ordinal))
        {
            return null;
        }

        List<string> offenders = [];
        foreach (string line in startupOutput.Split('\n'))
        {
            string? name = ParseInvocationPackage(line);
            if (name is not null && !offenders.Contains(name, StringComparer.Ordinal))
            {
                offenders.Add(name);
            }
        }

        return offenders;
    }

    /// <summary>
    /// 从一行里取 `invocation "&lt;包名&gt;#&lt;方法&gt;"` 的包名；无该子句或名字不合法返回 null。
    /// </summary>
    private static string? ParseInvocationPackage(string line)
    {
        int anchor = line.IndexOf(InvocationAnchor, StringComparison.Ordinal);
        if (anchor < 0)
        {
            return null;
        }

        // 锚点末尾即 invocation id 的首字符位置，取到下一个引号为止。
        int start = anchor + InvocationAnchor.Length;
        int close = line.IndexOf('"', start);
        if (close < 0)
        {
            return null;
        }

        string invocationId = line[start..close];
        int hash = invocationId.IndexOf('#');
        string name = (hash < 0 ? invocationId : invocationId[..hash]).Trim();

        // 名字必须像包名：留 `@scope/pkg` 的斜杠，但拒空白与反斜杠（后者是路径，取错会误导用户）。
        return name.Length == 0 || name.Any(char.IsWhiteSpace) || name.Contains('\\') ? null : name;
    }
}
