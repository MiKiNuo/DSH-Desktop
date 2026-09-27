using DshDesktop.App.Logging;
using DshDesktop.Application.Diagnostics;
using DshDesktop.Application.Plugins;
using DshDesktop.Application.Runtime;
using DshDesktop.Application.Startup;
using DshDesktop.Application.Updates;
using DshDesktop.Application.Bootstrap;
using DshDesktop.Domain.Diagnostics;
using DshDesktop.Domain.Plugins;
using DshDesktop.Domain.Runtime;
using DshDesktop.Domain.Updates;
using DshDesktop.Infrastructure.Config;
using DshDesktop.Infrastructure.Paths;
using DshDesktop.Infrastructure.Plugins;
using DshDesktop.Infrastructure.Runtime;
using DshDesktop.Infrastructure.Updates;
using DshDesktop.Platform.Windows.Runtime;
using DshDesktop.Platform.Windows.Startup;
using DshDesktop.Presentation.Avalonia;
using DshDesktop.Presentation.Avalonia.Composition;
using DshDesktop.Presentation.Avalonia.Features.AppShell;
using DshDesktop.Presentation.Avalonia.Features.Dashboard;
using DshDesktop.Presentation.Avalonia.Features.Diagnostics;
using DshDesktop.Presentation.Avalonia.Features.Plugins;
using DshDesktop.Presentation.Avalonia.Features.Runtime;
using DshDesktop.Presentation.Avalonia.Features.Settings;
using DshDesktop.Presentation.Avalonia.Features.Updates;
using MiKiNuo.Mvi.Application.MVI.Mediator;
using MiKiNuo.Mvi.Application.MVI.Store;
using MiKiNuo.Mvi.Application.MVI.Threading;
using MiKiNuo.Mvi.Domain.MVI.Effect;
using R3;
using Serilog;

namespace DshDesktop.App.Composition;

/// <summary>
/// 表示 DSH Desktop 组合根（CONTEXT.md: Composition Root）：
/// 创建生成的 DI 容器、注册 Mediator 路由、接线 Runtime 监管、诊断流与日志。
/// </summary>
public sealed partial class DshCompositionRoot
{
    private readonly GeneratedMviContainer _container;
    private readonly DesktopDiagnostics _diagnostics = new();

    private IRuntimeSupervisor? _supervisor;
    private DshDesktopConfig? _config;
    private IPluginManager? _pluginRepository;
    private IPluginOrchestrator? _pluginOrchestrator;
    private IRuntimeRepository? _runtimeRepository;
    private IDesktopUpdater? _desktopUpdater;
    private DesktopTrayLifetime? _trayLifetime;

    // Phase 8 Issue 03：Dashboard 数据源（进程指标采样 / 启动耗时持久化 / timeline 投影的去重守卫）。
    private ProcessMetricsMonitor? _metricsMonitor;
    private TimeSpan? _lastStartupElapsedRecorded;
    private TimeSpan? _lastTimelineElapsed;

    // Phase 8 Issue 04：重接管探测（ADR-0005）/ 连续启动失败计数（ADR-0004 修订注）已迁入
    // Application 的 RuntimeRecoveryController（批 2b）；组合根只留端口适配与订阅转发。
    // Phase 8 评审 F9：探测原语下沉 Infrastructure 端口（RuntimeProbe 持有 HttpClient 并随 Shutdown 释放）。
    private IRuntimeProbe? _runtimeProbe;

    private RuntimeReattacher? _reattacher;

    // 批 2b：Runtime 生命周期恢复编排（恢复环 / 失败计数 / 重接管判定 / 指标判定）迁入 Application；
    // 组合根只留一行订阅转发 + 端口适配器（mediator / ConfigPersistence 闭包）。
    private RuntimeRecoveryController? _recoveryController;

    // 批 2a：装配好的运行时栈回流持有（SetupRuntimeAsync 工具链补全后按引用比较刷新绑定件）。
    private RuntimeStack? _stack;

    // Phase 8 Issue 05：Settings 页端口（打开目录 / 开机自启注册表；非 Windows 平台为 null）。
    private readonly DesktopSettings _settings;

    // config 落盘唯一入口：所有写路径（设置开关 / 版本切换 / 快照驱动的后台保存）
    // 共用 ConfigPersistence 同一把锁，并发写不丢写（守卫 CompositionRootGuardTests）。
    private readonly ConfigPersistence _configPersistence = new();

    // 引导期 Runtime 自愈登记册外壳（组合根拆分批 1）：收口三处引导期自愈 + 一处崩溃漂移自愈；
    // 崩溃自愈的「每会话每插件只试一次」状态内置于 Bootstrapper，故此处不再持有 _crashHealAttempted。
    private RuntimeBootstrapper? _bootstrapper;

    // Runtime 状态订阅与生命周期取消源：随 Shutdown 取消/释放，
    // 避免应用退出后仍有排队中的恢复意图被派发。
    private readonly CancellationTokenSource _lifetimeSource = new();
    private IDisposable? _runtimeLifecycleSubscription;

    /// <summary>
    /// 获取是否处于安全模式（抑制自动启动）。
    /// </summary>
    public bool IsSafeMode => _config?.SafeMode == true;

    /// <summary>
    /// 获取"启动时检查网络更新"开关（§34 修订注，默认关；App 引导据此门控后台检查）。
    /// </summary>
    public bool CheckUpdatesOnStartup => _config?.CheckUpdatesOnStartup == true;

    /// <summary>
    /// 获取"后台检查更新"开关（Phase 8 Issue 05，默认开；与 CheckUpdatesOnStartup 独立，
    /// 语义为 UI Ready 后异步检查——bootstrap 全程后台、窗口已可见，即满足该时序）。
    /// </summary>
    public bool BackgroundUpdateCheckEnabled => _config?.BackgroundUpdateCheck ?? true;

    /// <summary>
    /// 获取"关闭窗口最小化到托盘"开关（Phase 8 Issue 05，默认开；MainWindow Closing 据此拦截为隐藏）。
    /// </summary>
    public bool MinimizeToTrayOnClose => _config?.MinimizeToTrayOnClose ?? true;

    private string RuntimeRootDir => Path.Combine(
        Directory.GetParent(_config!.DshHome)!.FullName, "runtime", "dsh");

    /// <summary>
    /// 初始化组合根。
    /// </summary>
    /// <param name="uiDispatcher">平台 UI 调度器。</param>
    public DshCompositionRoot(IMviUiDispatcher uiDispatcher)
    {
        ArgumentNullException.ThrowIfNull(uiDispatcher);

        // ADR-0009 旧数据根一次性迁移 + 日志目录确立（迁移必须先于日志目录，理由见 DesktopDiagnostics）。
        // 失败不阻断——旧根还在，启动链按新根继续（评审记录，可接受）。
        LegacyDataRootMigrationOutcome migrationOutcome = _diagnostics.InitializeLogging();

        _container = new GeneratedMviContainer(uiDispatcher);

        // 子 logger 与诊断回流须晚于日志初始化与容器就绪：Desktop.Startup / 迁移结果日志订阅后才写。
        _diagnostics.InitializeSubLoggers();
        _diagnostics.SubscribeToStore(diagnosticEvent =>
            _ = _container.Resolve<IMviStore<DiagnosticsState, DiagnosticsIntent, DiagnosticsEffect>>()
                .DispatchAsync(new DiagnosticsIntent.DiagnosticEventReceived(diagnosticEvent)));
        _diagnostics.LogStartup(migrationOutcome);
        _settings = new DesktopSettings(
            () => { ThrowIfNotInitialized(); return _config!; },
            _configPersistence,
            enabled => _ = ResolveRuntimeStore().DispatchAsync(new RuntimeIntent.SafeModeChanged(enabled)));
    }

