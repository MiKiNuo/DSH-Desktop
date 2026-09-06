using DshDesktop.Domain.Plugins;

namespace DshDesktop.Presentation.Avalonia.Features.Plugins;

/// <summary>
/// 表示 Plugins 页表格行投影（Phase 8 Issue 06，原型 plugins section 170-186 行）：
/// 把 <see cref="PluginInfo"/> 映射为状态 tag / 操作列的展示语义，纯函数可测。
/// </summary>
/// <param name="Info">源插件信息。</param>
/// <param name="IsUpdatable">是否有可用更新（BindSiblingState 自 UpdatesStore.PluginUpdates 投影，Phase 8 评审 F3）。</param>
public sealed record PluginRow(PluginInfo Info, bool IsUpdatable = false)
{
    /// <summary>获取插件包名。</summary>
    public string Name => Info.Name;

    /// <summary>获取已安装版本。</summary>
    public string Version => Info.Version;

    /// <summary>获取插件描述（无则空串）。</summary>
    public string Description => Info.Description;

    /// <summary>获取状态 tag 文案（优先级：○ 已禁用 info &gt; ↻ 可更新 warn &gt; ● 正常 绿）。</summary>
    public string StatusText => !Info.Enabled ? "○ 已禁用" : IsUpdatable ? "↻ 可更新" : "● 正常";

    /// <summary>获取状态 tag 是否 info 配色（已禁用）。</summary>
    public bool StatusIsInfo => !Info.Enabled;

    /// <summary>获取状态 tag 是否 warn 配色（已启用且有可用更新）。</summary>
    public bool StatusIsWarn => Info.Enabled && IsUpdatable;

    /// <summary>获取是否显示"更新"操作（有可用更新的第三方插件，走 UpdatePlugin 链路）。</summary>
    public bool ShowUpdate => !Info.IsCore && Info.Enabled && IsUpdatable;

    /// <summary>获取是否显示"启用"操作（已禁用的第三方插件）。</summary>
    public bool ShowEnable => !Info.IsCore && !Info.Enabled;

    /// <summary>获取是否显示"管理"操作组（禁用/卸载；启用的第三方插件）。</summary>
    public bool ShowManage => !Info.IsCore && Info.Enabled;

    /// <summary>
    /// 获取加载耗时展示文本：DSH 无插件加载耗时数据，列保留为原型视觉恒 "—"
    /// （Phase 8 spec Round 2 Q2）。
    /// </summary>
    public string LoadTime => "—";
}

/// <summary>
/// 表示 Plugins 页搜索过滤与头部统计投影（客户端过滤插件名，对应原型 pluginSearch 逻辑）。
/// </summary>
public static class PluginRowProjection
{
    /// <summary>
    /// 把插件清单投影为表格行；query 为空/空白返回全部，否则按包名不区分大小写包含过滤。
    /// </summary>
    /// <param name="plugins">源插件清单。</param>
    /// <param name="query">搜索词。</param>
    /// <param name="updatableNames">可更新插件名集（UpdatesStore 投影）；null 视为无可更新。</param>
    /// <returns>过滤后的行投影。</returns>
    public static IReadOnlyList<PluginRow> Filter(
        IReadOnlyList<PluginInfo> plugins,
        string? query,
        IReadOnlySet<string>? updatableNames = null)
    {
        ArgumentNullException.ThrowIfNull(plugins);

        string keyword = query?.Trim() ?? string.Empty;
        return keyword.Length == 0
            ? plugins.Select(p => new PluginRow(p, updatableNames?.Contains(p.Name) == true)).ToArray()
            : plugins
                .Where(p => p.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .Select(p => new PluginRow(p, updatableNames?.Contains(p.Name) == true))
                .ToArray();
    }

    /// <summary>
    /// 格式化卡片头部总数 tag（原型 "N Plugins"，统计总数而非过滤后数量）。
    /// </summary>
    /// <param name="total">插件总数。</param>
    /// <returns>tag 文案。</returns>
    public static string CountText(int total) => $"{total} Plugins";
}
