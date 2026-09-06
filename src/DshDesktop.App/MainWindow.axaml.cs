using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using DshDesktop.Domain.Common;
using DshDesktop.Domain.Plugins;
using DshDesktop.Domain.Runtime;
using DshDesktop.Presentation.Avalonia.Features.AppShell;
using DshDesktop.Presentation.Avalonia.Features.Dashboard;
using DshDesktop.Presentation.Avalonia.Features.Diagnostics;
using DshDesktop.Presentation.Avalonia.Features.Plugins;
using DshDesktop.Presentation.Avalonia.Features.Runtime;
using DshDesktop.Presentation.Avalonia.Features.Settings;
using DshDesktop.Presentation.Avalonia.Features.Updates;
using DshDesktop.Presentation.Avalonia.Features.Workbench;
using MiKiNuo.Mvi.Application.DI;
using MiKiNuo.Mvi.Application.MVI.Store;
using MiKiNuo.Mvi.Platforms.Avalonia.Views;
using R3;

namespace DshDesktop.App;

/// <summary>
/// 表示主窗口（Phase 8 Issue 02 壳）：218px 侧栏导航 + 顶栏 + 按应用壳当前页渲染对应
/// Feature 视图 + 状态栏 + toast 浮层（视觉基准 docs/DSH-Desktop-UI-Prototype.html）。
/// </summary>
public sealed partial class MainWindow : Window
{
    /// <summary>
    /// toast 自动消隐时长（原型 showToast 的 1800ms）。
    /// </summary>
    private static readonly TimeSpan ToastDuration = TimeSpan.FromMilliseconds(1800);

    private readonly AppShellViewModel _shellViewModel;
    private readonly IMviResolver _resolver;
    private readonly Func<bool>? _minimizeToTrayOnClose;
    private bool _exitRequested;
    private readonly ContentControl _rootContent;
    private readonly Ellipse _statusBarDot;
    private readonly Ellipse _miniStatusDot;
    private readonly Border _updatesBadgeBox;
    private readonly TextBlock _updatesBadgeText;
    private readonly Border _toastBox;
    private readonly TextBlock _toastText;
    private readonly DispatcherTimer _toastTimer;
    private readonly IReadOnlyDictionary<ShellPage, Button> _navButtons;

    /// <summary>
    /// 初始化主窗口。
    /// </summary>
    /// <param name="shellViewModel">应用壳 ViewModel。</param>
    /// <param name="resolver">组件解析容器。</param>
    /// <param name="minimizeToTrayOnClose">"关闭窗口最小化到托盘"开关取值器（Phase 8 Issue 05；null = 关窗真退出）。</param>
    public MainWindow(
        AppShellViewModel shellViewModel,
        IMviResolver resolver,
        Func<bool>? minimizeToTrayOnClose = null)
    {
        ArgumentNullException.ThrowIfNull(shellViewModel);
        ArgumentNullException.ThrowIfNull(resolver);

        _shellViewModel = shellViewModel;
        _resolver = resolver;
        _minimizeToTrayOnClose = minimizeToTrayOnClose;

        AvaloniaXamlLoader.Load(this);
        DataContext = _shellViewModel;
        _rootContent = FindRequiredControl<ContentControl>("RootContent");
        _statusBarDot = FindRequiredControl<Ellipse>("StatusBarDot");
        _miniStatusDot = FindRequiredControl<Ellipse>("MiniStatusDot");
        _updatesBadgeBox = FindRequiredControl<Border>("UpdatesBadgeBox");
        _updatesBadgeText = FindRequiredControl<TextBlock>("UpdatesBadgeText");
        _toastBox = FindRequiredControl<Border>("ToastBox");
        _toastText = FindRequiredControl<TextBlock>("ToastText");
        _navButtons = new Dictionary<ShellPage, Button>
        {
            [ShellPage.Dashboard] = FindRequiredControl<Button>("NavDashboard"),
            [ShellPage.Workbench] = FindRequiredControl<Button>("NavWorkbench"),
            [ShellPage.Plugins] = FindRequiredControl<Button>("NavPlugins"),
            [ShellPage.Runtime] = FindRequiredControl<Button>("NavRuntime"),
            [ShellPage.Updates] = FindRequiredControl<Button>("NavUpdates"),
            [ShellPage.Diagnostics] = FindRequiredControl<Button>("NavDiagnostics"),
            [ShellPage.Settings] = FindRequiredControl<Button>("NavSettings"),
        };

        // Phase 8 评审 F14：导航按钮文案收敛到 ShellPageText 单一映射源（XAML 文本仅为设计期占位）。
        foreach ((ShellPage page, Button button) in _navButtons)
        {
            if (button.Content is Grid { Children.Count: >= 2 } grid && grid.Children[1] is TextBlock label)
            {
                label.Text = ShellPageText.Title(page);
            }
        }

        _toastTimer = new DispatcherTimer { Interval = ToastDuration };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            _toastBox.IsVisible = false;
        };