    /// <summary>
    /// 创建主窗口（同步路径：窗口先可见，Runtime 初始化走后台，§17）。
    /// </summary>
    /// <returns>主窗口。</returns>
    public MainWindow CreateMainWindow()
    {
        AppShellViewModel shellViewModel = _container.Resolve<AppShellViewModel>();
        // Phase 8 Issue 05：关窗拦截策略注入（读取 config 投影，组合根为权威源）。
        MainWindow window = new(shellViewModel, _container, () => MinimizeToTrayOnClose);

        // Windows 平台集成（Phase 7）：托盘单图标 + 气泡通知（Issue 03/04，图标合并见下）。
        // 通知资源收口到 DesktopTrayLifetime（批 1 宿主侧拆分），NotificationsEnabled 延迟配置读取。
        if (OperatingSystem.IsWindows())
        {
            _trayLifetime = new DesktopTrayLifetime(
                window, shellViewModel, _diagnostics.Hub, () => _config?.NotificationsEnabled ?? true);
        }

        return window;
    }

    /// <summary>
    /// 后台初始化 Runtime 监管：注册 Mediator 路由 → 加载配置 → Profile 种子复制 → 订阅快照与退出事件。
    /// </summary>
    /// <param name="cancellationToken">取消标记。</param>
    public async Task InitializeRuntimeAsync(CancellationToken cancellationToken = default)
    {
        // 路由注册先于一切可能抛错的操作（配置加载 / Profile 种子 / 各编排构造）：
        // 注册只依赖构造函数已就绪的 _container.Mediator，不触碰 _config。
        // 否则配置加载抛错时此处被跳过，运行态是「窗口可用但路由表为空」，
        // 任意页面动作都退化为「未找到中介者路由」（2026-09-13 设置页报错回归）。
        // 处理器内部的 ThrowIfNotInitialized 守卫负责给出语义化错误。
        RegisterRoutes();

        // 启动编排已收口到 RuntimeBootstrapper（组合根拆分批 2a）：配置加载 → 主题套用 →
        // pnpm 门控自举 → Profile 种子 → 栈装配 → 引导期自愈登记册，全部经端口/工厂抽象完成；
        // 组合根只留路由注册、装配工厂（App→Infrastructure 合法引用）与事件转发。
        var configAdapter = new CompositionRootConfigAdapter(_configPersistence);
        RuntimeBootstrapper bootstrapper = new(
            configAdapter,
            new CompositionRootThemeApplier(),
            new CompositionRootNodeProvisioner(),
            new CompositionRootPnpmProvisioner(),
            new CompositionRootProfileSeeder(),
            new CompositionRootStackFactory(this),
            new CompositionRootCrashProbeAdapter(),
            new ProfileManifestNormalizer(),
            DshDesktopConfigStore.DataRoot,
            Log.Logger);
        _bootstrapper = bootstrapper;

        RuntimeStack stack = await bootstrapper.RunAsync(cancellationToken).ConfigureAwait(false);
        _stack = stack;
        _config = configAdapter.Loaded!;

        stack.ProcessHost.OutputReceived += _diagnostics.OnProcessOutputReceived;
        _supervisor = stack.Supervisor;
        _runtimeProbe = stack.RuntimeProbe;
        _reattacher = stack.Reattacher;
        _pluginRepository = stack.PluginRepository;
        _pluginOrchestrator = stack.PluginOrchestrator;
        _pluginOrchestrator.OperationChanged += OnPluginOperationChanged;
        _runtimeRepository = stack.RuntimeRepository;
        _desktopUpdater = stack.DesktopUpdater;

        // Phase 8 Issue 05：Settings 页端口（打开目录 / 开机自启注册表 Run 键）；非 Windows 降级 null。
        if (OperatingSystem.IsWindows())
        {
            _settings.PathOpener = new ExplorerPathOpener();
            _settings.StartupRegistration = new StartupRegistrationService(
                new RunKeyStartupRegistrar(),
                () => Environment.ProcessPath ?? string.Empty);
        }

        _supervisor.SnapshotChanged += OnRuntimeSnapshotChanged;
        _supervisor.Exited += OnRuntimeExited;

        // 安全模式状态回流（跨重启恢复，§15.1 SafeMode）。
        IMviStore<RuntimeState, RuntimeIntent, RuntimeEffect> store = ResolveRuntimeStore();
        _ = store.DispatchAsync(new RuntimeIntent.SafeModeChanged(_config.SafeMode));

        // 批 2b：Runtime 生命周期恢复编排迁入 Application 的 RuntimeRecoveryController（引导 ≠ 恢复）。
        // 组合根只留一行订阅转发 + 端口适配器：恢复环 / 失败计数 / 重接管判定 / 指标判定经控制器，
        // Store 派发与 config 落盘经适配器闭包 mediator / ConfigPersistence。
        _recoveryController = new RuntimeRecoveryController(
            new RecoveryHostAdapter(this),
            new RecoveryConfigAdapter(this),
            _supervisor,
            _reattacher,
            _lifetimeSource.Token,
            logger: Log.Logger);

        // ADR-0007：Failed 后的有界自动恢复。只在「新进入 Failed」这一沿触发一次自动重试，
        // 成功后重置——不构成崩溃重启循环（ADR-0004 的禁令不变，本项是其最小放宽）。
        // 订阅对象换为控制器后语义保持：Shutdown 先 Dispose 订阅再 Cancel _lifetimeSource。
        _runtimeLifecycleSubscription = store.States.Subscribe(s => _recoveryController.OnState(s.Lifecycle));

        // Phase 8 Issue 04：三策略开关与运行环境信息回流（config 为权威源，覆盖 State.Initial 默认值）。
        _ = store.DispatchAsync(new RuntimeIntent.PoliciesLoaded(
            _config.KeepRuntimeOnClose,
            _config.AutoSafeModeOnFailure,
            _config.CheckUpdatesOnStartup));
        _ = store.DispatchAsync(new RuntimeIntent.EnvironmentLoaded(new RuntimeEnvironmentInfo(
            NodeVersionProbe.TryGetVersion(_config.NodePath),
            WebView2VersionProbe.TryGetVersion(),
            _config.DshHome,
            "web")));

        // Phase 8 Issue 03：Dashboard 数据源接线——
        // 进程指标采样（Running 期间 2s 定时，Application 编排 + Infrastructure 端口实现）。
        _metricsMonitor = new ProcessMetricsMonitor(new ProcessMetricsSampler());
        _metricsMonitor.Sampled += OnMetricsSampled;

        // Dashboard 环境输入（Desktop 通道 / 上次启动耗时；Node 版本经 RuntimeStore
        // Environment 投影由 BindSiblingState 回流，Phase 8 评审 F8 统一单通道）。
        _ = ResolveDashboardStore().DispatchAsync(
            new DashboardIntent.EnvironmentLoaded(
                _config.DesktopChannel,
                _config.LastStartupElapsedMs),
            cancellationToken);

        // Dashboard 插件数投影预热（与进入 Plugins 页的 LoadPlugins 同一链路，幂等）。
        IMviStore<PluginsState, PluginsIntent, PluginsEffect> pluginsStore =
            _container.Resolve<IMviStore<PluginsState, PluginsIntent, PluginsEffect>>();
        _ = pluginsStore.DispatchAsync(new PluginsIntent.LoadPlugins(), cancellationToken);
    }

