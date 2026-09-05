using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Markup.Xaml;
using DshDesktop.Domain.Common;
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

namespace DshDesktop.App;

/// <summary>
/// 表示主窗口：侧边栏导航 + 按应用壳当前页渲染对应 Feature 视图。
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly AppShellViewModel _shellViewModel;
    private readonly IMviResolver _resolver;
    private readonly ContentControl _rootContent;
    private readonly Ellipse _runtimeStatusDot;
    private readonly Border _updateBadgeBox;
    private readonly TextBlock _updateBadgeText;

    /// <summary>
    /// 初始化主窗口。
    /// </summary>
    /// <param name="shellViewModel">应用壳 ViewModel。</param>
    /// <param name="resolver">组件解析容器。</param>
    public MainWindow(AppShellViewModel shellViewModel, IMviResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(shellViewModel);
        ArgumentNullException.ThrowIfNull(resolver);

        _shellViewModel = shellViewModel;
        _resolver = resolver;

        AvaloniaXamlLoader.Load(this);
        DataContext = _shellViewModel;
        _rootContent = this.FindControl<ContentControl>("RootContent")
            ?? throw new InvalidOperationException("无法找到 RootContent 控件。");
        _runtimeStatusDot = this.FindControl<Ellipse>("RuntimeStatusDot")
            ?? throw new InvalidOperationException("无法找到 RuntimeStatusDot 控件。");
        _updateBadgeBox = this.FindControl<Border>("UpdateBadgeBox")
            ?? throw new InvalidOperationException("无法找到 UpdateBadgeBox 控件。");
        _updateBadgeText = this.FindControl<TextBlock>("UpdateBadgeText")
            ?? throw new InvalidOperationException("无法找到 UpdateBadgeText 控件。");

        _shellViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(AppShellViewModel.CurrentPage))
            {
                RenderCurrentPage();
            }
            else if (args.PropertyName
                is nameof(AppShellViewModel.RuntimeIndicator)
                or nameof(AppShellViewModel.UpdateBadge))
            {
                ApplyIndicators();
            }
        };

        RenderCurrentPage();
        ApplyIndicators();
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
    /// 应用侧栏指示器：Runtime 生命周期状态点 + Updates 可用更新数徽标（表现逻辑属于 View）。
    /// </summary>
    private void ApplyIndicators()
    {
        // 生命周期 → 颜色的映射共享自 Presentation 层（与 Runtime 页同色系）。
        _runtimeStatusDot.Fill = RuntimeLifecycleBrushes.For(_shellViewModel.RuntimeIndicator);

        int badge = _shellViewModel.UpdateBadge;
        _updateBadgeBox.IsVisible = badge > 0;
        _updateBadgeText.Text = badge.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private void RenderCurrentPage()
    {
        Control view = _shellViewModel.CurrentPage switch
        {
            ShellPage.Dashboard => CreateView<DashboardView, RuntimeViewModel>(),
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

    private TView CreateView<TView, TViewModel>()
        where TView : MviAvaloniaView<TViewModel>, new()
        where TViewModel : class
    {
        TView view = new();
        view.Bind(_resolver.Resolve<TViewModel>(), _resolver);
        return view;
    }
}
