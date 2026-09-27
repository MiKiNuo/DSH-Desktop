using System.IO;
using Avalonia.Controls;
using DshDesktop.Application.Diagnostics;
using DshDesktop.Application.Notifications;
using DshDesktop.Platform.Windows.Notifications;
using DshDesktop.Presentation.Avalonia.Features.AppShell;

namespace DshDesktop.App.Composition;

/// <summary>
/// 组合根拆分的托盘 / 通知生命周期模块（批 1 宿主侧）：持有通知服务与订阅者，
/// 接线托盘静态单图标、tooltip 投影 Runtime 生命周期、菜单（显示主窗口 / 退出）与气泡通知。
/// NotificationsEnabled 延迟配置读取（订阅时才读 _config，构造期 _config 尚未加载）。
/// Windows 门控、菜单 / 气泡置前、tooltip 投影、退出走 RequestExit 的语义保持与原组合根一致。
/// </summary>
internal sealed class DesktopTrayLifetime : IDisposable
{
    private readonly BalloonNotificationService? _notificationService;
    private readonly DiagnosticsNotificationSubscriber? _notificationSubscriber;

    public DesktopTrayLifetime(
        MainWindow window,
        AppShellViewModel shellViewModel,
        DiagnosticsHub hub,
        Func<bool> notificationsEnabled)
    {
        TrayIcon trayIcon = new()
        {
            Icon = new WindowIcon(new MemoryStream(ProcessIcon.LoadIcoBytes())),
            ToolTipText = TrayTooltipText.Format(shellViewModel.RuntimeIndicator),
        };
        NativeMenu trayMenu = new();
        NativeMenuItem showItem = new("显示主窗口");
        showItem.Click += (_, _) => ShowMainWindow(window);
        NativeMenuItem exitItem = new("退出");
        // 托盘退出是显式真实退出意图，须绕过"最小化到托盘"关窗拦截。
        exitItem.Click += (_, _) => window.RequestExit();
        trayMenu.Add(showItem);
        trayMenu.Add(exitItem);
        trayIcon.Menu = trayMenu;
        TrayIcon.SetIcons(global::Avalonia.Application.Current!, [trayIcon]);

        shellViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(AppShellViewModel.RuntimeIndicator))
            {
                trayIcon.ToolTipText = TrayTooltipText.Format(shellViewModel.RuntimeIndicator);
            }
        };

        _notificationService = new BalloonNotificationService(() => ShowMainWindow(window));
        _notificationSubscriber = new DiagnosticsNotificationSubscriber(
            hub,
            _notificationService,
            notificationsEnabled);
    }

    /// <summary>
    /// 置前主窗口（托盘菜单 / 气泡点击共用；Q2 决策：不导航）。
    /// </summary>
    private static void ShowMainWindow(MainWindow window)
    {
        if (!window.IsVisible)
        {
            window.Show();
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
    }

    /// <summary>
    /// 释放通知订阅者与服务（顺序保持：先 subscriber 再 service，与原组合根 Shutdown 一致）。
    /// </summary>
    public void Dispose()
    {
        _notificationSubscriber?.Dispose();
        _notificationService?.Dispose();
    }
}
