using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DshDesktop.Application.Notifications;

namespace DshDesktop.Platform.Windows.Notifications;

/// <summary>
/// 表示 Windows 气泡通知服务（Phase 7 Issue 03；Issue 04 双图标合并为非常驻）：P/Invoke
/// <c>Shell_NotifyIcon</c>（NIIF_*），零新依赖、AOT 友好（LibraryImport 源生成 + UnmanagedCallersOnly 函数指针，无运行时封送）。
/// 常驻托盘图标由 Avalonia TrayIcon 承载（组合根接线）；本服务仅在发气泡时 NIM_ADD，
/// 气泡结束（NIN_BALLOONHIDE/TIMEOUT/USERCLICK）即 NIM_DELETE，避免与 TrayIcon 双图标并存。
/// 气泡点击回调经自建的 message-only 隐藏窗口回收，命中后仅回调外部委托（组合根负责置前主窗口）。
/// </summary>
public sealed unsafe partial class BalloonNotificationService : INotificationService, IDisposable
{
    // Shell_NotifyIcon 消息与标志（win32 文档）。
    private const uint NimAdd = 0x00000000;
    private const uint NimModify = 0x00000001;
    private const uint NimDelete = 0x00000002;
    private const uint NimSetVersion = 0x00000004;

    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint NifInfo = 0x00000010;

    private const uint NiifInfo = 0x00000001;
    private const uint NiifNoSound = 0x00000010;

    private const uint WmApp = 0x8000;
    private const uint BalloonCallbackMessage = WmApp + 0x4E;
    private const uint NinBalloonHide = 0x00000403; // WM_USER + 3
    private const uint NinBalloonTimeout = 0x00000404; // WM_USER + 4
    private const uint NinBalloonUserClick = 0x00000405; // WM_USER + 5
    private const uint NotifyIconVersion4 = 4;
    private const uint IconId = 0xD54E;

    private static readonly IntPtr HwndMessage = new(-3);

    // 进程内单实例（组合根唯一创建点）；静态 WndProc 经 _instance 反查（引用读写原子，无需锁）。
    // 窗口类注册/实例赋值仍在 _classSync 下串行。
    private static readonly object _classSync = new();
    private static BalloonNotificationService? _instance;
    private static ushort _classAtom;

    private readonly object _sync = new();
    private readonly Action _onClicked;
    private readonly IntPtr _window;
    private readonly IntPtr _icon;
    private readonly bool _ownsIcon;
    private bool _iconAdded;
    private bool _disposed;

    /// <summary>
    /// 初始化气泡通知服务：仅注册隐藏回调窗口并加载进程图标，<b>不</b>向通知区常驻图标。
    /// 须在带消息循环的线程（UI 线程）上构造。
    /// </summary>
    /// <param name="onClicked">气泡被点击时的回调（在 UI 线程触发）。</param>
    public BalloonNotificationService(Action onClicked)
    {
        ArgumentNullException.ThrowIfNull(onClicked);
        _onClicked = onClicked;

        lock (_classSync)
        {
            _window = CreateMessageWindow();
            _instance = this;
        }

        (_icon, _ownsIcon) = ProcessIcon.LoadHandle();
    }

    /// <inheritdoc />
    public Task ShowAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(message);

        // ShowAsync 可被诊断流任意线程触发；图标增删与气泡事件回调（UI 线程）经 _sync 互斥。
        lock (_sync)
        {
            if (_disposed)
            {
                return Task.CompletedTask;
            }

            if (!_iconAdded)
            {
                NOTIFYICONDATAW add = NewData();
                add.uFlags = NifMessage | NifIcon | NifTip;
                add.uCallbackMessage = BalloonCallbackMessage;
                add.hIcon = _icon;
                CopyTo(add.szTip, 128, "DSH Desktop");
                ThrowIfFailed(Shell_NotifyIconW(NimAdd, ref add), "NIM_ADD");

                // 版本语义须在发气泡前设置（v4：lParam 低字携带通知事件码）。
                NOTIFYICONDATAW version = NewData();
                version.uTimeoutOrVersion = NotifyIconVersion4;
                ThrowIfFailed(Shell_NotifyIconW(NimSetVersion, ref version), "NIM_SETVERSION");
                _iconAdded = true;
            }

            NOTIFYICONDATAW data = NewData();
            data.uFlags = NifInfo;
            data.dwInfoFlags = NiifInfo | NiifNoSound;
            CopyTo(data.szInfoTitle, 64, title);
            CopyTo(data.szInfo, 256, message);
            ThrowIfFailed(Shell_NotifyIconW(NimModify, ref data), "NIM_MODIFY(NIF_INFO)");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 删除通知区域图标（若已添加）并销毁回调窗口（应用退出时调用）。
    /// </summary>
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            DeleteIcon();
        }

