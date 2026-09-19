using DshDesktop.Domain.Plugins;

namespace DshDesktop.Presentation.Avalonia.Features.AppShell;

/// <summary>
/// 插件事务终态 toast 的操作动词映射。同一套安装事务被安装/更新/卸载/启用/禁用
/// 五个入口复用，文案按种类区分（避免点「卸载」却报「安装」）。
/// 文案集中在此以便单测，MainWindow 只按结果消费（与 ConfirmDialogText 同约定）。
/// </summary>
public static class PluginOperationText
{
    /// <summary>操作种类对应的动词（壳 toast：「插件 X {动词}完成 / {动词}失败：原因」）。</summary>
    public static string Verb(PluginOperationKind kind) => kind switch
    {
        PluginOperationKind.Install => "安装",
        PluginOperationKind.Update => "更新",
        PluginOperationKind.Uninstall => "卸载",
        PluginOperationKind.Enable => "启用",
        PluginOperationKind.Disable => "禁用",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}
