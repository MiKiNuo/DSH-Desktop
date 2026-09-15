using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using System.Threading.Tasks;
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
/// 表示主窗口（顶部导航改造壳）：52px 顶部导航 + 按应用壳当前页渲染对应 Feature 视图
/// + 状态栏 + toast 浮层（视觉基准 docs/DSH-Desktop-UI-Prototype.html；页标题条已移除）。
/// </summary>
public sealed partial class MainWindow : Window
{
    /// <summary>
    /// toast 自动消隐时长（原型 showToast 的 1800ms）。
    /// </summary>
    private static readonly TimeSpan ToastDuration = TimeSpan.FromMilliseconds(1800);

    /// <summary>
    /// 遮罩副标题兜底文案（Updates 状态无 PendingOperation 时用；与 MainWindow.axaml 初值一致）。
    /// </summary>
    private const string DefaultUpdateScrimText = "正在处理更新，请稍候…";

    private readonly AppShellViewModel _shellViewModel;
    private readonly IMviResolver _resolver;
    private readonly Func<bool>? _minimizeToTrayOnClose;
    private bool _exitRequested;
    private readonly ContentControl _rootContent;
    private readonly Border _topNav;
    private readonly Ellipse _statusBarDot;
    private readonly Border _updatesBadgeBox;
    private readonly TextBlock _updatesBadgeText;
    private readonly Border _toastBox;
    private readonly TextBlock _toastText;
    private readonly DispatcherTimer _toastTimer;
    private readonly IReadOnlyDictionary<ShellPage, Button> _navButtons;
    private readonly Border _updateScrim;

    // 必须全限定：本文件隐式 using System.IO，裸写 Path 在 Avalonia.Controls.Shapes.Path
    // 与 System.IO.Path 间二义（CS0104）。
    private readonly Avalonia.Controls.Shapes.Path _updateSpinner;
    private readonly ProgressBar _updateProgressBar;
    private readonly TextBlock _updateScrimText;

