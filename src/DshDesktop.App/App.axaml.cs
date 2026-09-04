using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DshDesktop.App.Composition;
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
            if (!_compositionRoot.IsSafeMode)
            {
                await _compositionRoot.AutoStartRuntimeAsync().ConfigureAwait(false);
            }

            // §34：网络更新检查放后台任务（UI 就绪后）。
            _ = _compositionRoot.BackgroundCheckUpdatesAsync();
        }
        catch (Exception exception)
        {
            // 初始化失败不影响窗口可用性，但必须留痕（§45：失败进诊断流）。
            Serilog.Log.Error(exception, "Desktop.Bootstrap.Failed");
        }
    }
}
