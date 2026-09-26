using System.Collections.Immutable;
using DshDesktop.Application.Plugins;
using DshDesktop.Application.Runtime;
using DshDesktop.Application.Updates;
using DshDesktop.Domain.Plugins;
using Serilog;

namespace DshDesktop.Application.Bootstrap;

/// <summary>
/// Runtime 启动引导编排模块（CONTEXT.md: Runtime Bootstrapper）：接管组合根的引导职责——
/// 配置加载 → 主题套用 → pnpm 门控自举 → Profile 种子 → 栈装配 → 引导期自愈登记册，
/// 以及首启安装链（RebuildStackAsync）与反应式崩溃漂移自愈。
/// 经端口/工厂抽象消费 Infrastructure 具体件，Application 不新增对 Infrastructure 的项目引用。
/// </summary>
public sealed class RuntimeBootstrapper
{
    private readonly IRuntimeConfig _config;
    private readonly IThemeApplier _themeApplier;
    private readonly INodeProvisioner _nodeProvisioner;
    private readonly IPnpmProvisioner _pnpmProvisioner;
    private readonly IProfileSeeder _profileSeeder;
    private readonly IRuntimeStackFactory _stackFactory;
    private readonly IIncompatiblePluginCrashProbe _crashProbe;
    private readonly IProfileManifestNormalizer _manifestNormalizer;
    private readonly string _dataRoot;
    private readonly ILogger _logger;

    // 每会话每插件只试一次崩溃漂移自愈，防自愈-失败循环（原组合根 _crashHealAttempted 字段）。
    private readonly HashSet<string> _crashHealAttempted = new(StringComparer.Ordinal);

    // 引导期自愈条目表（按序执行）。每条 = (名称, 失败时是否只告警不中断, 动作)。
    // 动作通过构造期闭包捕获实例依赖，故条目表为 readonly 不可变数组（非 static）：
    // 语义上等同静态只读登记册，仅因依赖捕获而无法 static。
    // 频次/幂等语义由各动作自身承载：CoreBundles 与 ActiveRuntime 为幂等（谓词不成立时零动作），
    // ProfileManifestNormalize 为每次启动都执行（无谓词，恒动作）。
    // 顺序按语义分组而非照搬历史相对序：归一化排最后——ProfileSeeder 先于 RunBootHeals 执行、
    // 其 EnsureDependenciesAsync 重建分支自带归一化，且归一化幂等不重入（code-review 2026-09-26）。
    private readonly ImmutableArray<BootHealEntry> _entries;

    private RuntimeStack? _stack;

    private readonly record struct BootHealEntry(
        string Name,
        bool SwallowOnFailure,
        Func<CancellationToken, Task> Run);

