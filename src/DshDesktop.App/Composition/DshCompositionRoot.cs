using DshDesktop.App.Logging;
using DshDesktop.Application.Diagnostics;
using DshDesktop.Application.Plugins;
using DshDesktop.Application.Runtime;
using DshDesktop.Application.Updates;
using DshDesktop.Domain.Diagnostics;
using DshDesktop.Domain.Plugins;
using DshDesktop.Domain.Runtime;
using DshDesktop.Domain.Updates;
using DshDesktop.Infrastructure.Config;
using DshDesktop.Infrastructure.Plugins;
using DshDesktop.Infrastructure.Runtime;
using DshDesktop.Infrastructure.Updates;
using DshDesktop.Presentation.Avalonia;
using DshDesktop.Presentation.Avalonia.Composition;
using DshDesktop.Presentation.Avalonia.Features.AppShell;
using DshDesktop.Presentation.Avalonia.Features.Diagnostics;
using DshDesktop.Presentation.Avalonia.Features.Plugins;
using DshDesktop.Presentation.Avalonia.Features.Runtime;
using DshDesktop.Presentation.Avalonia.Features.Settings;
using DshDesktop.Presentation.Avalonia.Features.Updates;
using MiKiNuo.Mvi.Application.MVI.Mediator;
using MiKiNuo.Mvi.Application.MVI.Store;
using MiKiNuo.Mvi.Application.MVI.Threading;
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

    /// <summary>
    /// 获取是否处于安全模式（抑制自动启动）。
    /// </summary>
    public bool IsSafeMode => _config?.SafeMode == true;

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
        return new MainWindow(_container.Resolve<AppShellViewModel>(), _container);
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

        RegisterRoutes();
        _supervisor.SnapshotChanged += OnRuntimeSnapshotChanged;
        _supervisor.Exited += OnRuntimeExited;

        // 安全模式状态回流（跨重启恢复，§15.1 SafeMode）。
        IMviStore<RuntimeState, RuntimeIntent, RuntimeEffect> store = ResolveRuntimeStore();
        _ = store.DispatchAsync(new RuntimeIntent.SafeModeChanged(_config.SafeMode));
    }

    /// <summary>
    /// 自动启动 Runtime（§17：窗口立即可见，Runtime 后台启动）。
    /// </summary>
    /// <param name="cancellationToken">取消标记。</param>
    public async Task AutoStartRuntimeAsync(CancellationToken cancellationToken = default)
    {
        IMviStore<RuntimeState, RuntimeIntent, RuntimeEffect> store = ResolveRuntimeStore();
        await store.DispatchAsync(new RuntimeIntent.StartRuntime(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 应用退出时回收 Runtime 进程树（Q5 决策：宿主亡则 Runtime 亡）。
    /// </summary>
    public void Shutdown()
    {
        _supervisor?.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
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
        mediator.Register<SetSafeModeRequest, bool>(HandleSetSafeModeAsync);
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
        mediator.Register<DownloadAndApplyDesktopUpdateRequest, bool>(HandleDownloadAndApplyDesktopUpdateAsync);
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

        return ValueTask.FromResult(new SettingsInfo(
            _config!.SafeMode,
            _config.DshChannel,
            _config.NodePath,
            _config.DshHome,
            DshDesktopConfigStore.DataRoot));
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

        return new CheckUpdatesResponse(latestDsh, currentDsh, runtimes, pluginUpdates, latestDesktop);
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
        ThrowIfNotInitialized();
        _config!.SafeMode = request.Enabled;
        await DshDesktopConfigStore.SaveAsync(_config, cancellationToken).ConfigureAwait(false);
        Log.Logger.Information("Runtime.SafeMode {Enabled}", request.Enabled);

        IMviStore<RuntimeState, RuntimeIntent, RuntimeEffect> store = ResolveRuntimeStore();
        _ = store.DispatchAsync(new RuntimeIntent.SafeModeChanged(request.Enabled));
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
        return await _supervisor!.StartAsync(BuildLaunchOptions(), cancellationToken).ConfigureAwait(false);
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
        IMviStore<DiagnosticsState, DiagnosticsIntent, MiKiNuo.Mvi.Domain.MVI.Effect.UnitEffect> store =
            _container.Resolve<IMviStore<DiagnosticsState, DiagnosticsIntent, MiKiNuo.Mvi.Domain.MVI.Effect.UnitEffect>>();
        _ = store.DispatchAsync(new DiagnosticsIntent.DiagnosticEventReceived(diagnosticEvent));
    }

    private IMviStore<RuntimeState, RuntimeIntent, RuntimeEffect> ResolveRuntimeStore()
    {
        return _container.Resolve<IMviStore<RuntimeState, RuntimeIntent, RuntimeEffect>>();
    }

    private void ThrowIfNotInitialized()
    {
        if (_supervisor is null || _config is null)
        {
            throw new InvalidOperationException("Runtime 编排尚未初始化完成，请稍候再试。");
        }
    }
}