    /// <summary>
    /// 首启自检（每次启动都应调用）：是否存在任何可用 DSH Runtime（借用外部安装或自建 side-by-side）。
    /// true = 一个都没有，应向用户弹「下载并安装」提示。
    /// </summary>
    public async Task<bool> IsRuntimeSetupRequiredAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfNotInitialized();
        IReadOnlyList<DshRuntimeInfo> runtimes = await _runtimeRepository!
            .ListRuntimesAsync(_config!.ActiveDshRuntime, cancellationToken)
            .ConfigureAwait(false);
        return runtimes.Count == 0;
    }

    /// <summary>
    /// 首启安装编排（用户在弹窗显式点「下载并安装」后由 App 调用）：
    /// 委托 RuntimeBootstrapper.RebuildStackAsync（批 2a）：node 自举（干净机器才下载）→ pnpm 自举 →
    /// 工具链落盘 → 重建构造期固化路径的组件 → 安装最新 DSH Runtime → 激活并落盘。
    /// 任何失败原样抛出（弹窗如实展示真实原因）。成功返回后调用方可走正常 <see cref="AutoStartRuntimeAsync"/>。
    /// </summary>
    public async Task SetupRuntimeAsync(
        IProgress<RuntimeSetupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfNotInitialized();

        RuntimeStack stack = await _bootstrapper!
            .RebuildStackAsync(progress, cancellationToken)
            .ConfigureAwait(false);

        // 工具链补全触发了绑定件重建（record with 换实例）时才刷新字段并重新接线；
        // node 本来就可用时栈未变，重复订阅同一编排器会让 OperationChanged 双发。
        if (!ReferenceEquals(stack, _stack))
        {
            _stack = stack;
            _pluginRepository = stack.PluginRepository;
            _pluginOrchestrator = stack.PluginOrchestrator;
            _pluginOrchestrator.OperationChanged += OnPluginOperationChanged;
            _runtimeRepository = stack.RuntimeRepository;
        }
    }

    /// <summary>
    /// 自动启动 Runtime（§17：窗口立即可见，Runtime 后台启动）。
    /// ADR-0005：KeepRuntimeOnClose 开且存在上次记录时先尝试重接管，探测失败回退正常启动链。
    /// </summary>
    /// <param name="cancellationToken">取消标记。</param>
    public async Task AutoStartRuntimeAsync(CancellationToken cancellationToken = default)
    {
        if (await _recoveryController!
                .TryReattachAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        IMviStore<RuntimeState, RuntimeIntent, RuntimeEffect> store = ResolveRuntimeStore();
        await store.DispatchAsync(new RuntimeIntent.StartRuntime(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 应用退出时按 ADR-0005 分叉处置 Runtime：KeepRuntimeOnClose 开 = 只退 Desktop（Runtime 保留，
    /// 下次启动重接管）；关 = 停 Runtime 并清除重接管记录（Phase 7 现状）。
    /// </summary>
    public void Shutdown()
    {
        // ADR-0007：先停掉自动恢复——退出流程自身会触发 Runtime 退出事件（MVI 侧可能落 Failed），
        // 若此时仍允许恢复，会在退出过程中重新拉起 Runtime。
        // 只取消不释放：排队中的恢复任务仍持有该令牌，释放后访问其 Token 会抛 ObjectDisposedException。
        _runtimeLifecycleSubscription?.Dispose();
        _lifetimeSource.Cancel();

        _trayLifetime?.Dispose();
        _metricsMonitor?.Dispose();
        _runtimeProbe?.Dispose();

        bool keepRuntimeOnClose = _config?.KeepRuntimeOnClose == true;
        RuntimeShutdown.ShutdownRuntime(_supervisor, keepRuntimeOnClose, Log.Logger);
        if (!keepRuntimeOnClose
            && _config is not null
            && (_config.LastRuntimePid is not null || _config.LastRuntimePort is not null))
        {
            _config.LastRuntimePid = null;
            _config.LastRuntimePort = null;
            SaveConfigAsync().GetAwaiter().GetResult();
        }

        Log.CloseAndFlush();
    }

    /// <summary>
    /// 后台静默检查更新一次（Q3-B，§34 允许的 Background Task）。
    /// </summary>
    public async Task BackgroundCheckUpdatesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            IMviStore<UpdatesState, UpdatesIntent, UpdatesEffect> store =
                _container.Resolve<IMviStore<UpdatesState, UpdatesIntent, UpdatesEffect>>();
            await store.DispatchAsync(new UpdatesIntent.CheckUpdates(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Log.Logger.Debug("Update.Check.BackgroundSkipped {Error}", exception.Message);
        }
    }

    private void RegisterRoutes()
    {
        if (_container.Mediator is not MviMediator mediator)
        {
            throw new InvalidOperationException("容器中介者不支持路由注册。");
        }

        mediator.Register<StartRuntimeRequest, RuntimeSnapshot>(HandleStartRuntimeAsync);
        mediator.Register<StopRuntimeRequest, bool>(HandleStopRuntimeAsync);
        mediator.Register<RestartRuntimeRequest, RuntimeSnapshot>(HandleRestartRuntimeAsync);
        mediator.Register<SetSafeModeRequest, bool>(HandleSetSafeModeAsync);
        mediator.Register<SetKeepRuntimeOnCloseRequest, bool>((request, ct) =>
            _settings.SetConfigFlagAsync(request, ct, c => c.KeepRuntimeOnClose = request.Enabled,
                "Runtime.KeepRuntimeOnClose {Enabled}", request.Enabled));
        mediator.Register<SetAutoSafeModeOnFailureRequest, bool>((request, ct) =>
            _settings.SetConfigFlagAsync(request, ct, c => c.AutoSafeModeOnFailure = request.Enabled,
                "Runtime.AutoSafeModeOnFailure {Enabled}", request.Enabled));
        mediator.Register<SetCheckUpdatesOnStartupRequest, bool>((request, ct) =>
            _settings.SetConfigFlagAsync(request, ct, c => c.CheckUpdatesOnStartup = request.Enabled,
                "Runtime.CheckUpdatesOnStartup {Enabled}", request.Enabled));
        mediator.Register<GetPluginListRequest, IReadOnlyList<PluginInfo>>(HandleGetPluginListAsync);
        mediator.Register<SetPluginEnabledRequest, IReadOnlyList<PluginInfo>>(HandleSetPluginEnabledAsync);
        mediator.Register<UninstallPluginRequest, IReadOnlyList<PluginInfo>>(HandleUninstallPluginAsync);
        mediator.Register<InstallPluginRequest, IReadOnlyList<PluginInfo>>(HandleInstallPluginAsync);
        mediator.Register<DisableAllThirdPartyRequest, IReadOnlyList<PluginInfo>>(HandleDisableAllThirdPartyAsync);
        mediator.Register<CheckUpdatesRequest, CheckUpdatesResponse>(HandleCheckUpdatesAsync);
        mediator.Register<InstallDshRuntimeRequest, IReadOnlyList<DshRuntimeInfo>>(HandleInstallDshRuntimeAsync);
        mediator.Register<ActivateDshRuntimeRequest, IReadOnlyList<DshRuntimeInfo>>(HandleActivateDshRuntimeAsync);
        mediator.Register<UpdatePluginRequest, bool>(HandleUpdatePluginAsync);
        mediator.Register<GetSettingsInfoRequest, SettingsInfo>(_settings.HandleGetSettingsInfo);
        mediator.Register<SetDshChannelRequest, bool>((request, ct) =>
            _settings.SetConfigFlagAsync(request, ct, c => c.DshChannel = request.Channel,
                "Settings.DshChannel {Channel}", request.Channel));
        mediator.Register<SetNotificationsEnabledRequest, bool>((request, ct) =>
            _settings.SetConfigFlagAsync(request, ct, c => c.NotificationsEnabled = request.Enabled,
                "Settings.Notifications {Enabled}", request.Enabled));
        mediator.Register<SetMinimizeToTrayOnCloseRequest, bool>((request, ct) =>
            _settings.SetConfigFlagAsync(request, ct, c => c.MinimizeToTrayOnClose = request.Enabled,
                "Settings.MinimizeToTrayOnClose {Enabled}", request.Enabled));
        mediator.Register<SetThemeRequest, bool>(_settings.HandleSetThemeAsync);
        mediator.Register<SetLaunchOnStartupRequest, bool>(_settings.HandleSetLaunchOnStartupAsync);
        mediator.Register<SetBackgroundUpdateCheckRequest, bool>((request, ct) =>
            _settings.SetConfigFlagAsync(request, ct, c => c.BackgroundUpdateCheck = request.Enabled,
                "Settings.BackgroundUpdateCheck {Enabled}", request.Enabled));
        mediator.Register<SetAutoDownloadUpdatesRequest, bool>((request, ct) =>
            _settings.SetConfigFlagAsync(request, ct, c => c.AutoDownloadUpdates = request.Enabled,
                "Settings.AutoDownloadUpdates {Enabled}", request.Enabled));
        mediator.Register<OpenPathRequest, bool>(_settings.HandleOpenPath);
        var diagnosticsCommands = new DesktopDiagnosticsCommands(
            _diagnostics.Hub, () => { ThrowIfNotInitialized(); return _config!; },
            () => _supervisor!, () => _runtimeProbe!, () => _pluginRepository!, _settings);
        mediator.Register<RunDiagnosisRequest, bool>(diagnosticsCommands.HandleRunDiagnosisAsync);
        mediator.Register<ExportDiagnosticsBundleRequest, bool>(diagnosticsCommands.HandleExportDiagnosticsBundle);
        mediator.Register<OpenLogsDirectoryRequest, bool>(diagnosticsCommands.HandleOpenLogsDirectory);
        mediator.Register<DownloadAndApplyDesktopUpdateRequest, bool>(HandleDownloadAndApplyDesktopUpdateAsync);
        mediator.Register<NavigateRequest, bool>(HandleNavigate);
    }

    /// <summary>
    /// 处理跨 Feature 导航请求（§28；Phase 8 Issue 03：Dashboard 按钮 → AppShell 导航意图）。
    /// </summary>
    private ValueTask<bool> HandleNavigate(NavigateRequest request, CancellationToken cancellationToken)
    {
        IMviStore<AppShellState, AppShellIntent, UnitEffect> shellStore =
            _container.Resolve<IMviStore<AppShellState, AppShellIntent, UnitEffect>>();
        AppShellIntent intent = request.Page switch
        {
            ShellPage.Dashboard => new AppShellIntent.ShowDashboard(),
            ShellPage.Workbench => new AppShellIntent.ShowWorkbench(),
            ShellPage.Plugins => new AppShellIntent.ShowPlugins(),
            ShellPage.Updates => new AppShellIntent.ShowUpdates(),
            ShellPage.Diagnostics => new AppShellIntent.ShowDiagnostics(),
            ShellPage.Settings => new AppShellIntent.ShowSettings(),
            _ => new AppShellIntent.ShowRuntime(),
        };
        _ = shellStore.DispatchAsync(intent, cancellationToken);
        return ValueTask.FromResult(true);
    }

    private async ValueTask<bool> HandleDownloadAndApplyDesktopUpdateAsync(
        DownloadAndApplyDesktopUpdateRequest request,
        CancellationToken cancellationToken)
    {
        ThrowIfNotInitialized();
        var progress = new Progress<int>(percent =>
        {
            IMviStore<UpdatesState, UpdatesIntent, UpdatesEffect> store =
                _container.Resolve<IMviStore<UpdatesState, UpdatesIntent, UpdatesEffect>>();
            _ = store.DispatchAsync(new UpdatesIntent.DesktopDownloadProgress(percent));
        });

        try
        {
            await _desktopUpdater!.DownloadAsync(progress, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // 下载失败此前只回流成 UI 文案，日志零痕迹（2026-09-19 实机排查全靠猜）：补一条留痕后原样上抛。
            // 取消（应用退出等）不算失败，不记 Error。
            Log.Logger.Error("Update.Desktop.DownloadFailed {Error}", exception.Message);
            throw;
        }

        // 应用并重启：进程退出，此行正常路径不返回之后的托管逻辑（§22 三套版本独立）。
        _desktopUpdater.ApplyAndRestart();
        return true;
    }

    private async ValueTask<CheckUpdatesResponse> HandleCheckUpdatesAsync(
        CheckUpdatesRequest request,
        CancellationToken cancellationToken)
    {
        ThrowIfNotInitialized();

        string? latestDsh = null;
        try
        {
            latestDsh = await _runtimeRepository!.GetLatestVersionAsync(_config!.DshChannel, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Log.Logger.Warning("Update.Check.DshFailed {Error}", exception.Message);
        }

        // Desktop 自更新检查（ADR-0003：失败不阻塞 DSH/插件检查）。
        string? latestDesktop = null;
        try
        {
            latestDesktop = (await _desktopUpdater!.CheckForUpdatesAsync(cancellationToken)
                .ConfigureAwait(false))?.Version;
        }
        catch (Exception exception)
        {
            Log.Logger.Debug("Update.Check.DesktopSkipped {Error}", exception.Message);
        }

        IReadOnlyList<PluginInfo> plugins = await _pluginRepository!.ListPluginsAsync(cancellationToken)
            .ConfigureAwait(false);
        List<PluginUpdateInfo> pluginUpdates = [];
        // 核心插件（dshmarket 等）也查更新——2026-09-19 v0.1.4 实机：旧核心插件 × 新 Runtime
        // 版本漂移导致启动硬崩且无任何更新入口。IsResolvable 自然排除 in-box bundle 与
        // 声明-未物化插件（它们的 Version 是占位符，查 npm 必出假更新）。
        foreach (PluginInfo plugin in plugins.Where(p => p is { Enabled: true, IsResolvable: true }))
        {
            string? latest = await _runtimeRepository!.GetLatestPluginVersionAsync(plugin.Name, cancellationToken)
                .ConfigureAwait(false);
            if (latest is not null && latest != plugin.Version)
            {
                pluginUpdates.Add(new PluginUpdateInfo(plugin.Name, plugin.Version, latest));
            }
        }

        IReadOnlyList<DshRuntimeInfo> runtimes = await _runtimeRepository!
            .ListRuntimesAsync(_config!.ActiveDshRuntime, cancellationToken).ConfigureAwait(false);
        string? currentDsh = runtimes.FirstOrDefault(r => r.IsActive)?.Version;

        // Phase 8 Issue 05：自动下载安装开关（默认关）——开 = 发现 Desktop 更新后后台预下载更新包，
        // 应用与重启仍需用户在更新中心确认（复用现有 DownloadAndApply 链路；DownloadAsync 已单飞 +
        // "已下载即复用"，与手动点击交叠只会等待/复用，不再抢同一文件）。
        if (latestDesktop is not null && _config.AutoDownloadUpdates)
        {
            _ = AutoDownloadDesktopUpdateAsync();
        }

        return new CheckUpdatesResponse(latestDsh, currentDsh, runtimes, pluginUpdates, latestDesktop);
    }

    /// <summary>
    /// 后台预下载已发现的 Desktop 更新包（不应用不重启；失败仅留痕，下次检查重试）。
    /// </summary>
    private async Task AutoDownloadDesktopUpdateAsync()
    {
        try
        {
            await _desktopUpdater!.DownloadAsync(null, CancellationToken.None).ConfigureAwait(false);
            Log.Logger.Information("Update.Desktop.AutoDownloaded");
        }
        catch (Exception exception)
        {
            Log.Logger.Debug("Update.Desktop.AutoDownloadSkipped {Error}", exception.Message);
        }
    }

    private async ValueTask<IReadOnlyList<DshRuntimeInfo>> HandleInstallDshRuntimeAsync(
        InstallDshRuntimeRequest request,
        CancellationToken cancellationToken)
    {
        ThrowIfNotInitialized();
        await _runtimeRepository!.InstallAsync(request.Version, cancellationToken).ConfigureAwait(false);
        Log.Logger.Information("Update.DshRuntime.Installed {Version}", request.Version);
        return await _runtimeRepository.ListRuntimesAsync(_config!.ActiveDshRuntime, cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask<IReadOnlyList<DshRuntimeInfo>> HandleActivateDshRuntimeAsync(
        ActivateDshRuntimeRequest request,
        CancellationToken cancellationToken)
    {
        ThrowIfNotInitialized();
        string? target = request.Version.Length == 0 ? null : request.Version;
        bool wasRunning = _supervisor!.Current.Lifecycle is RuntimeLifecycle.Running;
        using var activationSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _lifetimeSource.Token);
        await RuntimeActivation.ActivateAsync(
            new CompositionRootConfigAdapter(_configPersistence, _config!), target, wasRunning,
            async ct =>
            {
                if (wasRunning)
                {
                    await ResolveRuntimeStore().DispatchAsync(new RuntimeIntent.RuntimeStopOrchestrated(), ct);
                }
                await StopRuntimeIfRunningAsync(ct).ConfigureAwait(false);
            },
            async ct =>
            {
                RuntimeSnapshot snapshot = await TrackStartupAsync(
                    token => _supervisor.StartAsync(BuildLaunchOptions(), token), ct).ConfigureAwait(false);
                DispatchRuntimeStarted(snapshot);
            },
            previous => previous is null || Directory.Exists(Path.Combine(RuntimeRootDir, previous)),
            DispatchRuntimeFailed, activationSource.Token).ConfigureAwait(false);
        Log.Logger.Information("Update.DshRuntime.Activated {Version}", target ?? "借用");

        return await _runtimeRepository!.ListRuntimesAsync(_config.ActiveDshRuntime, cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask<bool> HandleUpdatePluginAsync(
        UpdatePluginRequest request,
        CancellationToken cancellationToken)
    {
        ThrowIfNotInitialized();

        // 🟡3：入口并发守卫（与 PluginsReducer.IsTransactionInFlight 同口径）。重复点击会并发跑多个
        // InstallAsync，故在读到非终态事务时直接忽略并记日志，不启动新事务。
        // 注意：崩溃漂移自愈路径**不**经此 handler（它绕过 MVI 守卫直接驱动编排器），不受此处影响。
        IMviStore<PluginsState, PluginsIntent, PluginsEffect> pluginsStore =
            _container.Resolve<IMviStore<PluginsState, PluginsIntent, PluginsEffect>>();
        PluginsState pluginsState = pluginsStore.CurrentState;
        if (pluginsState.Operation is
            { Stage: not PluginOperationStage.Completed and not PluginOperationStage.Failed })
        {
            Log.Logger.Warning(
                "Plugin.Update.Ignored.InFlight: 插件事务进行中，忽略重复的更新请求 {Plugin}",
                request.Name);
            return true;
        }

        // 🟡2：PluginsReducer.HandleUpdatePlugin 已置 PendingOperation（页内反馈），但 Operation 字段要等
        // 编排器首个 OperationChanged(Preparing) 回流才非空——其间壳遮罩的 PluginOperationInProgress 判定
        // （看 Operation 非终态）为假，形成"遮罩锁定窗口"。此处同步预置 Preparing 阶段，使遮罩自入口即锁定；
        // 编排器随后重发同一阶段（幂等，AppShell 按引用去重只处理一次），不重复副作用。不回退到 UpdatesState 双轨。
        _ = pluginsStore.DispatchAsync(new PluginsIntent.PluginOperationChanged(
            new PluginOperation(PluginOperationStage.Preparing, request.Name, null, PluginOperationKind.Update)));

        _ = await _pluginOrchestrator!
            .InstallAsync($"{request.Name}@latest", PluginOperationKind.Update, cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    private async ValueTask<IReadOnlyList<PluginInfo>> HandleInstallPluginAsync(
        InstallPluginRequest request,
        CancellationToken cancellationToken)
    {
        ThrowIfNotInitialized();
        _ = await _pluginOrchestrator!
            .InstallAsync(request.Source, PluginOperationKind.Install, cancellationToken)
            .ConfigureAwait(false);
        return await _pluginRepository!.ListPluginsAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<IReadOnlyList<PluginInfo>> HandleDisableAllThirdPartyAsync(
        DisableAllThirdPartyRequest request,
        CancellationToken cancellationToken)
    {
        ThrowIfNotInitialized();
        await StopRuntimeIfRunningAsync(cancellationToken).ConfigureAwait(false);
        await _pluginOrchestrator!.DisableAllThirdPartyAsync(cancellationToken).ConfigureAwait(false);
        Log.Logger.Information("Plugin.DisableAll.Completed");

        // Q6 恢复动作：全禁后自动启动 Runtime（干净环境下验证可用性）。
        IMviStore<RuntimeState, RuntimeIntent, RuntimeEffect> runtimeStore = ResolveRuntimeStore();
        _ = runtimeStore.DispatchAsync(new RuntimeIntent.StartRuntime(), cancellationToken);

        return await _pluginRepository!.ListPluginsAsync(cancellationToken).ConfigureAwait(false);
    }

    private void OnPluginOperationChanged(object? sender, PluginOperation operation)
    {
        IMviStore<PluginsState, PluginsIntent, PluginsEffect> store =
            _container.Resolve<IMviStore<PluginsState, PluginsIntent, PluginsEffect>>();
        _ = store.DispatchAsync(new PluginsIntent.PluginOperationChanged(operation));

        // 编排停止回流：插件事务在真正 StopAsync 之前就发布 StoppingRuntime（见 PluginOrchestrator），
        // 此处把 MVI 生命周期提前对齐 Stopping，使随后的进程退出（-1）被判定为用户请求的停止而非崩溃。
        if (operation.Stage is PluginOperationStage.StoppingRuntime)
        {
            IMviStore<RuntimeState, RuntimeIntent, RuntimeEffect> runtimeStore = ResolveRuntimeStore();
            _ = runtimeStore.DispatchAsync(new RuntimeIntent.RuntimeStopOrchestrated());
        }

        // 事务成功提交是两侧清单刷新的**唯一发起源**：更新中心入口与插件页入口都收口到这里，
        // 避免"各入口只刷新本侧 Store"导致对面页面（插件版本号 / 可更新列表 / 顶栏徽标）停在旧值。
        // 更新侧发显式终态意图（而非 CheckUpdates 兼职终态）：后者兼任"发起检查"，与在飞检查的完成回流
        // 交错时会互踩来源标记，使待办永不清空 ⇒ 全屏遮罩永久卡死（2026-09-21 v0.1.8 实机）。
        // 终态意图自带一次检查回流，插件版本仍以检查结果为准；失败分支刻意不广播：Failed 时
        // PluginOperationFailed 已把真实原因写进页内 LastError，自动 LoadPlugins/CheckUpdates 会把它清掉或覆盖。
        if (operation.Stage is PluginOperationStage.Completed)
        {
            _ = store.DispatchAsync(new PluginsIntent.LoadPlugins());

            IMviStore<UpdatesState, UpdatesIntent, UpdatesEffect> updatesStore =
                _container.Resolve<IMviStore<UpdatesState, UpdatesIntent, UpdatesEffect>>();
            _ = updatesStore.DispatchAsync(new UpdatesIntent.PluginOperationFinished(operation.PluginName));
        }
    }

    private async ValueTask<IReadOnlyList<PluginInfo>> HandleGetPluginListAsync(
        GetPluginListRequest request,
        CancellationToken cancellationToken)
    {
        ThrowIfNotInitialized();
        return await _pluginRepository!.ListPluginsAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<IReadOnlyList<PluginInfo>> HandleSetPluginEnabledAsync(
        SetPluginEnabledRequest request,
        CancellationToken cancellationToken)
    {
        ThrowIfNotInitialized();
        // A2：启停走插件事务（快照→停→变更→校验→重启→健康检查），失败自动回滚；
        // 阶段进度经 OnPluginOperationChanged 回流，壳遮罩与终态 toast 随之生效。
        await _pluginOrchestrator!.SetEnabledAsync(request.Name, request.Enabled, cancellationToken)
            .ConfigureAwait(false);
        return await _pluginRepository!.ListPluginsAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<IReadOnlyList<PluginInfo>> HandleUninstallPluginAsync(
        UninstallPluginRequest request,
        CancellationToken cancellationToken)
    {
        ThrowIfNotInitialized();
        // A2：卸载走插件事务（同管线），兑现确认弹窗「失败自动回滚，并重启原 Runtime」的承诺。
        await _pluginOrchestrator!.UninstallAsync(request.Name, cancellationToken).ConfigureAwait(false);
        return await _pluginRepository.ListPluginsAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<bool> HandleSetSafeModeAsync(
        SetSafeModeRequest request,
        CancellationToken cancellationToken)
    {
        await SetSafeModeCoreAsync(request.Enabled, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// 安全模式落盘 + 回流共享段（用户切换与 ADR-0004 修订注的自动进入共用）。
    /// </summary>
    private Task SetSafeModeCoreAsync(bool enabled, CancellationToken cancellationToken)
        => _settings.SetSafeModeCoreAsync(enabled, cancellationToken);

    /// <summary>
    /// 插件变更前置（Q7-A）：Running 时先停止 Runtime，变更后由用户手动重启。
    /// </summary>
    private async Task StopRuntimeIfRunningAsync(CancellationToken cancellationToken)
    {
        if (_supervisor!.Current.Lifecycle is RuntimeLifecycle.Running)
        {
            await _supervisor.StopAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask<RuntimeSnapshot> HandleStartRuntimeAsync(
        StartRuntimeRequest request,
        CancellationToken cancellationToken)
    {
        ThrowIfNotInitialized();
        return await TrackStartupAsync(
            ct => _supervisor!.StartAsync(BuildLaunchOptions(), ct), cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<RuntimeSnapshot> HandleRestartRuntimeAsync(
        RestartRuntimeRequest request,
        CancellationToken cancellationToken)
    {
        ThrowIfNotInitialized();
        return await TrackStartupAsync(
            ct => _supervisor!.RestartAsync(BuildLaunchOptions(), ct), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 启动成败计数（ADR-0004 修订注，Phase 8 Issue 04）：成功清零；失败累计，
    /// 连续 2 次且开关开启 → 自动进安全模式 + 发通知（诊断事件流 → NotificationTrigger → 气泡）。
    /// </summary>
    private async ValueTask<RuntimeSnapshot> TrackStartupAsync(
        Func<CancellationToken, Task<RuntimeSnapshot>> start,
        CancellationToken cancellationToken)
    {
        try
        {
            RuntimeSnapshot snapshot = await start(cancellationToken).ConfigureAwait(false);
            _recoveryController!.RecordSuccess();
            return snapshot;
        }
        catch (OperationCanceledException)
        {
            throw; // 调用方取消不是失败：不自愈、不计数（同 RuntimeSupervisor 语义）。
        }
        catch (Exception exception)
        {
            // 版本漂移自愈（2026-09-19 v0.1.4 实机）：旧插件静态 import 了 Runtime 已删除的
            // 命名导出 ⇒ 启动必崩，且肇事者可能是核心插件（无 UI 更新入口）⇒ 在 RuntimeBootstrapper
            // 内自动走编排器事务化升级（快照/停/变更/校验/启动/健康/回滚），成功即 Runtime 已被事务拉起。
            // 刻意绕开 MVI 的 IsTransactionInFlight 守卫：启动失败现场 Runtime 已停，用户事务不可能在飞。
            // 「每会话每插件只试一次、失败回落原失败计数链」的状态内置于 Bootstrapper。
            if (await _bootstrapper!
                    .TryHealIncompatiblePluginCrashAsync(exception.Message, cancellationToken)
                    .ConfigureAwait(false))
            {
                _recoveryController!.RecordSuccess();
                return _supervisor!.Current;
            }

            await _recoveryController!.RecordFailureAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private RuntimeLaunchOptions BuildLaunchOptions()
    {
        string entryPath = _config!.DshEntryPath;
        string workingDirectory = _config.WorkingDirectory;

        // 激活的自建 Runtime 优先（Q7-A）；借用外部安装为默认。
        if (_config.ActiveDshRuntime is { Length: > 0 } active)
        {
            string runtimeDir = Path.Combine(RuntimeRootDir, active);
            entryPath = Path.Combine(runtimeDir, "node_modules", "@deepseek-ai", "dsh", "lib", "bin.js");
            workingDirectory = runtimeDir;
        }

        return new RuntimeLaunchOptions(
            NodePath: _config.NodePath,
            EntryPath: entryPath,
            HarnessNodeEntryPath: _config.HarnessNodeEntryPath,
            ToolBinDirectory: EnsureToolBinDirectory(),
            WorkingDirectory: workingDirectory,
            DshHome: _config.DshHome,
            Host: _config.Host,
            Port: _config.Port,
            StartupTimeout: TimeSpan.FromSeconds(_config.StartupTimeoutSeconds));
    }

    /// <summary>
    /// 确保工具垫片（&lt;dshHome&gt;\.desktop-bin 的 pnpm.cmd / node.cmd）存在并返回其目录。
    /// </summary>
    /// <remarks>
    /// 工作台内的 dsh-market 与其拉起的 dsh CLI 按【名字】调用 pnpm，而 vendored pnpm 只是
    /// pnpm.cjs（非可执行名）⇒ 必须靠垫片让它按名可解析，否则市场顶部常驻
    /// 「安装插件前需要先配置 pnpm 环境」且安装被拦停。
    /// 失败只告警不阻断：装不了插件 ≠ 应用起不来，用户仍可用宿主的插件管理页。
    /// 因而不做异常类型过滤（ArgumentException / PathTooLongException 等同样必须吞掉）——
    /// 这里唯一的正确行为是"垫片尽力而为，绝不影响启动"。
    /// </remarks>
    private string? EnsureToolBinDirectory()
    {
        try
        {
            return DesktopBinProvisioner.Ensure(
                _config!.DshHome,
                _config.NodePath,
                _config.PnpmCjsPath ?? string.Empty,
                DesktopBinProvisioner.ResolveRunnerPath(AppContext.BaseDirectory));
        }
        catch (Exception ex)
        {
            Log.Logger.Warning(ex, DiagnosticEventNames.ToolBinProvisionFailed);
            return null;
        }
    }

    private async ValueTask<bool> HandleStopRuntimeAsync(
        StopRuntimeRequest request,
        CancellationToken cancellationToken)
    {
        ThrowIfNotInitialized();
        await _supervisor!.StopAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private void OnRuntimeSnapshotChanged(object? sender, RuntimeSnapshot snapshot)
    {
        IMviStore<RuntimeState, RuntimeIntent, RuntimeEffect> store = ResolveRuntimeStore();
        _ = store.DispatchAsync(new RuntimeIntent.RuntimeSnapshotReceived(snapshot));

        // 批 2b：指标监控启停 / 启动耗时去重派发 / 重接管目标落盘 / Running 对账派发迁入控制器，
        // 组合根只做纯投影转发（RuntimeSnapshotReceived）与订阅转发。
        _recoveryController!.OnSnapshot(snapshot, store.CurrentState.Lifecycle);
    }

    /// <summary>
    /// 以 supervisor 快照为事实源显式回流 RuntimeStarted（激活切换链路与快照对账共用；
    /// RuntimeSnapshotReceived 刻意不迁移 Lifecycle，故须单独派发）。批 2b：对账经控制器 → 端口适配器。
    /// </summary>
    private void DispatchRuntimeStarted(RuntimeSnapshot snapshot)
        => DispatchRuntimeStarted(snapshot.ProcessId, snapshot.Port, snapshot.Url ?? string.Empty);

    /// <summary>以拆解后的 PID/端口/URL 显式回流 RuntimeStarted（端口适配器与激活链路共用）。</summary>
    private void DispatchRuntimeStarted(int? processId, int? port, string url)
        => _ = ResolveRuntimeStore().DispatchAsync(new RuntimeIntent.RuntimeStarted(processId, port, url));

    /// <summary>
    /// 回流 Runtime 启动失败（激活切换链路用：整条链含回退都失败时，让页面看到真实原因
    /// 而非停在 Stopped/Starting）。
    /// </summary>
    private void DispatchRuntimeFailed(string error)
    {
        _ = ResolveRuntimeStore().DispatchAsync(new RuntimeIntent.RuntimeFailed(error));
    }

    /// <summary>
    /// 串行化保存当前 config（统一走 ConfigPersistence；守卫 CompositionRootGuardTests）。
    /// </summary>
    private Task SaveConfigAsync(CancellationToken cancellationToken = default)
        => _configPersistence.SaveAsync(_config!, cancellationToken);

    /// <summary>
    /// fire-and-forget 保存（Phase 8 评审 F12：异常必须观测——写盘失败不炸快照回调，但留 Warning 痕）。
    /// </summary>
    private void SaveConfigInBackground(CancellationToken cancellationToken = default)
    {
        _ = ObserveSaveAsync(SaveConfigAsync(cancellationToken));

        async Task ObserveSaveAsync(Task save)
        {
            try
            {
                await save.ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                Log.Logger.Warning("Config.Save.BackgroundFailed {Error}", exception.Message);
            }
        }
    }

    private void OnMetricsSampled(object? sender, ProcessMetricsSample sample)
    {
        _ = ResolveDashboardStore().DispatchAsync(
            new DashboardIntent.MetricsSampled(sample.CpuPercent, sample.WorkingSetBytes));
    }

    private void OnRuntimeExited(object? sender, RuntimeExitedEventArgs args)
    {
        IMviStore<RuntimeState, RuntimeIntent, RuntimeEffect> store = ResolveRuntimeStore();
        _ = store.DispatchAsync(new RuntimeIntent.RuntimeExited(args.ExitCode));
    }

    private IMviStore<RuntimeState, RuntimeIntent, RuntimeEffect> ResolveRuntimeStore()
    {
        return _container.Resolve<IMviStore<RuntimeState, RuntimeIntent, RuntimeEffect>>();
    }

    private IMviStore<DashboardState, DashboardIntent, DashboardEffect> ResolveDashboardStore()
    {
        return _container.Resolve<IMviStore<DashboardState, DashboardIntent, DashboardEffect>>();
    }

    private void ThrowIfNotInitialized()
    {
        if (_supervisor is null || _config is null)
        {
            throw new InvalidOperationException("Runtime 编排尚未初始化完成，请稍候再试。");
        }
    }

    // ===== 组合根拆分批 2a：RuntimeBootstrapper 的 Infrastructure 适配（App→Infrastructure 引用合法，
    // 仅 Application 不得新增对 Infrastructure 的项目引用） =====

    /// <summary>
    /// 把 <see cref="DshDesktopConfig"/> 适配为启动编排配置端口：LoadAsync 内部转调
    /// <see cref="DshDesktopConfigStore.LoadOrDetectAsync"/> 并装载到可变槽位，编排期读写字段，
    /// 落盘经 <see cref="ConfigPersistence"/> 同一把锁。组合根在 RunAsync 返回后取回
    /// <see cref="Loaded"/> 赋给 _config（路由处理器消费具体类型）。
    /// </summary>
    private sealed class CompositionRootConfigAdapter : IRuntimeConfig
    {
        private readonly ConfigPersistence _persistence;

        public CompositionRootConfigAdapter(ConfigPersistence persistence, DshDesktopConfig? loaded = null)
        {
            _persistence = persistence;
            Loaded = loaded;
        }

        /// <summary>LoadAsync 完成后的已加载配置。</summary>
        public DshDesktopConfig? Loaded { get; private set; }

        private DshDesktopConfig Current => Loaded
            ?? throw new InvalidOperationException("配置尚未加载。");

        public async Task LoadAsync(CancellationToken cancellationToken) =>
            Loaded = await DshDesktopConfigStore.LoadOrDetectAsync(cancellationToken).ConfigureAwait(false);

        public string Theme => Current.Theme;

        public string? DshEntryPath => Current.DshEntryPath;

        public string? ActiveDshRuntime
        {
            get => Current.ActiveDshRuntime;
            set => Current.ActiveDshRuntime = value;
        }

        public string NodePath
        {
            get => Current.NodePath;
            set => Current.NodePath = value;
        }

        public string? NpmCjsPath
        {
            get => Current.NpmCjsPath;
            set => Current.NpmCjsPath = value;
        }

        public string? PnpmCjsPath
        {
            get => Current.PnpmCjsPath;
            set => Current.PnpmCjsPath = value;
        }

        public string DshHome => Current.DshHome;

        public string? SeedProfileFrom => Current.SeedProfileFrom;

        public string DshChannel => Current.DshChannel;

        public Task PersistAsync(CancellationToken cancellationToken)
            => _persistence.SaveAsync(Current, cancellationToken);
    }

    /// <summary>把组合根的 ApplyTheme（走 UI 线程 Post）适配为主题套用端口。</summary>
    private sealed class CompositionRootThemeApplier : IThemeApplier
    {
        public void ApplyTheme(string theme) => DesktopSettings.ApplyTheme(theme);
    }

    /// <summary>把 <see cref="NodeProvisioner"/> 静态自举适配为 node 自举端口。</summary>
    private sealed class CompositionRootNodeProvisioner : INodeProvisioner
    {
        public bool IsNodeAvailable(string? nodePath) => NodeProvisioner.IsNodeAvailable(nodePath);

        public async Task<NodeProvisionResult> EnsureAvailableAsync(
            string dataRoot, IProgress<int>? progress, CancellationToken cancellationToken)
        {
            NodeToolchain toolchain = await NodeProvisioner
                .EnsureAvailableAsync(dataRoot, progress, cancellationToken)
                .ConfigureAwait(false);
            return new NodeProvisionResult(toolchain.NodePath, toolchain.NpmCjsPath);
        }
    }

    /// <summary>把 <see cref="PnpmProvisioner"/> 静态自举适配为 pnpm 自举端口。</summary>
    private sealed class CompositionRootPnpmProvisioner : IPnpmProvisioner
    {
        public Task<string?> EnsureAvailableAsync(
            string dataRoot,
            string nodePath,
            string? npmCjsPath,
            string? currentPnpmCjsPath,
            CancellationToken cancellationToken) =>
            PnpmProvisioner.EnsureAvailableAsync(
                dataRoot, nodePath, npmCjsPath, currentPnpmCjsPath, cancellationToken);
    }

    /// <summary>把 <see cref="ProfileSeeder"/> 静态种子复制适配为种子端口。</summary>
    private sealed class CompositionRootProfileSeeder : IProfileSeeder
    {
        public Task SeedIfNeededAsync(
            string dshHome,
            string? seedProfileFrom,
            string nodePath,
            string? pnpmCjsPath,
            CancellationToken cancellationToken) =>
            ProfileSeeder.SeedIfNeededAsync(dshHome, seedProfileFrom, nodePath, pnpmCjsPath, cancellationToken);
    }

    /// <summary>
    /// 运行时栈装配工厂（原 WirePluginStack / CreateRuntimeRepository / 进程宿主与监管器构造）：
    /// new Infrastructure 具体件的唯一位置。事件接线（OutputReceived / OperationChanged / 快照订阅）
    /// 留在组合根本体——转发是组合根职责，工厂只产未接线的实例。
    /// </summary>
    private sealed class CompositionRootStackFactory(DshCompositionRoot outer) : IRuntimeStackFactory
    {
        public RuntimeStack Create(IRuntimeConfig config)
        {
            var processHost = new DshProcessHost();
            var supervisor = new RuntimeSupervisor(processHost, Log.Logger);
            var probe = new RuntimeProbe();
            var reattacher = new RuntimeReattacher(probe, Log.Logger);
            ToolchainBoundStack bound = RebuildToolchainBound(config, supervisor);

            // GitHub Releases 自更新适配器（Inno 安装形态；未安装形态 no-op，不依赖 config）。
            var desktopUpdater = new GitHubDesktopUpdater(Log.Logger, DesktopInfo.Version);
            return new RuntimeStack(
                processHost,
                supervisor,
                probe,
                reattacher,
                bound.PluginRepository,
                bound.PluginOrchestrator,
                bound.RuntimeRepository,
                desktopUpdater);
        }

        public ToolchainBoundStack RebuildToolchainBound(IRuntimeConfig config, RuntimeSupervisor supervisor)
        {
            string profileDir = Path.Combine(config.DshHome, "profiles", "web");
            var pluginRepository = new PluginProfileRepository(profileDir, config.NodePath, config.PnpmCjsPath);
            var snapshotter = new ProfileSnapshotter(
                profileDir,
                Path.Combine(Directory.GetParent(config.DshHome)!.FullName, "backups"),
                config.NodePath,
                config.PnpmCjsPath ?? string.Empty);
            var orchestrator = new PluginOrchestrator(
                pluginRepository,
                snapshotter,
                supervisor,
                outer.BuildLaunchOptions,
                Log.Logger);
            string runtimeRootDir = Path.Combine(
                Directory.GetParent(config.DshHome)!.FullName, "runtime", "dsh");
            var runtimeRepository = new RuntimeRepository(
                runtimeRootDir, config.NodePath, config.NpmCjsPath, config.DshEntryPath);
            return new ToolchainBoundStack(pluginRepository, orchestrator, runtimeRepository);
        }
    }

    /// <summary>把 <see cref="IncompatiblePluginCrashProbe"/> 纯函数探针适配为崩溃肇事解析端口。</summary>
    private sealed class CompositionRootCrashProbeAdapter : IIncompatiblePluginCrashProbe
    {
        public string? TryParseOffender(string startFailureMessage)
            => IncompatiblePluginCrashProbe.TryParseOffender(startFailureMessage);
    }

    /// <summary>
    /// 恢复/重接管/指标判定的副作用端口适配器（批 2b）：闭包 mediator Store 与 ConfigPersistence，
    /// 落地 Application 编排描述的"何时做什么"——Store 派发与 config 写唯一收口在组合根。
    /// </summary>
    private sealed class RecoveryHostAdapter : IRuntimeRecoveryHost
    {
        private readonly DshCompositionRoot _outer;

        public RecoveryHostAdapter(DshCompositionRoot outer) => _outer = outer;

        public Task RedispatchStartRuntimeAsync(CancellationToken cancellationToken)
            => _outer.ResolveRuntimeStore()
                .DispatchAsync(new RuntimeIntent.StartRuntime(), cancellationToken)
                .AsTask();

        public Task EnterSafeModeAsync(CancellationToken cancellationToken)
            => _outer.SetSafeModeCoreAsync(true, cancellationToken);

        public void DispatchRuntimeStarted(int? processId, int? port, string url)
            => _outer.DispatchRuntimeStarted(processId, port, url);

        public void DispatchTimeline(IReadOnlyList<StartupStageTiming> timings)
            => _ = _outer.ResolveDashboardStore()
                .DispatchAsync(new DashboardIntent.TimelineReceived(timings));

        public void DispatchStartupElapsed(long? previousMs)
            => _ = _outer.ResolveDashboardStore()
                .DispatchAsync(new DashboardIntent.StartupElapsedRecorded(previousMs));

        public void StartMetricsMonitor(int processId) => _outer._metricsMonitor?.Start(processId);

        public void StopMetricsMonitor() => _outer._metricsMonitor?.Stop();

        public Task PersistReattachTargetAsync(int processId, int port, CancellationToken cancellationToken)
        {
            if (_outer._config is null)
            {
                return Task.CompletedTask;
            }

            _outer._config.LastRuntimePid = processId;
            _outer._config.LastRuntimePort = port;
            _outer.SaveConfigInBackground(cancellationToken);
            return Task.CompletedTask;
        }

        public Task ClearReattachRecordAsync(CancellationToken cancellationToken)
        {
            if (_outer._config is null)
            {
                return Task.CompletedTask;
            }

            // NotFound / DegradedToRestart：清除陈旧记录，回退正常启动链。
            _outer._config.LastRuntimePid = null;
            _outer._config.LastRuntimePort = null;
            _outer.SaveConfigInBackground(cancellationToken);
            return Task.CompletedTask;
        }

        public Task PersistStartupElapsedAsync(long elapsedMs, CancellationToken cancellationToken)
        {
            if (_outer._config is null)
            {
                return Task.CompletedTask;
            }

            _outer._config.LastStartupElapsedMs = elapsedMs;
            _outer.SaveConfigInBackground(cancellationToken);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// 恢复/重接管/指标判定所需的配置读取端口适配器（批 2b，只读投影）：把 DshDesktopConfig 适配进来，
    /// Application 不新增对 Infrastructure 的项目引用。
    /// </summary>
    private sealed class RecoveryConfigAdapter : IRuntimeRecoveryConfig
    {
        private readonly DshCompositionRoot _outer;

        public RecoveryConfigAdapter(DshCompositionRoot outer) => _outer = outer;

        public bool KeepRuntimeOnClose => _outer._config?.KeepRuntimeOnClose == true;

        public string Host => _outer._config?.Host ?? string.Empty;

        public int? LastRuntimePid => _outer._config?.LastRuntimePid;

        public int? LastRuntimePort => _outer._config?.LastRuntimePort;

        public bool AutoSafeModeOnFailure => _outer._config?.AutoSafeModeOnFailure == true;

        public long? LastStartupElapsedMs => _outer._config?.LastStartupElapsedMs;
    }
}