    // ===== 二次确认弹层（壳渲染，ConfirmDialog 注册 RequestConfirmAsync） =====
    private readonly Border _confirmScrim;
    private readonly TextBlock _confirmTitle;
    private readonly TextBlock _confirmBody;
    private readonly TextBlock _confirmSubject;
    private readonly Button _confirmOk;
    private readonly Button _confirmCancel;
    private TaskCompletionSource<bool>? _confirmTcs;

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
        _confirmScrim = FindRequiredControl<Border>("ConfirmScrim");
        _confirmTitle = FindRequiredControl<TextBlock>("ConfirmTitle");
        _confirmBody = FindRequiredControl<TextBlock>("ConfirmBody");
        _confirmSubject = FindRequiredControl<TextBlock>("ConfirmSubject");
        _confirmOk = FindRequiredControl<Button>("ConfirmOk");
        _confirmCancel = FindRequiredControl<Button>("ConfirmCancel");
        _rootContent = FindRequiredControl<ContentControl>("RootContent");
        _topNav = FindRequiredControl<Border>("TopNav");
        _statusBarDot = FindRequiredControl<Ellipse>("StatusBarDot");
        _updatesBadgeBox = FindRequiredControl<Border>("UpdatesBadgeBox");
        _updatesBadgeText = FindRequiredControl<TextBlock>("UpdatesBadgeText");
        _toastBox = FindRequiredControl<Border>("ToastBox");
        _toastText = FindRequiredControl<TextBlock>("ToastText");
        _updateScrim = FindRequiredControl<Border>("UpdateScrim");
        _updateSpinner = FindRequiredControl<Avalonia.Controls.Shapes.Path>("UpdateSpinner");
        _updateProgressBar = FindRequiredControl<ProgressBar>("UpdateProgress");
        _updateScrimText = FindRequiredControl<TextBlock>("UpdateScrimText");
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
                ApplyCurrentPageOnUiThread();
            }
            else if (args.PropertyName
                is nameof(AppShellViewModel.RuntimeIndicator)
                or nameof(AppShellViewModel.UpdateBadge))
            {
                ApplyIndicatorsOnUiThread(args.PropertyName);
            }
            else if (args.PropertyName == nameof(AppShellViewModel.UpdateInProgress))
            {
                ApplyUpdateScrim();
            }
        };

        // 二次确认弹层：取消 / 确认两个按钮收口到同一 TaskCompletionSource。
        _confirmCancel.Click += (_, _) => CompleteConfirm(false);
        _confirmOk.Click += (_, _) => CompleteConfirm(true);
        ConfirmDialog.Register((action, subject) => RequestConfirmAsync(action, subject));

        // 主题切换后必须重建并重算：Dsh*Brush 走 {DynamicResource} 会自动跟随主题字典，但下面这些是
        // C# 在构造期取出的**画刷引用** —— 状态栏圆点、各页就绪图标的着色与底色
        // （RuntimeLifecycleBrushes / RuntimeLifecycleProjection），不随主题字典变化重新求值。
        // 不重建的话，切到浅色后它们仍是深色主题的色值，最刺眼的是 TintStopped = DshSurface3Brush
        // 会在浅色卡片上留下一块深灰方块。
        // 必须 global:: 限定：本解决方案里存在 DshDesktop.Application 命名空间，裸写 Application
        // 会被解析成它而非 Avalonia.Application（CS0234）。
        if (global::Avalonia.Application.Current is { } themeHost)
        {
            themeHost.ActualThemeVariantChanged += (_, _) =>
            {
                RenderCurrentPage();
                ApplyIndicators();
            };
        }

        RenderCurrentPage();
        ApplyNavState();
        ApplyIndicators();
        ApplyUpdateScrim();
        WireToastScenarios();
        WireUpdateScrimProgress();
    }

    /// <inheritdoc />
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // §46：Desktop 窗口可见计时（自进程入口起）。
        Serilog.Log.Information(
            "Desktop.Window.Visible ElapsedMs={ElapsedMs}",
            (long)StartupTimer.SinceProcessStart.ElapsedMilliseconds);

        // caption 区避让（实测宽度，打开时 + DPI 变化时）。本类不再做窗口样式手术：最大化保持可用，
        // 见 MainWindow.axaml 注释与 WindowCaptionButtons 类注释。
        ApplyCaptionAvoidance();
        ScalingChanged += (_, _) => ApplyCaptionAvoidance();
    }

    /// <summary>
    /// 按实测 caption 按钮组宽度设置顶栏右内边距。实测只能运行时做（框架度量在 Full + Windows 下不可靠），
    /// 沙箱无法验证视觉效果，实机确认边界已在 WindowCaptionButtons 注释中标注。
    /// </summary>
    private void ApplyCaptionAvoidance()
    {
        _topNav.Padding = new Thickness(0, 0, WindowCaptionButtons.MeasureWidthDips(this), 0);
    }

    /// <summary>
    /// 显示 toast 浮层（右下角，约 1.8s 后自动消隐；接线场景见 <see cref="WireToastScenarios"/>）。
    /// </summary>
    /// <param name="text">提示文本。</param>
    public void ShowToast(string text)
    {
        // MainWindow 直接订阅 store.States（Plugins / Runtime），而 MviStore.DispatchAsync 在**派发线程**
        // 上同步发布 State；组合根又是在后台线程派发 intent 的（插件编排 OperationChanged、Runtime 快照），
        // 故这些订阅回调运行在线程池线程上。三个 toast 场景（插件终态 / 更新徽标 / Runtime 恢复）都在
        // 此处收口，故在此编组：已在 UI 线程同步执行，否则投递后返回（2026-09-14 实机崩溃回归）。
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ShowToast(text));
            return;
        }

        _toastText.Text = text;
        _toastBox.IsVisible = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    /// <summary>
    /// 弹出二次确认弹层并异步等待用户选择（由 <see cref="ConfirmDialog"/> 经注册的渲染器调用）。
    /// 填充 <see cref="ConfirmDialogText"/> 文案，按 <see cref="ConfirmDialogText.IsDangerous"/>
    /// 切换确认按钮的 danger / primary 样式，显示遮罩，用 <see cref="TaskCompletionSource{TResult}"/>
    /// 等待「取消 / 确认」之一，随后隐藏遮罩并回传结果。
    /// </summary>
    private Task<bool> RequestConfirmAsync(ConfirmAction action, string subject)
    {
        _confirmTitle.Text = ConfirmDialogText.Title(action);
        _confirmBody.Text = ConfirmDialogText.Body(action);
        _confirmSubject.Text = subject;
        _confirmOk.Content = ConfirmDialogText.ConfirmLabel(action);
        bool dangerous = ConfirmDialogText.IsDangerous(action);
        _confirmOk.Classes.Set("danger", dangerous);
        _confirmOk.Classes.Set("primary", !dangerous);

        _confirmScrim.IsVisible = true;
        _confirmTcs = new TaskCompletionSource<bool>();
        return _confirmTcs.Task;
    }

    /// <summary>
    /// 收口确认结果：隐藏遮罩并完成等待中的 <see cref="TaskCompletionSource{TResult}"/>。
    /// </summary>
    private void CompleteConfirm(bool result)
    {
        _confirmScrim.IsVisible = false;
        TaskCompletionSource<bool>? tcs = _confirmTcs;
        _confirmTcs = null;
        tcs?.TrySetResult(result);
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
    /// 应用壳指示器：状态栏 Runtime 生命周期状态点 + 更新中心徽标
    /// （表现逻辑属于 View；颜色映射共享自 Presentation 层，与 Runtime 页同色系）。
    /// </summary>
    private void ApplyIndicators()
    {
        _statusBarDot.Fill = RuntimeLifecycleBrushes.For(_shellViewModel.RuntimeIndicator);

        ApplyUpdatesBadge();
    }

    /// <summary>
    /// 应用更新中心徽标（顶部导航 Updates 项右侧；0 表示隐藏）。
    /// </summary>
    private void ApplyUpdatesBadge()
    {
        int badge = _shellViewModel.UpdateBadge;
        _updatesBadgeBox.IsVisible = badge > 0;
        _updatesBadgeText.Text = badge.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 应用顶部导航选中态（当前页按钮切换 active 样式类，对应原型 .nav-btn.active）。
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

    /// <summary>
    /// 应用更新中全屏遮罩：有更新操作进行中时显示遮罩并锁定导航按钮，否则移除遮罩并恢复导航。
    /// 壳 ViewModel 的 PropertyChanged 可能在后台派发线程上触发（§11.2 兄弟 Store 投影），
    /// 触及控件前必须编组到 UI 线程（同 <see cref="ShowToast"/> 的收口方式）。
    /// </summary>
    private void ApplyUpdateScrim()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(ApplyUpdateScrim);
            return;
        }

        bool running = _shellViewModel.UpdateInProgress;
        _updateScrim.IsVisible = running;
        foreach ((_, Button button) in _navButtons)
        {
            button.IsEnabled = !running;
        }
    }

    /// <summary>
    /// 应用当前页 + 导航选中态（壳投影可能在后台派发线程触发，触及控件前须编组到 UI 线程，同 <see cref="ApplyUpdateScrim"/>）。
    /// </summary>
    private void ApplyCurrentPageOnUiThread()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(ApplyCurrentPageOnUiThread);
            return;
        }

        RenderCurrentPage();
        ApplyNavState();
    }

    /// <summary>
    /// 应用指示器（状态点 + 更新徽标）与可用更新 toast（壳投影可能在后台派发线程触发，
    /// 触及控件前须编组到 UI 线程，同 <see cref="ApplyUpdateScrim"/>）。调用顺序与原始分支一致。
    /// </summary>
    private void ApplyIndicatorsOnUiThread(string? propertyName)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ApplyIndicatorsOnUiThread(propertyName));
            return;
        }

        ApplyIndicators();
        if (propertyName == nameof(AppShellViewModel.UpdateBadge))
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

    /// <summary>
    /// 接线遮罩内容：订阅 Updates Store，按「是否存在真实下载百分比」在**旋转图标（等待中）**与
    /// **确定进度条（真实进度）**之间互斥切换，并同步副标题为具体操作描述（§22）。
    /// 不再使用不确定态进度条：中段来回扫的滑块与「进度在推进」不可区分（2026-09-15 实机投诉）。
    /// </summary>
    private void WireUpdateScrimProgress()
    {
        IMviStore<UpdatesState, UpdatesIntent, UpdatesEffect> updatesStore =
            _resolver.Resolve<IMviStore<UpdatesState, UpdatesIntent, UpdatesEffect>>();
        updatesStore.States.Subscribe(OnUpdatesStateForScrim);
    }

    private void OnUpdatesStateForScrim(UpdatesState state)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnUpdatesStateForScrim(state));
            return;
        }

        if (state.DesktopDownloadProgress is { } percent)
        {
            _updateSpinner.IsVisible = false;
            _updateProgressBar.IsVisible = true;
            _updateProgressBar.Value = percent;
        }
        else
        {
            _updateSpinner.IsVisible = true;
            _updateProgressBar.IsVisible = false;
        }

        _updateScrimText.Text = state.PendingOperation ?? DefaultUpdateScrimText;
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