        lock (_classSync)
        {
            _instance = null;
        }

        _ = DestroyWindow(_window);
        if (_ownsIcon && _icon != IntPtr.Zero)
        {
            ProcessIcon.DestroyIcon(_icon);
        }
    }

    /// <summary>
    /// 气泡结束后删除非常驻图标（调用方须已持 <see cref="_sync"/> 锁）。
    /// </summary>
    private void DeleteIcon()
    {
        if (_iconAdded)
        {
            _iconAdded = false;
            NOTIFYICONDATAW data = NewData();
            _ = Shell_NotifyIconW(NimDelete, ref data);
        }
    }

    private NOTIFYICONDATAW NewData()
    {
        return new NOTIFYICONDATAW
        {
            cbSize = (uint)sizeof(NOTIFYICONDATAW),
            hWnd = _window,
            uID = IconId,
        };
    }

    private static void CopyTo(char* destination, int capacity, string value)
    {
        int length = Math.Min(value.Length, capacity - 1);
        value.AsSpan(0, length).CopyTo(new Span<char>(destination, capacity));
        destination[length] = '\0';
    }

    private static void ThrowIfFailed(bool success, string operation)
    {
        if (!success)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Shell_NotifyIcon {operation} 失败。");
        }
    }

    private static IntPtr CreateMessageWindow()
    {
        IntPtr instance = GetModuleHandleW(IntPtr.Zero);
        ushort atom = EnsureClassRegistered(instance);
        IntPtr window = CreateWindowExW(
            0, (IntPtr)atom, IntPtr.Zero, 0, 0, 0, 0, 0,
            HwndMessage, IntPtr.Zero, instance, IntPtr.Zero);
        return window == IntPtr.Zero
            ? throw new Win32Exception(Marshal.GetLastWin32Error(), "创建通知回调窗口失败。")
            : window;
    }

    private static ushort EnsureClassRegistered(IntPtr instance)
    {
        // 调用方已持 _classSync 锁，无需重复同步。
        if (_classAtom != 0)
        {
            return _classAtom;
        }

        const string className = "DshDesktopNotificationWindow";
        fixed (char* name = className)
        {
            WNDCLASSEXW wndClass = new()
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &MessageWindowProc,
                hInstance = instance,
                lpszClassName = name,
            };
            _classAtom = RegisterClassExW(ref wndClass);
        }

        return _classAtom == 0
            ? throw new Win32Exception(Marshal.GetLastWin32Error(), "注册通知回调窗口类失败。")
            : _classAtom;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static IntPtr MessageWindowProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        // NOTIFYICON_VERSION_4：lParam 低字为通知事件码。
        if (message == BalloonCallbackMessage)
        {
            long eventCode = lParam.ToInt64() & 0xFFFF;
            if (eventCode is NinBalloonUserClick or NinBalloonHide or NinBalloonTimeout)
            {
                BalloonNotificationService? service = _instance;
                if (service is not null)
                {
                    if (eventCode == NinBalloonUserClick)
                    {
                        service._onClicked();
                    }

                    lock (service._sync)
                    {
                        service.DeleteIcon();
                    }
                }
            }
        }

        return DefWindowProcW(hWnd, message, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        public fixed char szTip[128];
        public uint dwState;
        public uint dwStateMask;
        public fixed char szInfo[256];
        public uint uTimeoutOrVersion;
        public fixed char szInfoTitle[64];
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, IntPtr, IntPtr> lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public IntPtr lpszMenuName;
        public char* lpszClassName;
        public IntPtr hIconSm;
    }

    [LibraryImport("shell32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATAW lpData);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial ushort RegisterClassExW(ref WNDCLASSEXW lpwcx);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr CreateWindowExW(
        uint dwExStyle,
        IntPtr lpClassName,
        IntPtr lpWindowName,
        uint dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    private static partial IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GetModuleHandleW(IntPtr lpModuleName);
}
