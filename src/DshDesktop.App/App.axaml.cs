using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using DshDesktop.App.Composition;
using DshDesktop.Application.Diagnostics;
using DshDesktop.Application.Runtime;
using DshDesktop.Application.Updates;
using DshDesktop.Presentation.Avalonia.Features.AppShell;
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

            _ = BootstrapRuntimeAsync((MainWindow)desktop.MainWindow);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task BootstrapRuntimeAsync(MainWindow window)
    {
        try
        {
            await _compositionRoot!.InitializeRuntimeAsync().ConfigureAwait(false);

            // 首启自检（每次启动执行）：无任何可用 DSH Runtime → 弹窗询问「下载并安装」。
            // 用户拒绝或安装失败时不再尝试自动启动（没有可启动的东西，空跑只会白等 120s 超时）。
            if (!await EnsureRuntimePresentAsync(window).ConfigureAwait(false))
            {
                return;
            }

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
            // 初始化失败不影响窗口可用性，但必须留痕。用结构化事件名（而非自由文本）：
            // 它经 Serilog → DiagnosticsHub 进诊断流，并由 NotificationTrigger 触发用户可见通知。
            // 缺了这一步，Runtime 起不来时用户只见「窗口能开、Runtime 不动」而毫无线索（2026-09-13 回归）。
            // 异常消息拼在事件名之后：DiagnosticsSink 只取 RenderMessage（丢弃异常对象），
            // 不拼进来用户看到的正文就只有事件名，仍无可用信息。
            Serilog.Log.Error(
                exception,
                "{Event} {Error}",
                DiagnosticEventNames.DesktopBootstrapFailed,
                exception.Message);
        }
    }

    /// <summary>
    /// 首启 Runtime 自检：无可用 Runtime（无借用安装且无自建版本）时弹「下载并安装」提示。
    /// 返回 true = 有可用 Runtime（原本就有 / 用户刚装好）；false = 用户拒绝或安装失败
    /// （调用方不再自动启动）。失败原因由弹层失败态如实展示，不静默。
    /// </summary>
    private async Task<bool> EnsureRuntimePresentAsync(MainWindow window)
    {
        if (!await _compositionRoot!.IsRuntimeSetupRequiredAsync().ConfigureAwait(false))
        {
            return true;
        }

        bool accepted = await Dispatcher.UIThread.InvokeAsync(window.PromptRuntimeSetupAsync);
        if (!accepted)
        {
            Serilog.Log.Information("Runtime.Setup.Declined");
            await Dispatcher.UIThread.InvokeAsync(() => window.ShowToast(RuntimeSetupText.DeclinedToast));
            return false;
        }

        // 安装编排在后台线程跑，进度回流统一编组到 UI 线程（弹层控件只许 UI 线程触碰）。
        Progress<RuntimeSetupProgress> progress = new(setupProgress =>
            Dispatcher.UIThread.Post(() => window.ReportRuntimeSetupProgress(setupProgress)));
        try
        {
            await _compositionRoot.SetupRuntimeAsync(progress).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(window.CompleteRuntimeSetup);
            return true;
        }
        catch (Exception exception)
        {
            Serilog.Log.Error(exception, "Runtime.Setup.Failed {Error}", exception.Message);
            await Dispatcher.UIThread.InvokeAsync(() => window.FailRuntimeSetup(exception.Message));
            return false;
        }
    }
}
