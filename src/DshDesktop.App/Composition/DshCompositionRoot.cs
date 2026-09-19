using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
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
    private GitHubDesktopUpdater? _desktopUpdater;
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

    // config 落盘唯一入口：所有写路径（设置开关 / 版本切换 / 快照驱动的后台保存）
    // 共用 ConfigPersistence 同一把锁，并发写不丢写（守卫 CompositionRootGuardTests）。
    private readonly ConfigPersistence _configPersistence = new();

    // ADR-0007：Runtime 进入 Failed 后的有界自动恢复（每个失败周期最多 1 次自动重试）。
    private static readonly TimeSpan RecoveryRetryDelay = TimeSpan.FromSeconds(3);
    private readonly BoundedRecoveryPlanner _recoveryPlanner = new();

    // Runtime 状态订阅与生命周期取消源：随 Shutdown 取消/释放，
    // 避免应用退出后仍有排队中的恢复意图被派发。
    private readonly CancellationTokenSource _lifetimeSource = new();
    private IDisposable? _runtimeLifecycleSubscription;
    private RuntimeLifecycle _lastLifecycle = RuntimeLifecycle.Stopped;

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

        // ADR-0009 旧数据根一次性迁移：必须先于日志目录确立（迁移把旧根 logs 一并搬走，
        // 且迁移后日志必须落在**新**根）。失败不阻断——旧根还在，启动链按新根继续。
        // 代价注记：UI 线程同步复制（junction 实体化会放大 pnpm node_modules 体积），
        // 一次性、升级后首启发生；日志初始化依赖迁移完成故无法后台化（评审记录，可接受）。
        LegacyDataRootMigrationOutcome migrationOutcome = DshDesktopConfigStore.MigrateLegacyDataRootIfNeeded();

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
        if (migrationOutcome.Attempted)
        {
            if (migrationOutcome.Error is null)
            {
                Log.Logger.Information(
                    "Desktop.DataRoot.Migrated {DataRoot}", DshDesktopConfigStore.DataRoot);
            }
            else
            {
                Log.Logger.Warning(
                    "Desktop.DataRoot.MigrationFailed {Error}（旧根未动，按新根全新初始化继续）",
                    migrationOutcome.Error);
            }
        }
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

        _config = await DshDesktopConfigStore.LoadOrDetectAsync(cancellationToken).ConfigureAwait(false);
        ApplyTheme(_config.Theme);

        // pnpm 不可用时用宿主 npm 自举到 <dataRoot>\tools\pnpm，并回写 config。
        // 必须早于 ProfileSeeder（其 EnsureDependenciesAsync 会直用 pnpm，路径无效即抛 → 整个
        // InitializeRuntimeAsync 抛 → 被 App.axaml.cs 吞成 Desktop.Bootstrap.Failed → 窗口能开但
        // Runtime 全链路不初始化）；也早于 PluginProfileRepository / ProfileSnapshotter 构造时固化路径。
        // 无可用 node（干净机器首启）时跳过自举：PnpmProvisioner 对空 nodePath 硬抛参数校验，
        // 缺 node 是合法首启形态（SetupRuntimeAsync 负责补装），不该炸掉整个编排初始化
        // （2026-09-19 v0.1.2 便携版首启崩溃回归，守卫 CompositionRootGuardTests）。
        if (NodeProvisioner.IsNodeAvailable(_config.NodePath))
        {
            string? provisionedPnpm = await PnpmProvisioner
                .EnsureAvailableAsync(
                    DshDesktopConfigStore.DataRoot, _config.NodePath, _config.NpmCjsPath, _config.PnpmCjsPath,
                    cancellationToken)
                .ConfigureAwait(false);
            if (provisionedPnpm is not null
                && !string.Equals(provisionedPnpm, _config.PnpmCjsPath, StringComparison.Ordinal))
            {
                _config.PnpmCjsPath = provisionedPnpm;
                try
                {
                    // 持久化失败不得中断启动：内存里的 _config.PnpmCjsPath 已更新、本运行已生效；
                    // 落盘只为下次。SaveConfigAsync 用不可重入 SemaphoreSlim，此处不在持锁区间，安全。
                    await SaveConfigAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    Log.Logger.Warning(
                        exception,
                        "pnpm 自举路径已生效于本次运行，但配置落盘失败（下次启动将重新自举）：{Error}",
                        exception.Message);
                }
            }
        }
        else
        {
            Log.Logger.Warning(
                "无可用 node（NodePath={NodePath}），跳过 pnpm 自举；插件安装/更新与 profile 重建暂不可用，"
                + "待用户经首启弹窗安装 Runtime 时补全。",
                string.IsNullOrWhiteSpace(_config.NodePath) ? "<空>" : _config.NodePath);
        }

        await ProfileSeeder
            .SeedIfNeededAsync(
                _config.DshHome,
                _config.SeedProfileFrom,
                _config.NodePath,
                _config.PnpmCjsPath,
                cancellationToken)
            .ConfigureAwait(false);

        DshProcessHost processHost = new();
        processHost.OutputReceived += OnProcessOutputReceived;

        _supervisor = new RuntimeSupervisor(processHost, Log.Logger);
        _runtimeProbe = new RuntimeProbe();
        _reattacher = new RuntimeReattacher(_runtimeProbe, Log.Logger);
        WirePluginStack();

        // 核心插件自愈（2026-09-18 实机：dshmarket 被外部改出 bundles 后工作台不可用，
        // 而核心插件只读约定让 Desktop 无任何入口救回）。在任何 StartAsync 前显式修复，
        // 覆盖自动启动不经插件页的路径；失败只记日志不中断引导（同 PnpmProvisioner 约定）。
        try
        {
            await _pluginRepository.HealCoreBundlesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Log.Logger.Warning(
                exception,
                "核心插件 bundles 自愈失败（本次启动可能缺核心插件）：{Error}",
                exception.Message);
        }

        CreateRuntimeRepository();

        // 借用失效自愈（2026-09-19 v0.1.3 实机回归）：借用 Electron 安装被删后
        // HealRuntimePaths 清空 dshEntryPath，activeDshRuntime 仍为 null → BuildLaunchOptions
        // 抱空入口在 ValidateOptions 秒抛，连续 2 次进安全模式，而磁盘上自建 Runtime 完好。
        // 必须在 CreateRuntimeRepository 之后（自愈复用同一合法性规则枚举候选目录）。
        await HealActiveRuntimeSelectionAsync(cancellationToken).ConfigureAwait(false);

        // GitHub Releases 自更新适配器（Inno 安装形态；未安装形态 no-op，不依赖 config）。
        _desktopUpdater = new GitHubDesktopUpdater(Log.Logger, DesktopInfo.Version);

        // Phase 8 Issue 05：Settings 页端口（打开目录 / 开机自启注册表 Run 键）；非 Windows 降级 null。
        if (OperatingSystem.IsWindows())
        {
            _pathOpener = new ExplorerPathOpener();
            _startupRegistration = new StartupRegistrationService(
                new RunKeyStartupRegistrar(),
                () => Environment.ProcessPath ?? string.Empty);
        }

        _supervisor.SnapshotChanged += OnRuntimeSnapshotChanged;
        _supervisor.Exited += OnRuntimeExited;

        // 安全模式状态回流（跨重启恢复，§15.1 SafeMode）。
        IMviStore<RuntimeState, RuntimeIntent, RuntimeEffect> store = ResolveRuntimeStore();
        _ = store.DispatchAsync(new RuntimeIntent.SafeModeChanged(_config.SafeMode));

        // ADR-0007：Failed 后的有界自动恢复。只在「新进入 Failed」这一沿触发一次自动重试，
        // 成功后重置——不构成崩溃重启循环（ADR-0004 的禁令不变，本项是其最小放宽）。
        _runtimeLifecycleSubscription = store.States.Subscribe(OnRuntimeStateChanged);

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
    /// 构造插件栈（仓库 + 快照器 + 编排器）。node / pnpm 路径在构造期固化，
    /// 故首启安装补全工具链后必须重建（<see cref="SetupRuntimeAsync"/> 复用本方法）。
    /// 调用前置：_config 与 _supervisor 已就绪。
    /// </summary>
    private void WirePluginStack()
    {
        _pluginRepository = new PluginProfileRepository(
            Path.Combine(_config!.DshHome, "profiles", "web"),
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
            _supervisor!,
            BuildLaunchOptions,
            Log.Logger);
        _pluginOrchestrator.OperationChanged += OnPluginOperationChanged;
    }

    /// <summary>
    /// 构造 Runtime 仓库。node / npm 路径在构造期固化，工具链补全后必须重建
    /// （<see cref="SetupRuntimeAsync"/> 复用本方法）。
    /// </summary>
    private void CreateRuntimeRepository() =>
        _runtimeRepository = new RuntimeRepository(
            RuntimeRootDir,
            _config!.NodePath,
            _config.NpmCjsPath,
            _config.DshEntryPath);

    /// <summary>
    /// 借用失效自愈：未激活自建版本且借用入口不可用（空 / 文件已不存在）时，
    /// 自动激活磁盘上最新的合法自建版本并落盘；其余形态（借用可用 / 已激活 / 无候选）不动。
    /// 候选枚举与 <see cref="RuntimeRepository.ListRuntimesAsync"/> 同一合法性规则（入口包目录存在）。
    /// 刻意不复用仓库枚举：仓库返回 package.json 版本号而激活需要**目录名**，两者可能不同；
    /// 规则演进时此处与仓库必须同步（漂移风险已在评审中记录并 accepted——共用的代价是扩张公共接口）。
    /// 落盘失败不阻断：内存态已生效，本次启动照常（同 TrySaveAsync 约定，配置是派生数据）。
    /// </summary>
    private async Task HealActiveRuntimeSelectionAsync(CancellationToken cancellationToken)
    {
        bool borrowedUsable = !string.IsNullOrWhiteSpace(_config!.DshEntryPath)
            && File.Exists(_config.DshEntryPath);

        List<string> selfBuilt = [];
        if (Directory.Exists(RuntimeRootDir))
        {
            selfBuilt.AddRange(Directory
                .GetDirectories(RuntimeRootDir)
                .Where(dir => Directory.Exists(
                    Path.Combine(dir, "node_modules", "@deepseek-ai", "dsh")))
                .Select(dir => Path.GetFileName(dir)));
        }

        string? selected = ActiveRuntimeFallback.Select(
            _config.ActiveDshRuntime, borrowedUsable, selfBuilt);
        if (selected is null)
        {
            return;
        }

        _config.ActiveDshRuntime = selected;
        try
        {
            await SaveConfigAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Log.Logger.Warning(
                exception,
                "Active Runtime 自愈已生效于本次运行，但配置落盘失败（下次启动将重新自愈）：{Error}",
                exception.Message);
        }

        Log.Logger.Warning(
            DiagnosticEventNames.RuntimeActiveRuntimeAutoActivated + " {Version}（借用入口不可用，自动激活磁盘自建版本）",
            selected);
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
    /// node 自举（干净机器才下载）→ pnpm 自举 → 工具链落盘 → 重建构造期固化路径的组件 →
    /// 安装最新 DSH Runtime → 激活并落盘。
    /// 与「装不了 ≠ 起不来」的内部组件约定不同：本方法是用户显式发起的安装动作，
    /// 任何失败必须原样抛出（弹窗如实展示真实原因，静默失败会让用户误以为装好了）。
    /// 成功返回后调用方可走正常 <see cref="AutoStartRuntimeAsync"/>。
    /// </summary>
    public async Task SetupRuntimeAsync(
        IProgress<RuntimeSetupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfNotInitialized();

        if (!NodeProvisioner.IsNodeAvailable(_config!.NodePath))
        {
            IProgress<int>? nodePercent = progress is null
                ? null
                : new Progress<int>(percent =>
                    progress.Report(new RuntimeSetupProgress(RuntimeSetupText.DownloadingNodeStage, percent)));
            NodeToolchain toolchain = await NodeProvisioner
                .EnsureAvailableAsync(
                    DshDesktopConfigStore.DataRoot, nodePercent, cancellationToken)
                .ConfigureAwait(false);
            _config.NodePath = toolchain.NodePath;
            _config.NpmCjsPath = toolchain.NpmCjsPath;

            // pnpm 自举（插件链用；Runtime 安装本身只用 npm）。失败只记日志不阻断——
            // 插件功能降级 ≠ Runtime 装不上，下次启动 PnpmProvisioner 会重试。
            string? provisionedPnpm = await PnpmProvisioner
                .EnsureAvailableAsync(
                    DshDesktopConfigStore.DataRoot, _config.NodePath, _config.NpmCjsPath, _config.PnpmCjsPath,
                    cancellationToken)
                .ConfigureAwait(false);
            if (provisionedPnpm is not null)
            {
                _config.PnpmCjsPath = provisionedPnpm;
            }

            await SaveConfigAsync(cancellationToken).ConfigureAwait(false);

            // node/npm/pnpm 在组件构造期固化：工具链补全后必须重建，否则本运行插件链仍握空路径。
            CreateRuntimeRepository();
            WirePluginStack();
        }

        progress?.Report(new RuntimeSetupProgress(RuntimeSetupText.ResolvingVersionStage, -1));
        string version = await _runtimeRepository!
            .GetLatestVersionAsync(_config.DshChannel, cancellationToken)
            .ConfigureAwait(false);
        progress?.Report(new RuntimeSetupProgress(RuntimeSetupText.InstallingRuntimeStage(version), -1));
        await _runtimeRepository.InstallAsync(version, cancellationToken).ConfigureAwait(false);

        _config.ActiveDshRuntime = version;
        await SaveConfigAsync(cancellationToken).ConfigureAwait(false);
        Log.Logger.Information("Runtime.Setup.Installed {Version}", version);
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
        // ADR-0007：先停掉自动恢复——退出流程自身会触发 Runtime 退出事件（MVI 侧可能落 Failed），
        // 若此时仍允许恢复，会在退出过程中重新拉起 Runtime。
        // 只取消不释放：排队中的恢复任务仍持有该令牌，释放后访问其 Token 会抛 ObjectDisposedException。
        _runtimeLifecycleSubscription?.Dispose();
        _lifetimeSource.Cancel();

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
        mediator.Register<SetThemeRequest, bool>(HandleSetThemeAsync);
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
            _config.AutoDownloadUpdates,
            _config.Theme));
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

    /// <summary>
    /// 处理修改外观主题请求（Phase 9：即时套用 + 落盘持久，重启后经 InitializeRuntimeAsync 回流）。
    /// </summary>
    private async ValueTask<bool> HandleSetThemeAsync(
        SetThemeRequest request,
        CancellationToken cancellationToken)
    {
        ThrowIfNotInitialized();
        _config!.Theme = request.Theme;
        ApplyTheme(request.Theme);
        await SaveConfigAsync(cancellationToken).ConfigureAwait(false);
        Log.Logger.Information("Settings.Theme {Theme}", request.Theme);
        return true;
    }

    /// <summary>
    /// 套用外观主题到当前 Application（RequestedThemeVariant 为样式属性，须走 UI 线程；
    /// Application.Current 未就绪时安全跳过）。
    /// </summary>
    private static void ApplyTheme(string theme)
    {
        global::Avalonia.Application? app = global::Avalonia.Application.Current;
        if (app is null)
        {
            return;
        }

        ThemeVariant variant = string.Equals(theme, "Light", StringComparison.Ordinal)
            ? ThemeVariant.Light
            : ThemeVariant.Dark;

        Dispatcher.UIThread.Post(() => app.RequestedThemeVariant = variant);
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
        await SaveConfigAsync(cancellationToken).ConfigureAwait(false);
        Log.Logger.Information("Settings.Notifications {Enabled}", request.Enabled);
        return true;
    }

    private async ValueTask<bool> HandleSetDshChannelAsync(
        SetDshChannelRequest request,
        CancellationToken cancellationToken)
    {
        ThrowIfNotInitialized();
        _config!.DshChannel = request.Channel;
        await SaveConfigAsync(cancellationToken).ConfigureAwait(false);
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
        // 应用与重启仍需用户在更新中心确认（复用现有 DownloadAndApply 链路；重复下载覆盖同名文件，幂等）。
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

        if (wasRunning)
        {
            // 编排停止回流（同插件链先例）：先把 MVI 生命周期对齐 Stopping，
            // 随后的进程退出才不会被 Reducer 误判为崩溃（守卫 CompositionRootGuardTests）。
            _ = ResolveRuntimeStore().DispatchAsync(new RuntimeIntent.RuntimeStopOrchestrated());
        }

        await StopRuntimeIfRunningAsync(cancellationToken).ConfigureAwait(false);
        _config.ActiveDshRuntime = target;
        await SaveConfigAsync(cancellationToken).ConfigureAwait(false);
        Log.Logger.Information("Update.DshRuntime.Activated {Version}", target ?? "借用");

        if (wasRunning)
        {
            try
            {
                // 复用 MVI 启动链终点（TrackStartupAsync：失败计数进自动安全模式），
                // 成功后显式回流 RuntimeStarted，不再依赖快照对账兜底。
                RuntimeSnapshot snapshot = await TrackStartupAsync(
                    ct => _supervisor.StartAsync(BuildLaunchOptions(), ct), cancellationToken).ConfigureAwait(false);
                DispatchRuntimeStarted(snapshot);
            }
            catch (Exception exception)
            {
                // 激活失败回退到之前的 Runtime（Q7-A 的兜底语义）。
                _config.ActiveDshRuntime = previous;
                await SaveConfigAsync(cancellationToken).ConfigureAwait(false);
                if (previous is null || Directory.Exists(Path.Combine(RuntimeRootDir, previous)))
                {
                    try
                    {
                        RuntimeSnapshot snapshot = await TrackStartupAsync(
                            ct => _supervisor.StartAsync(BuildLaunchOptions(), ct), cancellationToken).ConfigureAwait(false);
                        DispatchRuntimeStarted(snapshot);
                    }
                    catch (Exception rollbackException)
                    {
                        DispatchRuntimeFailed(rollbackException.Message);
                        throw;
                    }
                }
                else
                {
                    DispatchRuntimeFailed(exception.Message);
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
        // 失败分支刻意不广播：Failed 时 PluginOperationFailed 已把真实原因写进页内 LastError，
        // 自动 LoadPlugins/CheckUpdates 会把它清掉或覆盖；且回滚后可更新列表本就未变，无需刷新。
        if (operation.Stage is PluginOperationStage.Completed)
        {
            _ = store.DispatchAsync(new PluginsIntent.LoadPlugins());

            IMviStore<UpdatesState, UpdatesIntent, UpdatesEffect> updatesStore =
                _container.Resolve<IMviStore<UpdatesState, UpdatesIntent, UpdatesEffect>>();
            _ = updatesStore.DispatchAsync(new UpdatesIntent.CheckUpdates());
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

        // Phase 8 Issue 03：Dashboard 数据源——采样监控开关 + 启动耗时持久化 + timeline 投影。
        UpdateMetricsMonitor(snapshot);
        RecordStartupMetrics(snapshot);

        // Phase 8 Issue 04（ADR-0005）：Running 快照的 PID/端口写入 config，作下次启动重接管探测依据
        // （PID/端口非 Session 数据，允许落盘；Session URL 仍禁止落盘）。
        PersistReattachTarget(snapshot);

        // 事实对账：supervisor 是真实状态源。插件链路直连 supervisor 启动且不经 MVI 启动链，
        // 可能导致进程已 Running 但 MVI 仍停在 Stopped/Failed 等。此处把状态与事实对齐，补发 RuntimeStarted。
        if (snapshot.Lifecycle is RuntimeLifecycle.Running
            && store.CurrentState.Lifecycle is not RuntimeLifecycle.Running)
        {
            DispatchRuntimeStarted(snapshot);
        }
    }

    /// <summary>
    /// 以 supervisor 快照为事实源显式回流 RuntimeStarted（激活切换链路与快照对账共用；
    /// RuntimeSnapshotReceived 刻意不迁移 Lifecycle，故须单独派发）。
    /// </summary>
    private void DispatchRuntimeStarted(RuntimeSnapshot snapshot)
    {
        _ = ResolveRuntimeStore().DispatchAsync(new RuntimeIntent.RuntimeStarted(
            snapshot.ProcessId, snapshot.Port, snapshot.Url ?? string.Empty));
    }

    /// <summary>
    /// 回流 Runtime 启动失败（激活切换链路用：整条链含回退都失败时，让页面看到真实原因
    /// 而非停在 Stopped/Starting）。
    /// </summary>
    private void DispatchRuntimeFailed(string error)
    {
        _ = ResolveRuntimeStore().DispatchAsync(new RuntimeIntent.RuntimeFailed(error));
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

    /// <summary>
    /// Runtime 生命周期变化回调（ADR-0007）：Running 时重置恢复计数；「新进入 Failed」这一沿
    /// 放行一次有界自动重试。只在沿上触发，避免同一 Failed 的重复通知反复计数。
    /// </summary>
    private void OnRuntimeStateChanged(RuntimeState state)
    {
        RuntimeLifecycle previous = _lastLifecycle;
        _lastLifecycle = state.Lifecycle;

        if (state.Lifecycle is RuntimeLifecycle.Running)
        {
            _recoveryPlanner.Reset();
            return;
        }

        if (state.Lifecycle is not RuntimeLifecycle.Failed || previous is RuntimeLifecycle.Failed)
        {
            return;
        }

        if (!_recoveryPlanner.TryBeginAttempt())
        {
            return;
        }

        Log.Logger.Warning(
            DiagnosticEventNames.RuntimeAutoRecoveryAttempted + " Attempt={Attempt}",
            BoundedRecoveryPlanner.MaxAttemptsPerFailure);

        _ = RetryStartAfterDelayAsync(_lifetimeSource.Token);
    }

    /// <summary>
    /// 延迟后派发一次启动意图（ADR-0007）。短暂延迟避开「刚失败即重试」的紧密循环，
    /// 也留给用户界面把 Failed 渲染出来。取消（应用退出）时静默放弃。
    /// </summary>
    private async Task RetryStartAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(RecoveryRetryDelay, cancellationToken).ConfigureAwait(false);
            await ResolveRuntimeStore()
                .DispatchAsync(new RuntimeIntent.StartRuntime(), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 应用退出：放弃本次自动恢复（用户可见的 Failed 面板仍在，可手动重试）。
        }
        catch (Exception exception)
        {
            // fire-and-forget：任何意外都必须自己吞掉，绝不让恢复逻辑拖垮宿主。
            Log.Logger.Warning(
                DiagnosticEventNames.RuntimeAutoRecoveryAttempted + " DispatchFailed {Error}",
                exception.Message);
        }
    }

    private void OnProcessOutputReceived(object? sender, ProcessOutputLineEventArgs args)
    {
        // Session token 禁止落盘（CONTEXT.md: Session URL）：日志中打码，规则全仓单源（Domain）。
        string line = SessionUrlRedactor.Redact(args.Line)!;
        Serilog.ILogger logger = args.IsError ? _dshStderrLogger : _dshStdoutLogger;
        logger.Write(args.IsError ? LogEventLevel.Warning : LogEventLevel.Information, "{Line}", line);
    }

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
