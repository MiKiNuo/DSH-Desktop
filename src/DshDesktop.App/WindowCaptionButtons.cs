using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace DshDesktop.App;

/// <summary>
/// 顶栏右侧避让量：运行时实测 caption 按钮组总宽。
/// ⚠️ 机制（2026-09-15 源码 + 截图像素取证修正；此前记为「DWM 系统绘制」是**误判**）：
/// 按钮组是 **Avalonia 自绘**的 —— MainWindow 的 ExtendClientAreaToDecorationsHint=true
/// + WindowDecorations=Full 会让 Avalonia 创建 WindowDrawnDecorations（TitleBar 部件），
/// 由 Fluent 主题模板绘制标题栏文字与 4 个按钮（全屏/最小化/最大化/关闭，各 45 DIP + 间距 2 DIP）。
/// 其中「全屏」按钮已在 App.axaml 隐藏（用户决策），故实际需避让的是 3 个按钮 ≈ 139 DIP
/// （最小化 / 最大化 / 关闭全部保留——标准窗口功能）。
/// 之前两次「右侧动作被遮挡」的真因是隐藏前那第 4 个按钮（组内最左的「全屏」）：组宽 186 DIP 比当时
/// 的避让量（按三按钮估算 146 DIP）多出 40 DIP，多出的部分正好落在最左那个 45 DIP 宽的按钮上，
/// 于是它压住了顶栏右侧内容——而不是当时推断的 DPI 缩放 /「DWM 按钮更宽」。
/// 为什么仍实测而不写死 DIP：此实现已在实机验证可用，且 DPI 变化时自动跟随；写死像素的两次实机回归
/// 记录见 MEMORY.md，不宜回退。
/// 为什么有下限：GetSystemMetricsForDpi(SM_CXSIZE) 系统性偏小于自绘按钮实际宽度（≈36 vs ≈45 DIP），
/// 纯实测会比已验证的避让宽度更窄，故取 max(实测, 46)。
/// 非 Windows 平台全部 no-op（macOS caption 按钮在左上，不占右上）。
/// 注：本类不再做任何窗口样式手术。2026-09-14 曾在此移除 WS_MAXIMIZEBOX 以「禁用最大化」，2026-09-15 删除：
/// 自绘按钮的启用/可见性取自 Window.CanMaximize，动样式位既隐藏不了也置灰不了按钮，却会连带让
/// 「双击顶栏最大化」失效（用户要的是保留最大化）。
/// </summary>
internal static class WindowCaptionButtons
{
    /// <summary>需避让的自绘 caption 按钮数：min/max/close（全屏按钮见 App.axaml 隐藏）。</summary>
    private const int CaptionButtonCount = 3;

    private const double CaptionSlackDips = 8;
    private const int SmCxSize = 30;
    private const double MinCaptionButtonWidthDips = 46;

    /// <summary>实测 caption 按钮组（min/max/close）总宽（DIP，含少量视觉余量）；非 Windows 返回 0。</summary>
    public static double MeasureWidthDips(Window window)
    {
        if (!OperatingSystem.IsWindows())
        {
            return 0;
        }

        double scaling = window.RenderScaling;
        uint dpi = (uint)Math.Max(1, Math.Round(96.0 * scaling));
        int perButtonPx = GetSystemMetricsForDpi(SmCxSize, dpi);
        // SM_CXSIZE 偏小于自绘按钮实际宽度（≈36 vs ≈45 DIP，见类注释），取下限兜底；
        // 文本缩放等使按钮更宽的场景仍由实测值覆盖。
        double perButtonDips = Math.Max(perButtonPx / scaling, MinCaptionButtonWidthDips);
        return (perButtonDips * CaptionButtonCount) + CaptionSlackDips;
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int nIndex, uint dpi);
}
