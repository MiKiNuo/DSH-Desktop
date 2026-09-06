using Avalonia.Controls;
using DshDesktop.App.Logging;
using DshDesktop.Application.Diagnostics;
using DshDesktop.Application.Notifications;
using DshDesktop.Application.Paths;
using DshDesktop.Application.Plugins;
using DshDesktop.Application.Runtime;
using DshDesktop.Application.Startup;
using DshDesktop.Application.Updates;
using DshDesktop.Domain.Diagnostics;
using DshDesktop.Domain.Plugins;
using DshDesktop.Domain.Runtime;
using DshDesktop.Domain.Updates;
using DshDesktop.Infrastructure.Config;
using DshDesktop.Infrastructure.Diagnostics;
using DshDesktop.Infrastructure.Paths;
using DshDesktop.Infrastructure.Plugins;
using DshDesktop.Infrastructure.Runtime;
using DshDesktop.Infrastructure.Updates;
using DshDesktop.Platform.Windows.Notifications;
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
using Serilog.Events;

namespace DshDesktop.App.Composition;

/// <summary>
/// 表示 DSH Desktop 组合根（CONTEXT.md: Composition Root）：
/// 创建生成的 DI 容器、注册 Mediator 路由、接线 Runtime 监管、诊断流与日志。
/// </summary>
public sealed partial class DshCompositionRoot
{
    private readonly GeneratedMviContainer _container;
    private readonly DiagnosticsHub _diagnosticsHub = new();
    private readonly Serilog.ILogger _dshStdoutLogger;
    private readonly Serilog.ILogger _dshStderrLogger;

    private RuntimeSupervisor? _supervisor;
    private DshDesktopConfig? _config;
    private PluginProfileRepository? _pluginRepository;
    private PluginOrchestrator? _pluginOrchestrator;
    private RuntimeRepository? _runtimeRepository;
    private VelopackDesktopUpdater? _desktopUpdater;
    private BalloonNotificationService? _notificationService;
    private DiagnosticsNotificationSubscriber? _notificationSubscriber;

    // Phase 8 Issue 03：Dashboard 数据源（进程指标采样 / 启动耗时持久化 / timeline 投影的去重守卫）。
    private ProcessMetricsMonitor? _metricsMonitor;
    private TimeSpan? _lastStartupElapsedRecorded;
    private TimeSpan? _lastTimelineElapsed;

    // Phase 8 Issue 04：重接管探测（ADR-0005）/ 连续启动失败计数（ADR-0004 修订注）。
    // Phase 8 评审 F9：探测原语下沉 Infrastructure 端口（RuntimeProbe 持有 HttpClient 并随 Shutdown 释放）。
    private RuntimeProbe? _runtimeProbe;
    private readonly StartupFailureTracker _failureTracker = new();
    private RuntimeReattacher? _reattacher;

    // Phase 8 Issue 05：Settings 页端口（打开目录 / 开机自启注册表；非 Windows 平台为 null）。
    private IPathOpener? _pathOpener;
    private StartupRegistrationService? _startupRegistration;

    // config 保存串行化：快照驱动的两处保存（启动耗时 / 重接管目标）落在同一 Ready 快照上，
    // 并发 File.Create（无共享）会丢写。
    private readonly SemaphoreSlim _configSaveLock = new(1, 1);

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

        // 日志目录统一走数据根（ADR-0003：Velopack 安装后落到 <安装根>\data\logs）。
        string logDirectory = Path.Combine(DshDesktopConfigStore.DataRoot, "logs");
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                Path.Combine(logDirectory, "dsh-desktop-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14)
            .WriteTo.Sink(new DiagnosticsSink(_diagnosticsHub))
            .CreateLogger();

        _container = new GeneratedMviContainer(uiDispatcher);

        _dshStdoutLogger = Log.Logger.ForContext("Source", nameof(DiagnosticSource.DshStdout));
        _dshStderrLogger = Log.Logger.ForContext("Source", nameof(DiagnosticSource.DshStderr));

        _diagnosticsHub.Events.Subscribe(OnDiagnosticEvent);
        Log.Logger.Information("Desktop.Startup");
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
        if (OperatingSystem.IsWindows())
        {
            ConfigureTrayIcon(window, shellViewModel);
        }

        return window;
    }

