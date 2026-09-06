using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DshDesktop.App.Composition;
using DshDesktop.Application.Updates;
using MiKiNuo.Mvi.Platforms.Avalonia.Threading;

namespace DshDesktop.App;

/// <summary>
/// 表示 Avalonia 应用。
/// </summary>
public sealed partial class App : global::Avalonia.Application
{
    private DshCompositionRoot? _compositionRoot;

    /// <summary>
    /// 初始化应用程序。
    /// </summary>
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// 框架初始化完成时创建主窗口。
    /// </summary>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _compositionRoot = new DshCompositionRoot(new AvaloniaMviUiDispatcher());

            // §17：窗口立即可见；配置加载 / Profile 种子 / Runtime 自动启动走后台。
            desktop.MainWindow = _compositionRoot.CreateMainWindow();
            desktop.Exit += (_, _) => _compositionRoot.Shutdown();

            _ = BootstrapRuntimeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task BootstrapRuntimeAsync()
    {
        try
        {
            await _compositionRoot!.InitializeRuntimeAsync().ConfigureAwait(false);

            // §34 修订注（Phase 8 Issue 04，评审 F2 语义修复）：两个时机独立——启动时开 =
            // 启动早期即检查（config 已载、Runtime 自举前，不等 UI Ready）；后台开 = UI Ready 后检查。
            UpdateCheckPlan plan = UpdateCheckSchedule.Plan(
                _compositionRoot.CheckUpdatesOnStartup, _compositionRoot.BackgroundUpdateCheckEnabled);
            if (plan.AtStartup)
            {
                _ = _compositionRoot.BackgroundCheckUpdatesAsync();
            }

            if (!_compositionRoot.IsSafeMode)
            {
                await _compositionRoot.AutoStartRuntimeAsync().ConfigureAwait(false);
            }

            // Phase 8 Issue 05：后台检查更新（默认开）——bootstrap 全程后台、窗口已可见，即"UI Ready 后"
            // 语义；启动早期已检查过则本次不重复发起。
            if (plan.AfterUiReady)
            {
                _ = _compositionRoot.BackgroundCheckUpdatesAsync();
            }
        }
        catch (Exception exception)
        {
            // 初始化失败不影响窗口可用性，但必须留痕（§45：失败进诊断流）。
            Serilog.Log.Error(exception, "Desktop.Bootstrap.Failed");
        }
    }
}