    /// <summary>
    /// 构造启动引导编排器。
    /// </summary>
    /// <param name="config">配置端口（加载、编排期读写、落盘）。</param>
    /// <param name="themeApplier">主题套用端口（配置加载后同一时点触发）。</param>
    /// <param name="nodeProvisioner">node 自举端口。</param>
    /// <param name="pnpmProvisioner">pnpm 自举端口。</param>
    /// <param name="profileSeeder">Profile 种子复制端口。</param>
    /// <param name="stackFactory">运行时栈装配工厂端口。</param>
    /// <param name="crashProbe">崩溃肇事插件探针端口。</param>
    /// <param name="manifestNormalizer">Profile 清单归一化端口。</param>
    /// <param name="dataRoot">数据根（自举产物落 &lt;dataRoot&gt;\tools）。</param>
    /// <param name="logger">日志。</param>
    public RuntimeBootstrapper(
        IRuntimeConfig config,
        IThemeApplier themeApplier,
        INodeProvisioner nodeProvisioner,
        IPnpmProvisioner pnpmProvisioner,
        IProfileSeeder profileSeeder,
        IRuntimeStackFactory stackFactory,
        IIncompatiblePluginCrashProbe crashProbe,
        IProfileManifestNormalizer manifestNormalizer,
        string dataRoot,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(themeApplier);
        ArgumentNullException.ThrowIfNull(nodeProvisioner);
        ArgumentNullException.ThrowIfNull(pnpmProvisioner);
        ArgumentNullException.ThrowIfNull(profileSeeder);
        ArgumentNullException.ThrowIfNull(stackFactory);
        ArgumentNullException.ThrowIfNull(crashProbe);
        ArgumentNullException.ThrowIfNull(manifestNormalizer);
        ArgumentNullException.ThrowIfNull(dataRoot);
        ArgumentNullException.ThrowIfNull(logger);

        _config = config;
        _themeApplier = themeApplier;
        _nodeProvisioner = nodeProvisioner;
        _pnpmProvisioner = pnpmProvisioner;
        _profileSeeder = profileSeeder;
        _stackFactory = stackFactory;
        _crashProbe = crashProbe;
        _manifestNormalizer = manifestNormalizer;
        _dataRoot = dataRoot;
        _logger = logger;

        _entries = ImmutableArray.Create(
            new BootHealEntry(
                "CoreBundles",
                SwallowOnFailure: true,
                ct => _stack!.PluginRepository.HealCoreBundlesAsync(ct)),
            new BootHealEntry(
                "ActiveRuntimeSelection",
                SwallowOnFailure: false,
                HealActiveRuntimeAsync),
            new BootHealEntry(
                "ProfileManifestNormalize",
                SwallowOnFailure: false,
                ct =>
                {
                    _manifestNormalizer.NormalizeToFlatModel(
                        Path.Combine(_config.DshHome, "profiles", "web"));
                    return Task.CompletedTask;
                }));
    }

    /// <summary>
    /// 执行启动编排并回流装配好的运行时栈：配置加载 → 主题套用 → pnpm 门控自举 →
    /// Profile 种子 → 栈装配 → 引导期自愈登记册。
    /// 错误语义：pnpm 自举/落盘失败只告警不中断；种子失败原样上抛（初始化整体失败）。
    /// </summary>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>装配好的运行时栈。</returns>
    public async Task<RuntimeStack> RunAsync(CancellationToken cancellationToken)
    {
        await _config.LoadAsync(cancellationToken).ConfigureAwait(false);
        _themeApplier.ApplyTheme(_config.Theme);

        await ProvisionPnpmIfNodeAvailableAsync(cancellationToken).ConfigureAwait(false);

        await _profileSeeder
            .SeedIfNeededAsync(
                _config.DshHome,
                _config.SeedProfileFrom,
                _config.NodePath,
                _config.PnpmCjsPath,
                cancellationToken)
            .ConfigureAwait(false);

        RuntimeStack stack = _stackFactory.Create(_config);
        _stack = stack;

        await RunBootHealsAsync(cancellationToken).ConfigureAwait(false);
        return stack;
    }

