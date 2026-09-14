using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using DshDesktop.Domain.Runtime;
using DshDesktop.Presentation.Avalonia.Themes;

namespace DshDesktop.Presentation.Avalonia.Features.Runtime;

/// <summary>
/// 表示 Runtime 生命周期状态点的共享颜色映射（顶部导航状态点与 Runtime 页阶段指示同色系）。
///
/// 取值策略（重要）：
/// 调用方 <see cref="MainWindow.ApplyIndicators"/> 在**构造期**调用本类，此时 View 尚未挂到
/// 逻辑树，<c>control.TryFindResource</c> 走不到应用字典（历史上实测崩溃，文件头原注释已说明）。
/// 因此这里不直接依赖某棵逻辑树，而是优先用 <c>Application.Current.TryFindResource</c> 从
/// 应用级主题字典取键（与 DshColors.Dark/Light.axaml 完全一致，单一事实来源）；若取不到（极端场景），
/// 回退到下面与**深色**主题键完全相同的十六进制常量，保证两处永远是同一个数。
/// 全部画刷成员都是计算属性、不经类加载固化，故主题切换后无需额外刷新即可取到新主题的颜色
/// （唯一的缓存是按色值复用的回退画刷，见 <c>FallbackBrushes</c>，只在取不到应用字典时命中）。
/// 修改任一颜色时，必须同步 DshColors 两套文件里对应的 Dsh*Brush 键与下方常量。
/// </summary>
public static class RuntimeLifecycleBrushes
{
    // 与 DshColors.Dark.axaml 主题键保持一致的回退常量（仅当字典取不到时使用）。
    // 键名走 DshThemeKeys 常量（拼错即编译失败）；色值由 ThemeKeyTests 直接断言
    // **解析后的画刷颜色**与主题字典逐字节相等——失同步会失败，不再只靠上方注释提醒。
    private const string OkHex = "#3fbf8f";        // DshOkBrush
    private const string WarnHex = "#e0a93f";      // DshWarnBrush
    private const string ErrHex = "#ec625d";        // DshErrBrush
    private const string Text3Hex = "#6d737c";      // DshText3Brush（停止/待定灰）
    private const string Text2Hex = "#a1a7b0";      // DshText2Brush（次要文本）
    private const string OkSoftHex = "#1C3FBF8F";   // DshOkSoftBrush（就绪图标底色）
    private const string WarnSoftHex = "#1CE0A93F"; // DshWarnSoftBrush
    private const string ErrSoftHex = "#1CEC625D";  // DshErrSoftBrush
    private const string Surface3Hex = "#1d2024";   // DshSurface3Brush

    /// <summary>
    /// 从应用主题字典取键，取不到则回退到同值常量（两处永远是同一个数）。
    ///
    /// **每次访问都重新解析**：主题切换后 <c>TryFindResource</c> 会命中另一套字典，取到的画刷随之更新。
    /// 若像早先那样缓存进 <c>static readonly</c> 字段，切到亮色后状态点与图标会停在深色值上
    /// （字段初始化只在类首次使用时跑一次）。
    /// 回退分支按色值做实例缓存，使测试进程（无 <c>Application.Current</c>）里的多次解析
    /// 仍返回同一实例——<c>RuntimeLifecycleProjectionTests</c> 断言的正是「引用同一」。
    /// </summary>
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

        // 记录本次实际使用的回退色，供 ThemeKeyTests 校验（测试进程读不到画刷颜色：
        // Avalonia 属性的 get 会触发 Dispatcher.VerifyAccess，非 UI 线程直接抛异常）。
        ResolvedFallbackHex[key] = fallbackHex;

        // 必须是并发字典：测试进程并行跑用例，多个线程会同时首次解析同一个键；
        // 普通 Dictionary 下后来者会覆盖先前的条目，先拿到的调用方就与后拿到的引用不同了
        // （RuntimeLifecycleProjectionTests 的「同输入同引用」纯度断言正是这么挂的）。
        return FallbackBrushes.GetOrAdd(
            fallbackHex, static hex => new SolidColorBrush(Color.Parse(hex)));
    }

    /// <summary>回退画刷的实例缓存（键 = 十六进制色值），保证引用稳定。</summary>
    private static readonly ConcurrentDictionary<string, IBrush> FallbackBrushes =
        new(StringComparer.Ordinal);

    /// <summary>
    /// <see cref="Resolve"/> 实际用过的「主题键 → 回退色」。由 <c>ThemeKeyTests</c> 读取并与
    /// <c>DshColors.Dark.axaml</c> 比对——记的是**真实实参**，故把 <c>Resolve(键, 别的常量)</c>
    /// 写错也会被抓到。这是唯一能绕过 Avalonia 线程约束读到有效值的途径。
    /// 并发字典：测试并行跑用例时会有多线程同时写入。
    /// </summary>
    internal static readonly ConcurrentDictionary<string, string> ResolvedFallbackHex =
        new(StringComparer.Ordinal);

    /// <summary>已停止 / 待定（灰）。</summary>
    public static IBrush Stopped => Resolve(DshThemeKeys.Text3Brush, Text3Hex);

    /// <summary>过渡中（黄）：Starting / Stopping / Recovering。</summary>
    public static IBrush Transition => Resolve(DshThemeKeys.WarnBrush, WarnHex);

    /// <summary>运行中 / 健康 / 阶段完成（绿）。</summary>
    public static IBrush Running => Resolve(DshThemeKeys.OkBrush, OkHex);

    /// <summary>失败 / 无响应（红）。</summary>
    public static IBrush Failed => Resolve(DshThemeKeys.ErrBrush, ErrHex);

    /// <summary>就绪图标半透明底色：运行中（绿）。</summary>
    public static IBrush TintRunning => Resolve(DshThemeKeys.OkSoftBrush, OkSoftHex);

    /// <summary>就绪图标半透明底色：过渡中（黄）。</summary>
    public static IBrush TintTransition => Resolve(DshThemeKeys.WarnSoftBrush, WarnSoftHex);

    /// <summary>就绪图标半透明底色：失败（红）。</summary>
    public static IBrush TintFailed => Resolve(DshThemeKeys.ErrSoftBrush, ErrSoftHex);

    /// <summary>就绪图标半透明底色：已停止 / 待定（灰，使用中性 surface 键，无软灰键）。</summary>
    public static IBrush TintStopped => Resolve(DshThemeKeys.Surface3Brush, Surface3Hex);

    /// <summary>次要文本（灰）。</summary>
    public static IBrush Muted => Resolve(DshThemeKeys.Text2Brush, Text2Hex);

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