    /// <summary>
    /// 接线托盘图标与气泡通知（Phase 7 Issue 03/04）：托盘静态单图标 + tooltip 投影 Runtime
    /// 生命周期（复用 AppShell RuntimeIndicator 投影链路）；菜单 = 显示主窗口 / 退出（退出走
    /// desktop.Exit → Shutdown 现状链路）；气泡订阅诊断事件流，点击仅置前主窗口（Issue 04
    /// 改为非常驻图标以合并双图标）。
    /// </summary>
    private void ConfigureTrayIcon(MainWindow window, AppShellViewModel shellViewModel)
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
        // Phase 8 Issue 05：托盘退出是显式真实退出意图，须绕过"最小化到托盘"关窗拦截。
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
            _diagnosticsHub,
            _notificationService,
            () => _config?.NotificationsEnabled ?? true);
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
    /// 后台初始化 Runtime 监管：加载配置 → Profile 种子复制 → 注册 Mediator 路由 → 订阅快照与退出事件。
    /// </summary>
    /// <param name="cancellationToken">取消标记。</param>
    public async Task InitializeRuntimeAsync(CancellationToken cancellationToken = default)
    {
        _config = await DshDesktopConfigStore.LoadOrDetectAsync(cancellationToken).ConfigureAwait(false);
        await ProfileSeeder
            .SeedIfNeededAsync(_config.DshHome, _config.SeedProfileFrom, cancellationToken)
            .ConfigureAwait(false);

        DshProcessHost processHost = new();
        processHost.OutputReceived += OnProcessOutputReceived;

        _supervisor = new RuntimeSupervisor(processHost, Log.Logger);
        _runtimeProbe = new RuntimeProbe();
        _reattacher = new RuntimeReattacher(_runtimeProbe, Log.Logger);
        _pluginRepository = new PluginProfileRepository(
            Path.Combine(_config.DshHome, "profiles", "web"),
            _config.NodePath,
            _config.PnpmCjsPath);

        ProfileSnapshotter snapshotter = new(
            Path.Combine(_config.DshHome, "profiles", "web"),
            Path.Combine(Directory.GetParent(_config.DshHome)!.FullName, "backups"),
            _config.NodePath,
            _config.PnpmCjsPath ?? string.Empty);
        _pluginOrchestrator = new PluginOrchestrator(
            _pluginRepository,
            snapshotter,
            _supervisor,
            BuildLaunchOptions,
            Log.Logger);
        _pluginOrchestrator.OperationChanged += OnPluginOperationChanged;

        _runtimeRepository = new RuntimeRepository(
            RuntimeRootDir,
            _config.NodePath,
            _config.NpmCjsPath,
            _config.DshEntryPath);

        // Velopack 自更新适配器（ADR-0003；未安装形态 no-op，不依赖 config）。
        _desktopUpdater = new VelopackDesktopUpdater(Log.Logger);

        // Phase 8 Issue 05：Settings 页端口（打开目录 / 开机自启注册表 Run 键）；非 Windows 降级 null。
        if (OperatingSystem.IsWindows())
        {
            _pathOpener = new ExplorerPathOpener();
            _startupRegistration = new StartupRegistrationService(
                new RunKeyStartupRegistrar(),
                () => Environment.ProcessPath ?? string.Empty);
        }

        RegisterRoutes();
        _supervisor.SnapshotChanged += OnRuntimeSnapshotChanged;
        _supervisor.Exited += OnRuntimeExited;

        // 安全模式状态回流（跨重启恢复，§15.1 SafeMode）。
        IMviStore<RuntimeState, RuntimeIntent, RuntimeEffect> store = ResolveRuntimeStore();
        _ = store.DispatchAsync(new RuntimeIntent.SafeModeChanged(_config.SafeMode));

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
    /// 自动启动 Runtime（§17：窗口立即可见，Runtime 后台启动）。
    /// ADR-0005：KeepRuntimeOnClose 开且存在上次记录时先尝试重接管，探测失败回退正常启动链。
    /// </summary>
    /// <param name="cancellationToken">取消标记。</param>
    public async Task AutoStartRuntimeAsync(CancellationToken cancellationToken = default)
    {
        if (await TryReattachRuntimeAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        IMviStore<RuntimeState, RuntimeIntent, RuntimeEffect> store = ResolveRuntimeStore();
        await store.DispatchAsync(new RuntimeIntent.StartRuntime(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// ADR-0005 重接管：按上次记录的 PID + 端口探测存活 Runtime（进程存活 + HTTP 健康检查）。
    /// </summary>
    /// <returns>已接管返回 true；否则（未找到 / 已退化清场）返回 false，走正常启动链。</returns>
    private async Task<bool> TryReattachRuntimeAsync(CancellationToken cancellationToken)
    {
        if (_config is not { KeepRuntimeOnClose: true, LastRuntimePid: { } pid, LastRuntimePort: { } port } config
            || _reattacher is null)
        {
            return false;
        }

        // Session URL 结论：dsh web 的 token 一次性且禁止落盘，DSH 不支持无 token 重连
        // （Workbench 刷新亦需重取最新 URL）→ 按 ADR-0005 恒退化重启；canRestoreSessionUrl 恒 false。
        ReattachOutcome outcome = await _reattacher
            .TryReattachAsync(config.Host, pid, port, canRestoreSessionUrl: false, cancellationToken)
            .ConfigureAwait(false);

        if (outcome is ReattachOutcome.Adopted)
        {
            // 当前不可达（canRestoreSessionUrl: false）；DSH 若支持无 token 重连，置 true 启用接管主路径。
            RuntimeSnapshot adopted = _supervisor!.AdoptRunning(pid, port, config.Host);
            IMviStore<RuntimeState, RuntimeIntent, RuntimeEffect> store = ResolveRuntimeStore();
            await store.DispatchAsync(
                new RuntimeIntent.RuntimeStarted(adopted.ProcessId, adopted.Port, adopted.Url ?? string.Empty),
                cancellationToken).ConfigureAwait(false);
            return true;
        }

        // NotFound / DegradedToRestart：清除陈旧记录，回退正常启动链。
        config.LastRuntimePid = null;
        config.LastRuntimePort = null;
        SaveConfigInBackground(cancellationToken);
        return false;
    }

    /// <summary>
    /// 应用退出时按 ADR-0005 分叉处置 Runtime：KeepRuntimeOnClose 开 = 只退 Desktop（Runtime 保留，
    /// 下次启动重接管）；关 = 停 Runtime 并清除重接管记录（Phase 7 现状）。
    /// </summary>
    public void Shutdown()
    {
        _notificationSubscriber?.Dispose();
        _notificationService?.Dispose();
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
        mediator.Register<SetKeepRuntimeOnCloseRequest, bool>(HandleSetKeepRuntimeOnCloseAsync);
        mediator.Register<SetAutoSafeModeOnFailureRequest, bool>(HandleSetAutoSafeModeOnFailureAsync);
        mediator.Register<SetCheckUpdatesOnStartupRequest, bool>(HandleSetCheckUpdatesOnStartupAsync);
        mediator.Register<GetPluginListRequest, IReadOnlyList<PluginInfo>>(HandleGetPluginListAsync);
        mediator.Register<SetPluginEnabledRequest, IReadOnlyList<PluginInfo>>(HandleSetPluginEnabledAsync);
        mediator.Register<UninstallPluginRequest, IReadOnlyList<PluginInfo>>(HandleUninstallPluginAsync);
        mediator.Register<InstallPluginRequest, IReadOnlyList<PluginInfo>>(HandleInstallPluginAsync);
        mediator.Register<DisableAllThirdPartyRequest, IReadOnlyList<PluginInfo>>(HandleDisableAllThirdPartyAsync);
        mediator.Register<CheckUpdatesRequest, CheckUpdatesResponse>(HandleCheckUpdatesAsync);
        mediator.Register<InstallDshRuntimeRequest, IReadOnlyList<DshRuntimeInfo>>(HandleInstallDshRuntimeAsync);
        mediator.Register<ActivateDshRuntimeRequest, IReadOnlyList<DshRuntimeInfo>>(HandleActivateDshRuntimeAsync);
        mediator.Register<UpdatePluginRequest, bool>(HandleUpdatePluginAsync);
        mediator.Register<GetSettingsInfoRequest, SettingsInfo>(HandleGetSettingsInfo);
        mediator.Register<SetDshChannelRequest, bool>(HandleSetDshChannelAsync);
        mediator.Register<SetNotificationsEnabledRequest, bool>(HandleSetNotificationsEnabledAsync);
        mediator.Register<SetMinimizeToTrayOnCloseRequest, bool>(HandleSetMinimizeToTrayOnCloseAsync);
        mediator.Register<SetLaunchOnStartupRequest, bool>(HandleSetLaunchOnStartupAsync);
        mediator.Register<SetBackgroundUpdateCheckRequest, bool>(HandleSetBackgroundUpdateCheckAsync);
        mediator.Register<SetAutoDownloadUpdatesRequest, bool>(HandleSetAutoDownloadUpdatesAsync);
        mediator.Register<OpenPathRequest, bool>(HandleOpenPath);
        mediator.Register<RunDiagnosisRequest, bool>(HandleRunDiagnosisAsync);
        mediator.Register<ExportDiagnosticsBundleRequest, bool>(HandleExportDiagnosticsBundle);
        mediator.Register<OpenLogsDirectoryRequest, bool>(HandleOpenLogsDirectory);
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

        await _desktopUpdater!.DownloadAsync(progress, cancellationToken).ConfigureAwait(false);

        // 应用并重启：进程退出，此行正常路径不返回之后的托管逻辑（§22 三套版本独立）。
        _desktopUpdater.ApplyAndRestart();
        return true;
    }

    private ValueTask<SettingsInfo> HandleGetSettingsInfo(
        GetSettingsInfoRequest request,
        CancellationToken cancellationToken)
    {
        ThrowIfNotInitialized();

        // Phase 8 Issue 05：数据与安装目录卡三行路径从实际配置推导（不写死）——
        // 插件目录 = profiles\web\node_modules；Runtime 目录含当前激活版本子目录（借用外部安装时为根）。
        string pluginsDirectory = Path.Combine(_config!.DshHome, "profiles", "web", "node_modules");
        string dshRuntimeDirectory = _config.ActiveDshRuntime is { Length: > 0 } active
            ? Path.Combine(RuntimeRootDir, active)
            : RuntimeRootDir;

        return ValueTask.FromResult(new SettingsInfo(
            _config.SafeMode,
            _config.NotificationsEnabled,
            _config.DshChannel,
            _config.NodePath,
            _config.DshHome,
            DshDesktopConfigStore.DataRoot,
            pluginsDirectory,
            dshRuntimeDirectory,
            _config.MinimizeToTrayOnClose,
            _config.LaunchOnStartup,
            _config.BackgroundUpdateCheck,
            _config.AutoDownloadUpdates));
    }

    // ===== Phase 8 Issue 05：桌面行为 / 更新策略开关持久化（照 SetNotificationsEnabled 链路） =====

    private async ValueTask<bool> HandleSetMinimizeToTrayOnCloseAsync(
        SetMinimizeToTrayOnCloseRequest request,
        CancellationToken cancellationToken)
    {
        ThrowIfNotInitialized();
        _config!.MinimizeToTrayOnClose = request.Enabled;
        await SaveConfigAsync(cancellationToken).ConfigureAwait(false);
        Log.Logger.Information("Settings.MinimizeToTrayOnClose {Enabled}", request.Enabled);
        return true;
    }

    private async ValueTask<bool> HandleSetLaunchOnStartupAsync(
        SetLaunchOnStartupRequest request,
        CancellationToken cancellationToken)
    {
        ThrowIfNotInitialized();
        _config!.LaunchOnStartup = request.Enabled;
        await SaveConfigAsync(cancellationToken).ConfigureAwait(false);

        // 注册表 Run 键写/删（仅安装形态有意义；未安装形态如实写入当前 exe 路径）。
        // 写失败抛错走失败回流：config 已落盘，UI 乐观状态不回滚，仅提示错误。
        _startupRegistration?.SetEnabled(request.Enabled);
        Log.Logger.Information("Settings.LaunchOnStartup {Enabled}", request.Enabled);
        return true;
    }

    private async ValueTask<bool> HandleSetBackgroundUpdateCheckAsync(
        SetBackgroundUpdateCheckRequest request,
        CancellationToken cancellationToken)
    {
        ThrowIfNotInitialized();
        _config!.BackgroundUpdateCheck = request.Enabled;
        await SaveConfigAsync(cancellationToken).ConfigureAwait(false);
        Log.Logger.Information("Settings.BackgroundUpdateCheck {Enabled}", request.Enabled);
        return true;
    }

    private async ValueTask<bool> HandleSetAutoDownloadUpdatesAsync(
        SetAutoDownloadUpdatesRequest request,
        CancellationToken cancellationToken)
    {
        ThrowIfNotInitialized();
        _config!.AutoDownloadUpdates = request.Enabled;
        await SaveConfigAsync(cancellationToken).ConfigureAwait(false);
        Log.Logger.Information("Settings.AutoDownloadUpdates {Enabled}", request.Enabled);
        return true;
    }

    /// <summary>
    /// 处理打开目录请求（Phase 8 Issue 05，§4.1：经 IPathOpener 端口，Presentation 不起进程）。
    /// </summary>
    private ValueTask<bool> HandleOpenPath(OpenPathRequest request, CancellationToken cancellationToken)
    {
        if (_pathOpener is null)
        {
            throw new InvalidOperationException("当前平台不支持打开目录。");
        }

        _pathOpener.Open(request.Path);
        return ValueTask.FromResult(true);
    }

    // ===== Phase 8 Issue 06：诊断中心三按钮（运行诊断 / 导出诊断包 / 打开日志目录） =====

    private static string LogDirectory => Path.Combine(DshDesktopConfigStore.DataRoot, "logs");

    /// <summary>
    /// 处理运行诊断请求：编排健康检查序列（复用现有探测原语），结果经诊断事件流回流 Live 控制台。
    /// </summary>
    private async ValueTask<bool> HandleRunDiagnosisAsync(
        RunDiagnosisRequest request,
        CancellationToken cancellationToken)
    {
        ThrowIfNotInitialized();

        RuntimeSnapshot snapshot = _supervisor!.Current;
        string profileDir = Path.Combine(_config!.DshHome, "profiles", "web");
        string host = _config.Host;
        PluginProfileRepository pluginRepository = _pluginRepository!;

        DiagnosisRunner runner = new(_diagnosticsHub);
        await runner.RunAsync(
        [
            new DiagnosisCheck("Runtime 进程健康",
                _ => Task.FromResult(snapshot.Lifecycle is RuntimeLifecycle.Running)),
            new DiagnosisCheck("HTTP 端点可达",
                token => snapshot.Port is { } port
                    ? _runtimeProbe!.IsHttpAliveAsync(host, port, token)
                    : Task.FromResult(false)),
            new DiagnosisCheck("Profile 完整性",
                _ => Task.FromResult(File.Exists(Path.Combine(profileDir, "package.json")))),
            new DiagnosisCheck("插件依赖检查",
                async token =>
                {
                    _ = await pluginRepository.ListPluginsAsync(token).ConfigureAwait(false);
                    return true;
                }),
        ], cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// 处理导出诊断包请求：打包 data/logs 为 zip（成败均写诊断流，用户在 Live 控制台可见）。
    /// </summary>
    private ValueTask<bool> HandleExportDiagnosticsBundle(
        ExportDiagnosticsBundleRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            DiagnosticsBundleExporter.Export(LogDirectory, request.DestinationPath);
            _diagnosticsHub.Publish(new DiagnosticEvent(
                DateTimeOffset.Now, DiagnosticSource.App, DiagnosticLevel.Success,
                $"✓ {DiagnosticEventNames.DiagnosisExportCompleted} {request.DestinationPath}"));
        }
        catch (Exception exception)
        {
            _diagnosticsHub.Publish(new DiagnosticEvent(
                DateTimeOffset.Now, DiagnosticSource.App, DiagnosticLevel.Error,
                $"✗ {DiagnosticEventNames.DiagnosisExportFailed} {exception.Message}"));
        }

        return ValueTask.FromResult(true);
    }

    /// <summary>
    /// 处理打开日志目录请求（复用 OpenPath 链路；路径由组合根推导）。
    /// </summary>
    private ValueTask<bool> HandleOpenLogsDirectory(
        OpenLogsDirectoryRequest request,
        CancellationToken cancellationToken)
    {
        return HandleOpenPath(new OpenPathRequest(LogDirectory), cancellationToken);
    }

    private async ValueTask<bool> HandleSetNotificationsEnabledAsync(
        SetNotificationsEnabledRequest request,
        CancellationToken cancellationToken)
    {
        ThrowIfNotInitialized();
        _config!.NotificationsEnabled = request.Enabled;
        await DshDesktopConfigStore.SaveAsync(_config, cancellationToken).ConfigureAwait(false);
        Log.Logger.Information("Settings.Notifications {Enabled}", request.Enabled);
        return true;
    }

    private async ValueTask<bool> HandleSetDshChannelAsync(
        SetDshChannelRequest request,
        CancellationToken cancellationToken)
    {
        ThrowIfNotInitialized();
        _config!.DshChannel = request.Channel;
        await DshDesktopConfigStore.SaveAsync(_config, cancellationToken).ConfigureAwait(false);
        Log.Logger.Information("Settings.DshChannel {Channel}", request.Channel);
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
        foreach (PluginInfo plugin in plugins.Where(p => p is { IsCore: false, Enabled: true }))
        {
            string? latest = await _runtimeRepository.GetLatestPluginVersionAsync(plugin.Name, cancellationToken)
                .ConfigureAwait(false);
            if (latest is not null && latest != plugin.Version)
            {
                pluginUpdates.Add(new PluginUpdateInfo(plugin.Name, plugin.Version, latest));
            }
        }

        IReadOnlyList<DshRuntimeInfo> runtimes = await _runtimeRepository
            .ListRuntimesAsync(_config!.ActiveDshRuntime, cancellationToken).ConfigureAwait(false);
        string? currentDsh = runtimes.FirstOrDefault(r => r.IsActive)?.Version;

        // Phase 8 Issue 05：自动下载安装开关（默认关）——开 = 发现 Desktop 更新后后台预下载更新包，
        // 应用与重启仍需用户在更新中心确认（复用现有 DownloadAndApply 链路；Velopack 对已下载文件幂等）。
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
        string? previous = _config!.ActiveDshRuntime;
        bool wasRunning = _supervisor!.Current.Lifecycle is RuntimeLifecycle.Running;

        await StopRuntimeIfRunningAsync(cancellationToken).ConfigureAwait(false);
        _config.ActiveDshRuntime = target;
        await DshDesktopConfigStore.SaveAsync(_config, cancellationToken).ConfigureAwait(false);
        Log.Logger.Information("Update.DshRuntime.Activated {Version}", target ?? "借用");

        if (wasRunning)
        {
            try
            {
                await _supervisor.StartAsync(BuildLaunchOptions(), cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // 激活失败回退到之前的 Runtime（Q7-A 的兜底语义）。
                _config.ActiveDshRuntime = previous;
                await DshDesktopConfigStore.SaveAsync(_config, cancellationToken).ConfigureAwait(false);
                if (previous is null || Directory.Exists(Path.Combine(RuntimeRootDir, previous)))
                {
                    await _supervisor.StartAsync(BuildLaunchOptions(), cancellationToken).ConfigureAwait(false);
                }

                throw;
            }
        }

        return await _runtimeRepository!.ListRuntimesAsync(_config.ActiveDshRuntime, cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask<bool> HandleUpdatePluginAsync(
        UpdatePluginRequest request,
        CancellationToken cancellationToken)
    {
        ThrowIfNotInitialized();
        _ = await _pluginOrchestrator!.InstallAsync($"{request.Name}@latest", cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    private async ValueTask<IReadOnlyList<PluginInfo>> HandleInstallPluginAsync(
        InstallPluginRequest request,
        CancellationToken cancellationToken)
    {
        ThrowIfNotInitialized();
        _ = await _pluginOrchestrator!.InstallAsync(request.Source, cancellationToken).ConfigureAwait(false);
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
        await StopRuntimeIfRunningAsync(cancellationToken).ConfigureAwait(false);
        await _pluginRepository!.SetEnabledAsync(request.Name, request.Enabled, cancellationToken)
            .ConfigureAwait(false);
        Log.Logger.Information("Plugin.SetEnabled {Name} {Enabled}", request.Name, request.Enabled);
        return await _pluginRepository.ListPluginsAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<IReadOnlyList<PluginInfo>> HandleUninstallPluginAsync(
        UninstallPluginRequest request,
        CancellationToken cancellationToken)
    {
        ThrowIfNotInitialized();
        await StopRuntimeIfRunningAsync(cancellationToken).ConfigureAwait(false);
        await _pluginRepository!.UninstallAsync(request.Name, cancellationToken).ConfigureAwait(false);
        Log.Logger.Information("Plugin.Uninstall {Name}", request.Name);
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
    private async Task SetSafeModeCoreAsync(bool enabled, CancellationToken cancellationToken)
    {
        ThrowIfNotInitialized();
        _config!.SafeMode = enabled;
        await SaveConfigAsync(cancellationToken).ConfigureAwait(false);
        Log.Logger.Information("Runtime.SafeMode {Enabled}", enabled);

        IMviStore<RuntimeState, RuntimeIntent, RuntimeEffect> store = ResolveRuntimeStore();
        _ = store.DispatchAsync(new RuntimeIntent.SafeModeChanged(enabled));
    }

    // ===== Phase 8 Issue 04：三策略开关持久化（照 SetSafeMode 链路） =====

    private async ValueTask<bool> HandleSetKeepRuntimeOnCloseAsync(
        SetKeepRuntimeOnCloseRequest request,
        CancellationToken cancellationToken)
    {
        ThrowIfNotInitialized();
        _config!.KeepRuntimeOnClose = request.Enabled;
        await SaveConfigAsync(cancellationToken).ConfigureAwait(false);
        Log.Logger.Information("Runtime.KeepRuntimeOnClose {Enabled}", request.Enabled);
        return true;
    }

    private async ValueTask<bool> HandleSetAutoSafeModeOnFailureAsync(
        SetAutoSafeModeOnFailureRequest request,
        CancellationToken cancellationToken)
    {
        ThrowIfNotInitialized();
        _config!.AutoSafeModeOnFailure = request.Enabled;
        await SaveConfigAsync(cancellationToken).ConfigureAwait(false);
        Log.Logger.Information("Runtime.AutoSafeModeOnFailure {Enabled}", request.Enabled);
        return true;
    }

    private async ValueTask<bool> HandleSetCheckUpdatesOnStartupAsync(
        SetCheckUpdatesOnStartupRequest request,
        CancellationToken cancellationToken)
    {
        ThrowIfNotInitialized();
        _config!.CheckUpdatesOnStartup = request.Enabled;
        await SaveConfigAsync(cancellationToken).ConfigureAwait(false);
        Log.Logger.Information("Runtime.CheckUpdatesOnStartup {Enabled}", request.Enabled);
        return true;
    }

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
            _failureTracker.RecordSuccess();
            return snapshot;
        }
        catch
        {
            await OnStartupFailureAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private async Task OnStartupFailureAsync(CancellationToken cancellationToken)
    {
        if (!_failureTracker.RecordFailure(_config!.AutoSafeModeOnFailure))
        {
            return;
        }

        Log.Logger.Error(
            DiagnosticEventNames.RuntimeAutoSafeModeEntered + " ConsecutiveFailures={Count}",
            _failureTracker.ConsecutiveFailures);
        await SetSafeModeCoreAsync(true, cancellationToken).ConfigureAwait(false);
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
            WorkingDirectory: workingDirectory,
            DshHome: _config.DshHome,
            Host: _config.Host,
            Port: _config.Port,
            StartupTimeout: TimeSpan.FromSeconds(_config.StartupTimeoutSeconds));
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

        // Phase 8 Issue 03：Dashboard 数据源——采样监控开关 + 启动耗时持久化 + timeline 投影。
        UpdateMetricsMonitor(snapshot);
        RecordStartupMetrics(snapshot);

        // Phase 8 Issue 04（ADR-0005）：Running 快照的 PID/端口写入 config，作下次启动重接管探测依据
        // （PID/端口非 Session 数据，允许落盘；Session URL 仍禁止落盘）。
        PersistReattachTarget(snapshot);
    }

    private void PersistReattachTarget(RuntimeSnapshot snapshot)
    {
        if (_config is null
            || snapshot is not { Lifecycle: RuntimeLifecycle.Running, ProcessId: { } pid, Port: { } port }
            || (_config.LastRuntimePid == pid && _config.LastRuntimePort == port))
        {
            return;
        }

        _config.LastRuntimePid = pid;
        _config.LastRuntimePort = port;
        SaveConfigInBackground();
    }

    /// <summary>
    /// 串行化保存当前 config（快照驱动的多处保存并发时防丢写）。
    /// </summary>
    private async Task SaveConfigAsync(CancellationToken cancellationToken = default)
    {
        await _configSaveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await DshDesktopConfigStore.SaveAsync(_config!, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _configSaveLock.Release();
        }
    }

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

    /// <summary>
    /// 按生命周期开关进程指标采样（仅 Running 且已知 PID 时采样；其余状态停止）。
    /// </summary>
    private void UpdateMetricsMonitor(RuntimeSnapshot snapshot)
    {
        if (snapshot.Lifecycle is RuntimeLifecycle.Running && snapshot.ProcessId is { } processId)
        {
            _metricsMonitor?.Start(processId);
        }
        else
        {
            _metricsMonitor?.Stop();
        }
    }

    /// <summary>
    /// Runtime Ready 时：timeline 阶段计时投影 Dashboard；启动耗时写 config（旧值先回流为"上次"基准）。
    /// </summary>
    private void RecordStartupMetrics(RuntimeSnapshot snapshot)
    {
        if (snapshot.Lifecycle is RuntimeLifecycle.Starting)
        {
            // 新一次启动开始：重置去重守卫。
            _lastStartupElapsedRecorded = null;
            _lastTimelineElapsed = null;
            return;
        }

        if (snapshot.StartupStage is not RuntimeStartupStage.Ready
            || snapshot.StartupElapsed is not { } elapsed
            || _config is null
            || _supervisor is null)
        {
            return;
        }

        if (_lastTimelineElapsed != elapsed)
        {
            _lastTimelineElapsed = elapsed;
            _ = ResolveDashboardStore().DispatchAsync(
                new DashboardIntent.TimelineReceived(_supervisor.LastStartupStageTimings));
        }

        if (_lastStartupElapsedRecorded != elapsed)
        {
            _lastStartupElapsedRecorded = elapsed;
            _ = ResolveDashboardStore().DispatchAsync(
                new DashboardIntent.StartupElapsedRecorded(_config.LastStartupElapsedMs));
            _config.LastStartupElapsedMs = (long)elapsed.TotalMilliseconds;
            SaveConfigInBackground();
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

    private void OnProcessOutputReceived(object? sender, ProcessOutputLineEventArgs args)
    {
        // Session token 禁止落盘（CONTEXT.md: Session URL）：日志中打码。
        string line = TokenRedactRegex().Replace(args.Line, "$1***");
        Serilog.ILogger logger = args.IsError ? _dshStderrLogger : _dshStdoutLogger;
        logger.Write(args.IsError ? LogEventLevel.Warning : LogEventLevel.Information, "{Line}", line);
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"(?i)(token=)[^\s&]+")]
    private static partial System.Text.RegularExpressions.Regex TokenRedactRegex();

    private void OnDiagnosticEvent(DiagnosticEvent diagnosticEvent)
    {
        IMviStore<DiagnosticsState, DiagnosticsIntent, DiagnosticsEffect> store =
            _container.Resolve<IMviStore<DiagnosticsState, DiagnosticsIntent, DiagnosticsEffect>>();
        _ = store.DispatchAsync(new DiagnosticsIntent.DiagnosticEventReceived(diagnosticEvent));
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
}
