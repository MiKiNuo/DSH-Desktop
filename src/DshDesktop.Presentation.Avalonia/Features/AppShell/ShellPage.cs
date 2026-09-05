namespace DshDesktop.Presentation.Avalonia.Features.AppShell;

/// <summary>
/// 表示应用壳页面枚举。
/// </summary>
public enum ShellPage
{
    /// <summary>Runtime 页。</summary>
    Runtime,

    /// <summary>Dashboard 页（Runtime 投影 + 快捷操作，§26）。</summary>
    Dashboard,

    /// <summary>Workbench 页。</summary>
    Workbench,

    /// <summary>Diagnostics 页。</summary>
    Diagnostics,

    /// <summary>Plugins 页。</summary>
    Plugins,

    /// <summary>Updates 页。</summary>
    Updates,

    /// <summary>Settings 页。</summary>
    Settings,
}