    /// <summary>
    /// 首启安装编排（原组合根 SetupRuntimeAsync）：node 自举（干净机器才下载）→ pnpm 自举 →
    /// 工具链落盘 → 重建构造期固化路径的栈子集 → 安装最新 DSH Runtime → 激活并落盘。
    /// 与「装不了 ≠ 起不来」的内部组件约定不同：本方法是用户显式发起的安装动作，
    /// 任何失败必须原样抛出（弹窗如实展示真实原因）。
    /// </summary>
    /// <param name="progress">进度投影（可为 null）。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>当前运行时栈（工具链补全后绑定件已重建时为换实例后的新栈）。</returns>
    public async Task<RuntimeStack> RebuildStackAsync(
        IProgress<RuntimeSetupProgress>? progress,
        CancellationToken cancellationToken)
    {
        RuntimeStack stack = _stack
            ?? throw new InvalidOperationException("Runtime 编排尚未初始化完成，请稍候再试。");

        if (!_nodeProvisioner.IsNodeAvailable(_config.NodePath))
        {
            IProgress<int>? nodePercent = progress is null
                ? null
                : new Progress<int>(percent =>
                    progress.Report(new RuntimeSetupProgress(RuntimeSetupText.DownloadingNodeStage, percent)));
            NodeProvisionResult toolchain = await _nodeProvisioner
                .EnsureAvailableAsync(_dataRoot, nodePercent, cancellationToken)
                .ConfigureAwait(false);
            _config.NodePath = toolchain.NodePath;
            _config.NpmCjsPath = toolchain.NpmCjsPath;

            // pnpm 自举（插件链用；Runtime 安装本身只用 npm）。失败只记日志不阻断——
            // 插件功能降级 ≠ Runtime 装不上，下次启动 PnpmProvisioner 会重试。
            string? provisionedPnpm = await _pnpmProvisioner
                .EnsureAvailableAsync(
                    _dataRoot, _config.NodePath, _config.NpmCjsPath, _config.PnpmCjsPath, cancellationToken)
                .ConfigureAwait(false);
            if (provisionedPnpm is not null)
            {
                _config.PnpmCjsPath = provisionedPnpm;
            }

            await _config.PersistAsync(cancellationToken).ConfigureAwait(false);

            // node/npm/pnpm 在组件构造期固化：工具链补全后必须重建，否则本运行插件链仍握空路径。
            ToolchainBoundStack rebound = _stackFactory.RebuildToolchainBound(_config, stack.Supervisor);
            stack = stack with
            {
                PluginRepository = rebound.PluginRepository,
                PluginOrchestrator = rebound.PluginOrchestrator,
                RuntimeRepository = rebound.RuntimeRepository,
            };
            _stack = stack;
        }

        progress?.Report(new RuntimeSetupProgress(RuntimeSetupText.ResolvingVersionStage, -1));
        string version = await stack.RuntimeRepository
            .GetLatestVersionAsync(_config.DshChannel, cancellationToken)
            .ConfigureAwait(false);
        progress?.Report(new RuntimeSetupProgress(RuntimeSetupText.InstallingRuntimeStage(version), -1));
        await stack.RuntimeRepository.InstallAsync(version, cancellationToken).ConfigureAwait(false);

        _config.ActiveDshRuntime = version;
        await _config.PersistAsync(cancellationToken).ConfigureAwait(false);
        _logger.Information("Runtime.Setup.Installed {Version}", version);
        return stack;
    }

    /// <summary>
    /// pnpm 不可用时用宿主 npm 自举到 &lt;dataRoot&gt;\tools\pnpm，并回写 config。
    /// 必须早于 ProfileSeeder（其 EnsureDependenciesAsync 会直用 pnpm，路径无效即抛 → 整个
    /// 初始化抛 → 被 App.axaml.cs 吞成 Desktop.Bootstrap.Failed → 窗口能开但
    /// Runtime 全链路不初始化）；也早于插件栈构造时固化路径。
    /// 无可用 node（干净机器首启）时跳过自举：缺 node 是合法首启形态（RebuildStackAsync 负责补装），
    /// 不该炸掉整个编排初始化（2026-09-19 v0.1.2 便携版首启崩溃回归——
    /// 原 CompositionRootGuardTests 文本守卫已换算为 RuntimeBootstrapperRunTests 真测试）。
    /// </summary>
    private async Task ProvisionPnpmIfNodeAvailableAsync(CancellationToken cancellationToken)
    {
        if (!_nodeProvisioner.IsNodeAvailable(_config.NodePath))
        {
            _logger.Warning(
                "无可用 node（NodePath={NodePath}），跳过 pnpm 自举；插件安装/更新与 profile 重建暂不可用，"
                + "待用户经首启弹窗安装 Runtime 时补全。",
                string.IsNullOrWhiteSpace(_config.NodePath) ? "<空>" : _config.NodePath);
            return;
        }

        string? provisionedPnpm = await _pnpmProvisioner
            .EnsureAvailableAsync(
                _dataRoot, _config.NodePath, _config.NpmCjsPath, _config.PnpmCjsPath, cancellationToken)
            .ConfigureAwait(false);
        if (provisionedPnpm is null
            || string.Equals(provisionedPnpm, _config.PnpmCjsPath, StringComparison.Ordinal))
        {
            return;
        }

        _config.PnpmCjsPath = provisionedPnpm;
        try
        {
            // 持久化失败不得中断启动：内存里的 PnpmCjsPath 已更新、本运行已生效；
            // 落盘只为下次。落盘经配置端口（与组合根其余写路径同一把 ConfigPersistence 锁）。
            await _config.PersistAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.Warning(
                exception,
                "pnpm 自举路径已生效于本次运行，但配置落盘失败（下次启动将重新自举）：{Error}",
                exception.Message);
        }
    }

