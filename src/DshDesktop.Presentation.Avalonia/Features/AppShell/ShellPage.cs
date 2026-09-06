namespace DshDesktop.Presentation.Avalonia.Features.AppShell;

/// <summary>
/// 表示应用壳页面枚举（Phase 8 Issue 02：顺序照原型侧栏导航：概览 → 工作台 → 插件 →
/// 运行环境 → 更新 → 诊断 → 设置）。
/// </summary>
public enum ShellPage
{
    /// <summary>概览页（默认页，原型 dashboard）。</summary>
    Dashboard,

    /// <summary>DSH 工作台页（官方 Web UI · NativeWebView）。</summary>
    Workbench,

    /// <summary>插件管理页。</summary>
    Plugins,

    /// <summary>运行环境页。</summary>
    Runtime,

    /// <summary>更新中心页。</summary>
    Updates,

    /// <summary>诊断中心页。</summary>
    Diagnostics,

    /// <summary>设置页。</summary>
    Settings,
}