        _shellViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(AppShellViewModel.CurrentPage))
            {
                RenderCurrentPage();
                ApplyNavState();
            }
            else if (args.PropertyName
                is nameof(AppShellViewModel.RuntimeIndicator)
                or nameof(AppShellViewModel.UpdateBadge))
            {
                ApplyIndicators();
                if (args.PropertyName == nameof(AppShellViewModel.UpdateBadge))
                {
                    // 发现可用更新：徽标上升沿弹一条 toast。
                    int badge = _shellViewModel.UpdateBadge;
                    if (badge > _lastUpdateBadge)
                    {
                        ShowToast($"发现 {badge} 项可用更新");
                    }

                    _lastUpdateBadge = badge;
                }
            }
        };

        RenderCurrentPage();
        ApplyNavState();
        ApplyIndicators();
        WireToastScenarios();
    }

    /// <inheritdoc />
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // §46：Desktop 窗口可见计时（自进程入口起）。
        Serilog.Log.Information(
            "Desktop.Window.Visible ElapsedMs={ElapsedMs}",
            (long)StartupTimer.SinceProcessStart.ElapsedMilliseconds);
    }

    /// <summary>
    /// 显示 toast 浮层（右下角，约 1.8s 后自动消隐；接线场景见 <see cref="WireToastScenarios"/>）。
    /// </summary>
    /// <param name="text">提示文本。</param>
    public void ShowToast(string text)
    {
        _toastText.Text = text;
        _toastBox.IsVisible = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    // ===== Phase 8 评审 F7：toast 接线（只订阅现有 Store 投影 / 壳投影，不新造事件源） =====

    private int _lastUpdateBadge;
    private RuntimeLifecycle _lastLifecycle;
    private PluginOperation? _notifiedPluginOperation;

    /// <summary>
    /// 接线 toast 场景：插件安装事务完成/失败（PluginsStore.Operation 投影）、
    /// 发现可用更新（壳 UpdateBadge 上升沿，见 ApplyIndicators 调用点）、
    /// Runtime 恢复完成（RuntimeStore Recovering→Running 迁移）。
    /// </summary>
    private void WireToastScenarios()
    {
        _lastUpdateBadge = _shellViewModel.UpdateBadge;

        IMviStore<PluginsState, PluginsIntent, PluginsEffect> pluginsStore =
            _resolver.Resolve<IMviStore<PluginsState, PluginsIntent, PluginsEffect>>();
        pluginsStore.States.Subscribe(OnPluginsStateForToast);

        IMviStore<RuntimeState, RuntimeIntent, RuntimeEffect> runtimeStore =
            _resolver.Resolve<IMviStore<RuntimeState, RuntimeIntent, RuntimeEffect>>();
        _lastLifecycle = runtimeStore.CurrentState.Lifecycle;
        runtimeStore.States.Subscribe(OnRuntimeStateForToast);
    }

    private void OnPluginsStateForToast(PluginsState state)
    {
        // Operation 引用在阶段推进时整体替换；以引用去重，避免同一终态重复弹。
        if (state.Operation is not { } operation || ReferenceEquals(operation, _notifiedPluginOperation))
        {
            return;
        }

        if (operation.Stage is PluginOperationStage.Completed)
        {
            _notifiedPluginOperation = operation;
            ShowToast($"插件 {operation.PluginName} 安装完成");
        }
        else if (operation.Stage is PluginOperationStage.Failed)
        {
            _notifiedPluginOperation = operation;
            ShowToast($"插件 {operation.PluginName} 安装失败：{operation.Error}");
        }
    }

    private void OnRuntimeStateForToast(RuntimeState state)
    {
        if (_lastLifecycle is RuntimeLifecycle.Recovering && state.Lifecycle is RuntimeLifecycle.Running)
        {
            ShowToast("Runtime 已恢复运行");
        }

        _lastLifecycle = state.Lifecycle;
    }

    /// <summary>
    /// 托盘"退出"入口（Phase 8 Issue 05）：标记真实退出意图（绕过最小化到托盘拦截）后走 Shutdown 现状链路。
    /// </summary>
    public void RequestExit()
    {
        _exitRequested = true;
        (global::Avalonia.Application.Current?.ApplicationLifetime
            as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }

    /// <inheritdoc />
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Phase 8 Issue 05：关窗拦截为隐藏到托盘（开关开且非托盘退出）；
        // 与 KeepRuntimeOnClose 正交（ADR-0005）：托盘 = 窗藏宿主在，保持 Runtime = 宿主死 Runtime 留。
        if (WindowClosePolicy.ShouldHideToTray(_minimizeToTrayOnClose?.Invoke() == true, _exitRequested))
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    /// <summary>
    /// 应用壳指示器：状态栏 / runtime-mini 的 Runtime 生命周期状态点 + 更新中心徽标
    /// （表现逻辑属于 View；颜色映射共享自 Presentation 层，与 Runtime 页同色系）。
    /// </summary>
    private void ApplyIndicators()
    {
        IBrush lifecycleBrush = RuntimeLifecycleBrushes.For(_shellViewModel.RuntimeIndicator);
        _statusBarDot.Fill = lifecycleBrush;
        _miniStatusDot.Fill = lifecycleBrush;

        int badge = _shellViewModel.UpdateBadge;
        _updatesBadgeBox.IsVisible = badge > 0;
        _updatesBadgeText.Text = badge.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 应用侧栏导航选中态（当前页按钮切换 active 样式类，对应原型 .nav-btn.active）。
    /// </summary>
    private void ApplyNavState()
    {
        foreach ((ShellPage page, Button button) in _navButtons)
        {
            if (page == _shellViewModel.CurrentPage)
            {
                if (!button.Classes.Contains("active"))
                {
                    button.Classes.Add("active");
                }
            }
            else
            {
                button.Classes.Remove("active");
            }
        }
    }

    private void RenderCurrentPage()
    {
        Control view = _shellViewModel.CurrentPage switch
        {
            ShellPage.Dashboard => CreateView<DashboardView, DashboardViewModel>(),
            ShellPage.Workbench => CreateView<WorkbenchView, WorkbenchViewModel>(),
            ShellPage.Diagnostics => CreateView<DiagnosticsView, DiagnosticsViewModel>(),
            ShellPage.Plugins => CreateView<PluginsView, PluginsViewModel>(),
            ShellPage.Updates => CreateView<UpdatesView, UpdatesViewModel>(),
            ShellPage.Settings => CreateView<SettingsView, SettingsViewModel>(),
            _ => CreateView<RuntimeView, RuntimeViewModel>(),
        };

        _rootContent.Content = view;

        // 进入 Plugins 页即刷新清单（View 只产生 Intent，页面级加载由导航方发起）。
        if (_shellViewModel.CurrentPage is ShellPage.Plugins)
        {
            IMviStore<PluginsState, PluginsIntent, PluginsEffect> store =
                _resolver.Resolve<IMviStore<PluginsState, PluginsIntent, PluginsEffect>>();
            _ = store.DispatchAsync(new PluginsIntent.LoadPlugins());
        }

        // 进入 Settings 页即加载设置（同 Plugins 先例）。
        if (_shellViewModel.CurrentPage is ShellPage.Settings)
        {
            IMviStore<SettingsState, SettingsIntent, SettingsEffect> store =
                _resolver.Resolve<IMviStore<SettingsState, SettingsIntent, SettingsEffect>>();
            _ = store.DispatchAsync(new SettingsIntent.LoadSettings());
        }
    }

    private TControl FindRequiredControl<TControl>(string name)
        where TControl : Control
    {
        return this.FindControl<TControl>(name)
            ?? throw new InvalidOperationException($"无法找到 {name} 控件。");
    }

    private TView CreateView<TView, TViewModel>()
        where TView : MviAvaloniaView<TViewModel>, new()
        where TViewModel : class
    {
        TView view = new();
        view.Bind(_resolver.Resolve<TViewModel>(), _resolver);
        return view;
    }
}
