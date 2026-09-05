using System.Runtime.InteropServices;

namespace DshDesktop.Platform.Windows.Notifications;

/// <summary>
/// 表示进程图标源（Phase 7 Issue 04）：托盘（Avalonia TrayIcon）与气泡通知复用同一进程图标，
/// 保证通知区域单图标视觉一致。优先进程自身图标，回退系统应用图标（IDI_APPLICATION）。
/// </summary>
public static unsafe partial class ProcessIcon
{
    private const int IdiApplication = 32512;
    private const int IconSize = 32;
    private const uint DiNormal = 0x0003;
    private const uint DibRgbColors = 0;

    /// <summary>
    /// 加载进程图标 HICON。
    /// </summary>
    /// <returns>图标句柄与是否归调用方所有（所有则需 <see cref="DestroyIcon"/>）。</returns>
    public static (IntPtr Icon, bool Owned) LoadHandle()
    {
        // 优先进程自身图标（ExtractIconEx 返回的句柄归调用方，须 DestroyIcon）。
        if (Environment.ProcessPath is { } processPath
            && ExtractIconExW(processPath, 0, out IntPtr large, out _, 1) > 0
            && large != IntPtr.Zero)
        {
            return (large, true);
        }

        // 回退：系统应用图标（共享资源，不可销毁）。
        return (LoadIconW(IntPtr.Zero, (IntPtr)IdiApplication), false);
    }

    /// <summary>
    /// 把进程图标转成 ICO 字节流（供 Avalonia <c>WindowIcon</c> 使用）。
    /// HICON → 32bpp DIB（DrawIconEx）→ 拼 ICONDIR/ICONDIRENTRY/BITMAPINFOHEADER + XOR + 空 AND 掩码。
    /// </summary>
    /// <returns>单帧 32x32 ICO 字节。</returns>
    public static byte[] LoadIcoBytes()
    {
        (IntPtr icon, bool owned) = LoadHandle();
        try
        {
            return ToIcoBytes(icon);
        }
        finally
        {
            if (owned && icon != IntPtr.Zero)
            {
                _ = DestroyIcon(icon);
            }
        }
    }

    private static byte[] ToIcoBytes(IntPtr icon)
    {
        const int xorBytes = IconSize * IconSize * 4;
        const int andStride = (IconSize + 31) / 32 * 4;
        const int andBytes = andStride * IconSize;
        const int imageBytes = 40 + xorBytes + andBytes;

        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr dc = CreateCompatibleDC(screenDc);
        BITMAPINFOHEADER header = NewDibHeader(-IconSize); // 负高 = 顶向下 DIB
        IntPtr dib = CreateDIBSection(dc, ref header, DibRgbColors, out IntPtr bits, IntPtr.Zero, 0);
        IntPtr previous = SelectObject(dc, dib);
        _ = DrawIconEx(dc, 0, 0, icon, IconSize, IconSize, 0, IntPtr.Zero, DiNormal);
        _ = SelectObject(dc, previous);

        using MemoryStream stream = new(imageBytes + 22);
        using BinaryWriter writer = new(stream);

        // ICONDIR + ICONDIRENTRY（单帧）。
        writer.Write((ushort)0); // reserved
        writer.Write((ushort)1); // type = icon
        writer.Write((ushort)1); // count
        writer.Write((byte)IconSize);
        writer.Write((byte)IconSize);
        writer.Write((byte)0); // colors
        writer.Write((byte)0); // reserved
        writer.Write((ushort)1); // planes
        writer.Write((ushort)32); // bit count
        writer.Write(imageBytes);
        writer.Write(22); // image offset

        // ICO 内嵌位图为底向上（biHeight = 2*h 含 AND 掩码），像素须翻转行序。
        BITMAPINFOHEADER icoHeader = NewDibHeader(IconSize * 2);
        writer.Write(icoHeader.biSize);
        writer.Write(icoHeader.biWidth);
        writer.Write(icoHeader.biHeight);
        writer.Write(icoHeader.biPlanes);
        writer.Write(icoHeader.biBitCount);
        writer.Write(icoHeader.biCompression);
        writer.Write(icoHeader.biSizeImage);
        writer.Write(icoHeader.biXPelsPerMeter);
        writer.Write(icoHeader.biYPelsPerMeter);
        writer.Write(icoHeader.biClrUsed);
        writer.Write(icoHeader.biClrImportant);
        for (int row = IconSize - 1; row >= 0; row--)
        {
            writer.Write(new ReadOnlySpan<byte>((byte*)bits + (row * IconSize * 4), IconSize * 4));
        }

        writer.Write(new byte[andBytes]); // 空 AND 掩码 = 全不透明由 alpha 决定

        DeleteObject(dib);
        DeleteDC(dc);
        ReleaseDC(IntPtr.Zero, screenDc);
        return stream.ToArray();
    }

    private static BITMAPINFOHEADER NewDibHeader(int height)
    {
        return new BITMAPINFOHEADER
        {
            biSize = (uint)sizeof(BITMAPINFOHEADER),
            biWidth = IconSize,
            biHeight = height,
            biPlanes = 1,
            biBitCount = 32,
            biCompression = 0, // BI_RGB
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [LibraryImport("shell32.dll")]
    private static partial uint ExtractIconExW(
        [MarshalAs(UnmanagedType.LPWStr)] string lpszFile,
        int nIconIndex,
        out IntPtr phiconLarge,
        out IntPtr phiconSmall,
        uint nIcons);

    [LibraryImport("user32.dll")]
    private static partial IntPtr LoadIconW(IntPtr hInstance, IntPtr lpIconName);

    /// <summary>销毁 <see cref="LoadHandle"/> 返回的自有图标句柄（Win32 DestroyIcon 直通）。</summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(IntPtr hIcon);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetDC(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr CreateCompatibleDC(IntPtr hdc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(IntPtr hdc);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr CreateDIBSection(
        IntPtr hdc,
        ref BITMAPINFOHEADER pbmi,
        uint usage,
        out IntPtr ppvBits,
        IntPtr hSection,
        uint offset);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(IntPtr ho);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DrawIconEx(
        IntPtr hdc,
        int xLeft,
        int yTop,
        IntPtr hIcon,
        int cxWidth,
        int cyWidth,
        uint istepIfAniCur,
        IntPtr hbrFlickerFreeDraw,
        uint diFlags);
}
