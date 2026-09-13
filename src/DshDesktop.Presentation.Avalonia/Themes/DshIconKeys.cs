namespace DshDesktop.Presentation.Avalonia.Themes;

/// <summary>
/// <c>DshIcons.axaml</c> 中图标资源键的编译期常量。
///
/// 消费方式：<c>RuntimeLifecycleProjection.For</c> 在运行时选键，
/// View 侧用 <c>FindResource(常量)</c> 取 <c>StreamGeometry</c>。
/// 此前这些键是裸字符串——拼错即静默无图标（<c>FindResource</c> 找不到键会抛异常）。
///
/// 注意：图标是**运行时求值**的，静态差集分析会把
/// <see cref="Alert"/> / <see cref="Power"/> / <see cref="Refresh"/>
/// 误判为"从未被引用"（它们由投影选出，不出现在任何 axaml 字面量里）。
/// 清理孤儿键前务必以本类为准，勿按静态差集删除。
/// </summary>
public static class DshIconKeys
{
    public const string Dashboard = "IconDashboard";
    public const string Terminal = "IconTerminal";
    public const string Package = "IconPackage";
    public const string Cpu = "IconCpu";
    public const string Refresh = "IconRefresh";
    public const string Activity = "IconActivity";
    public const string Sliders = "IconSliders";
    public const string Search = "IconSearch";

    public const string ChevronLeft = "IconChevronLeft";
    public const string ChevronRight = "IconChevronRight";
    public const string ChevronDown = "IconChevronDown";

    public const string Plus = "IconPlus";
    public const string Play = "IconPlay";
    public const string Stop = "IconStop";

    public const string Download = "IconDownload";
    public const string Folder = "IconFolder";
    public const string Close = "IconClose";
    public const string Check = "IconCheck";

    public const string Alert = "IconAlert";
    public const string Info = "IconInfo";
    public const string More = "IconMore";

    public const string External = "IconExternal";
    public const string Trash = "IconTrash";
    public const string Document = "IconDocument";
    public const string Shield = "IconShield";
    public const string Zap = "IconZap";
    public const string Clock = "IconClock";
    public const string Drive = "IconDrive";
    public const string Power = "IconPower";
}
