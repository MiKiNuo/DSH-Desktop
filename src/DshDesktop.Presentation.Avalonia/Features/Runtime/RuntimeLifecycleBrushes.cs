using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using DshDesktop.Domain.Runtime;

namespace DshDesktop.Presentation.Avalonia.Features.Runtime;

/// <summary>
/// 表示 Runtime 生命周期状态点的共享颜色映射（侧栏指示与 Runtime 页阶段指示同色系）。
///
/// 取值策略（重要）：
/// 调用方 <see cref="MainWindow.ApplyIndicators"/> 在**构造期**调用本类，此时 View 尚未挂到
/// 逻辑树，<c>control.TryFindResource</c> 走不到应用字典（历史上实测崩溃，文件头原注释已说明）。
/// 因此这里不直接依赖某棵逻辑树，而是优先用 <c>Application.Current.FindResource</c> 从
/// 应用级主题字典取键（与 DshTheme.axaml 完全一致，单一事实来源）；若取不到（极端场景），
/// 回退到下面与主题键**完全相同**的十六进制常量，保证两处永远是同一个数。
/// 修改任一颜色时，必须同步 DshTheme.axaml 中对应的 Dsh*Brush 键与下方常量。
/// </summary>
public static class RuntimeLifecycleBrushes
{
    // 与 DshTheme.axaml 主题键保持一致的回退常量（仅当字典取不到时使用）。
    private const string OkHex = "#3fbf8f";        // DshOkBrush
    private const string WarnHex = "#e0a93f";      // DshWarnBrush
    private const string ErrHex = "#ec625d";        // DshErrBrush
    private const string Text3Hex = "#6d737c";      // DshText3Brush（停止/待定灰）
    private const string Text2Hex = "#a1a7b0";      // DshText2Brush（次要文本）
    private const string OkSoftHex = "#1C3FBF8F";   // DshOkSoftBrush（就绪图标底色）
    private const string WarnSoftHex = "#1CE0A93F"; // DshWarnSoftBrush
    private const string ErrSoftHex = "#1CEC625D";  // DshErrSoftBrush

    /// <summary>从应用字典取键，取不到则回退到同值常量（两处永远是同一个数）。</summary>
    private static IBrush Resolve(string key, string fallbackHex)
    {
        // 必须 global:: 限定：本文件命名空间是 DshDesktop.Presentation.Avalonia.*，
        // 裸写 Avalonia 会命中 DshDesktop.Presentation.Avalonia 这个命名空间而非 Avalonia 程序集。
        // 用 TryFindResource 而非 FindResource：后者找不到键时抛异常，会让回退分支变成死代码。
        if (global::Avalonia.Application.Current is { } app
            && app.TryFindResource(key, out var found)
            && found is IBrush brush)
        {
            return brush;
        }

        return new SolidColorBrush(Color.Parse(fallbackHex));
    }

    /// <summary>已停止 / 待定（灰）。</summary>
    public static readonly IBrush Stopped = Resolve("DshText3Brush", Text3Hex);

    /// <summary>过渡中（黄）：Starting / Stopping / Recovering。</summary>
    public static readonly IBrush Transition = Resolve("DshWarnBrush", WarnHex);

    /// <summary>运行中 / 健康 / 阶段完成（绿）。</summary>
    public static readonly IBrush Running = Resolve("DshOkBrush", OkHex);

    /// <summary>失败 / 无响应（红）。</summary>
    public static readonly IBrush Failed = Resolve("DshErrBrush", ErrHex);

    /// <summary>就绪图标半透明底色：运行中（绿）。</summary>
    public static readonly IBrush TintRunning = Resolve("DshOkSoftBrush", OkSoftHex);

    /// <summary>就绪图标半透明底色：过渡中（黄）。</summary>
    public static readonly IBrush TintTransition = Resolve("DshWarnSoftBrush", WarnSoftHex);

    /// <summary>就绪图标半透明底色：失败（红）。</summary>
    public static readonly IBrush TintFailed = Resolve("DshErrSoftBrush", ErrSoftHex);

    /// <summary>就绪图标半透明底色：已停止 / 待定（灰，使用中性 surface 键，无软灰键）。</summary>
    public static readonly IBrush TintStopped = Resolve("DshSurface3Brush", "#1d2024");

    /// <summary>次要文本（灰）。</summary>
    public static readonly IBrush Muted = Resolve("DshText2Brush", Text2Hex);

    /// <summary>
    /// 按生命周期取状态点画刷。图标键与底色的合并映射见
    /// <see cref="RuntimeLifecycleProjection"/>（就绪图标三要素的唯一出处）。
    /// </summary>
    public static IBrush For(RuntimeLifecycle lifecycle)
    {
        return lifecycle switch
        {
            RuntimeLifecycle.Running => Running,
            RuntimeLifecycle.Starting or RuntimeLifecycle.Stopping or RuntimeLifecycle.Recovering => Transition,
            RuntimeLifecycle.Failed => Failed,
            _ => Stopped,
        };
    }
}