    /// <summary>
    /// 按序执行引导期自愈条目。核心 bundles 条目失败只告警不中断后续；其余条目失败原样上抛。
    /// </summary>
    /// <param name="cancellationToken">取消标记。</param>
    private async Task RunBootHealsAsync(CancellationToken cancellationToken)
    {
        foreach (BootHealEntry entry in _entries)
        {
            try
            {
                await entry.Run(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (entry.SwallowOnFailure)
            {
                // 核心 bundles 自愈失败（磁盘/权限）不得中断引导（同 PnpmProvisioner 约定）：只记日志。
                _logger.Warning(
                    exception,
                    "引导期自愈[{Name}]失败（不影响启动）：{Error}",
                    entry.Name,
                    exception.Message);
            }
        }
    }

    /// <summary>
    /// 借用失效自愈（原组合根 HealActiveRuntimeSelectionAsync）：未激活自建版本且借用入口不可用
    /// （空 / 文件已不存在）时，自动激活磁盘上最新的合法自建版本并落盘；其余形态（借用可用 /
    /// 已激活 / 无候选）不动。候选枚举与 ActiveRuntimeFallback.Select 同一合法性规则。
    /// 落盘失败不阻断：内存态已生效，本次启动照常。
    /// </summary>
    private async Task HealActiveRuntimeAsync(CancellationToken cancellationToken)
    {
        bool borrowedUsable = !string.IsNullOrWhiteSpace(_config.DshEntryPath)
            && File.Exists(_config.DshEntryPath);

        string runtimeRootDir = Path.Combine(
            Directory.GetParent(_config.DshHome)!.FullName, "runtime", "dsh");
        List<string> selfBuilt = [];
        if (Directory.Exists(runtimeRootDir))
        {
            selfBuilt.AddRange(Directory
                .GetDirectories(runtimeRootDir)
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
            await _config.PersistAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.Warning(
                exception,
                "Active Runtime 自愈已生效于本次运行，但配置落盘失败（下次启动将重新自愈）：{Error}",
                exception.Message);
        }

        _logger.Warning(
            "Runtime.ActiveRuntime.AutoActivated {Version}（借用入口不可用，自动激活磁盘自建版本）",
            selected);
    }

    /// <summary>
    /// 崩溃漂移自愈入口（原组合根 TrackStartupAsync catch 分支）：解析肇事插件并经编排器事务化升级
    /// （快照/停/变更/校验/启动/健康/回滚）。每会话每插件只试一次，失败回落原失败计数链，绝不循环。
    /// 返回"自愈是否成功"——成功时组合根照旧 RecordSuccess 并返回 supervisor.Current。
    /// </summary>
    /// <param name="startFailureMessage">启动失败异常消息（内含 stderr 末尾）。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>自愈成功（已触发事务化升级并成功）返回 true；否则 false。</returns>
    public async ValueTask<bool> TryHealIncompatiblePluginCrashAsync(
        string startFailureMessage,
        CancellationToken cancellationToken)
    {
        string? offender = _crashProbe.TryParseOffender(startFailureMessage);
        if (offender is null)
        {
            return false;
        }

        // 每会话每插件只试一次（_crashHealAttempted），失败回落原失败计数链，绝不循环。
        if (!_crashHealAttempted.Add(offender))
        {
            return false;
        }

        _logger.Warning("Runtime.Start.CrashHeal.Begin {PluginName}", offender);
        try
        {
            _ = await _stack!.PluginOrchestrator
                .InstallAsync($"{offender}@latest", PluginOperationKind.Update, cancellationToken)
                .ConfigureAwait(false);
            _logger.Information("Runtime.Start.CrashHeal.Success {PluginName}", offender);
            return true;
        }
        catch (Exception healException)
        {
            _logger.Warning(
                "Runtime.Start.CrashHeal.Failed {PluginName} {Error}", offender, healException.Message);
            return false;
        }
    }
}
