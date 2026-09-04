using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using DshDesktop.Presentation.Avalonia.Features.AppShell;
using DshDesktop.Presentation.Avalonia.Features.Diagnostics;
using DshDesktop.Presentation.Avalonia.Features.Plugins;
using DshDesktop.Presentation.Avalonia.Features.Runtime;
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

        _shellViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(AppShellViewModel.CurrentPage))
            {
                RenderCurrentPage();
            }
        };

        RenderCurrentPage();
    }

    private void RenderCurrentPage()
    {
        Control view = _shellViewModel.CurrentPage switch
        {
            ShellPage.Workbench => CreateView<WorkbenchView, WorkbenchViewModel>(),
            ShellPage.Diagnostics => CreateView<DiagnosticsView, DiagnosticsViewModel>(),
            ShellPage.Plugins => CreateView<PluginsView, PluginsViewModel>(),
            ShellPage.Updates => CreateView<UpdatesView, UpdatesViewModel>(),
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
