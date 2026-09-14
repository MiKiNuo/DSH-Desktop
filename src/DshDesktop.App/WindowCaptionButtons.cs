using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace DshDesktop.App;

/// <summary>
/// Win32 caption 按钮区集成：运行时实测 caption 按钮总宽（供顶栏动态避让）+ 禁用最大化。
/// 为什么实测：Window.WindowDecorationMargin 在 Full + Windows 下实机取值不可靠（2026-09-14 首次回归，
/// 按钮被压到只剩图标）；写死 DIP 值又无法覆盖按钮实际更宽的环境（同日二次回归）。
/// 为什么有下限：GetSystemMetricsForDpi(SM_CXSIZE) 系统性偏小于 DWM 实际绘制宽度（≈36 vs ≈46 DIP），
/// 纯实测会比实机验证过的 140px 还窄，故取 max(实测, 46)。
/// P/Invoke 用 *PtrW 入口，仅适用 64 位进程（本产品仅发布 win-x64）。
/// 非 Windows 平台全部 no-op（macOS caption 按钮在左上，不占右上）。
/// </summary>
internal static class WindowCaptionButtons
{
    private const int CaptionButtonCount = 3;
    private const double CaptionSlackDips = 8;
    private const int SmCxSize = 30;
    private const double MinCaptionButtonWidthDips = 46;
    private const int GwlStyle = -16;
    private const int WS_MAXIMIZEBOX = 0x00010000;
    private const uint SwpNomove = 0x0002;
    private const uint SwpNosize = 0x0001;
    private const uint SwpNozorder = 0x0004;
    private const uint SwpNoactivate = 0x0010;
    private const uint SwpFramechanged = 0x0020;

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
        // SM_CXSIZE 偏小于 DWM 实际按钮宽（≈36 vs ≈46 DIP，见类注释），取下限兜底；
        // 文本缩放等使按钮更宽的场景仍由实测值覆盖。
        double perButtonDips = Math.Max(perButtonPx / scaling, MinCaptionButtonWidthDips);
        return (perButtonDips * CaptionButtonCount) + CaptionSlackDips;
    }

    /// <summary>禁用最大化（按钮变灰 + 双击标题栏不再最大化），保留边框拉伸。</summary>
    public static void DisableMaximizeBox(Window window)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        nint hwnd = window.TryGetPlatformHandle()?.Handle ?? nint.Zero;
        if (hwnd == nint.Zero)
        {
            return;
        }

        nint style = GetWindowLongPtr(hwnd, GwlStyle);
        _ = SetWindowLongPtr(hwnd, GwlStyle, style & ~WS_MAXIMIZEBOX);
        _ = SetWindowPos(
            hwnd,
            nint.Zero,
            0, 0, 0, 0,
            SwpNomove | SwpNosize | SwpNozorder | SwpNoactivate | SwpFramechanged);
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int nIndex, uint dpi);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
}
