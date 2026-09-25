using System.Collections.Immutable;
using DshDesktop.Application.Plugins;
using DshDesktop.Application.Updates;
using DshDesktop.Domain.Plugins;
using Serilog;

namespace DshDesktop.Application.Bootstrap;

/// <summary>
/// Runtime 自愈登记册外壳（组合根拆分批 1）：把散落组合根的三处引导期自愈（核心 bundles /
/// ActiveRuntime 失效回退 / Profile manifest 归一化）与一处反应式自愈（崩溃漂移）收口到
/// Application 层，经既有/新建抽象消费 Infrastructure 具体件，不新增 Application→Infrastructure
/// 项目引用。行为零变化（safe-refactor）。
/// </summary>
public sealed class RuntimeBootstrapper
{
    private readonly IPluginManager _pluginManager;
    private readonly IPluginOrchestrator _pluginOrchestrator;
    private readonly IRuntimeBootstrapConfig _config;
    private readonly IIncompatiblePluginCrashProbe _crashProbe;
    private readonly IProfileManifestNormalizer _manifestNormalizer;
    private readonly string _runtimeRootDir;
    private readonly string _profileDir;
    private readonly ILogger _logger;

    // 每会话每插件只试一次崩溃漂移自愈，防自愈-失败循环（原组合根 _crashHealAttempted 字段）。
    private readonly HashSet<string> _crashHealAttempted = new(StringComparer.Ordinal);

    // 引导期自愈条目表（按序执行）。每条 = (名称, 失败时是否只告警不中断, 动作)。
    // 动作通过构造期闭包捕获实例依赖，故条目表为 readonly 不可变数组（非 static）：
    // 语义上等同静态只读登记册，仅因依赖捕获而无法 static。
    // 频次/幂等语义由各动作自身承载：CoreBundles 与 ActiveRuntime 为幂等（谓词不成立时零动作），
    // ProfileManifestNormalize 为每次启动都执行（无谓词，恒动作）。
    //
    // 顺序意图（防误改）：CoreBundles → ActiveRuntimeSelection → ProfileManifestNormalize。
    // 这是按「依赖/语义分组」定的序，并非照搬原始相对序——原始 normalize 调用内嵌于
    // ProfileSeeder.SeedIfNeededAsync（在 ActiveRuntime 之前执行）。本登记册把 normalize 排在最后，
    // 依据是：SeedIfNeededAsync 先于 RunBootHealsAsync 执行（InitializeRuntimeAsync 调用序），且其
    // 内部 :115 重建分支自带 NormalizeToFlatModel，故即便 normalize 条目排在 ActiveRuntime 之后，
    // 对 profile 的归一化仍已由 Seed 先行完成、幂等不重入；三者互无硬数据依赖，顺序为语义分组而非约束。
    private readonly ImmutableArray<BootHealEntry> _entries;

    private readonly record struct BootHealEntry(
        string Name,
        bool SwallowOnFailure,
        Func<CancellationToken, Task> Run);

    /// <summary>
    /// 构造自愈登记册外壳。
    /// </summary>
    /// <param name="pluginManager">插件管理端口（核心 bundles 修复走此，复用既有 IPluginManager.HealCoreBundlesAsync）。</param>
    /// <param name="pluginOrchestrator">插件编排端口（崩溃漂移自愈走事务化升级）。</param>
    /// <param name="config">引导期配置读写端口（ActiveRuntime 激活态 + 落盘）。</param>
    /// <param name="crashProbe">崩溃肇事插件探针端口。</param>
    /// <param name="manifestNormalizer">Profile 清单归一化端口。</param>
    /// <param name="runtimeRootDir">Runtime 根目录（枚举自建版本目录）。</param>
    /// <param name="profileDir">Profile 目录（归一化目标）。</param>
    /// <param name="logger">日志。</param>
    public RuntimeBootstrapper(
        IPluginManager pluginManager,
        IPluginOrchestrator pluginOrchestrator,
        IRuntimeBootstrapConfig config,
        IIncompatiblePluginCrashProbe crashProbe,
        IProfileManifestNormalizer manifestNormalizer,
        string runtimeRootDir,
        string profileDir,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(pluginManager);
        ArgumentNullException.ThrowIfNull(pluginOrchestrator);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(crashProbe);
        ArgumentNullException.ThrowIfNull(manifestNormalizer);
        ArgumentNullException.ThrowIfNull(runtimeRootDir);
        ArgumentNullException.ThrowIfNull(profileDir);
        ArgumentNullException.ThrowIfNull(logger);

        _pluginManager = pluginManager;
        _pluginOrchestrator = pluginOrchestrator;
        _config = config;
        _crashProbe = crashProbe;
        _manifestNormalizer = manifestNormalizer;
        _runtimeRootDir = runtimeRootDir;
        _profileDir = profileDir;
        _logger = logger;

        _entries = ImmutableArray.Create(
            new BootHealEntry(
                "CoreBundles",
                SwallowOnFailure: true,
                ct => _pluginManager.HealCoreBundlesAsync(ct)),
            new BootHealEntry(
                "ActiveRuntimeSelection",
                SwallowOnFailure: false,
                HealActiveRuntimeAsync),
            new BootHealEntry(
                "ProfileManifestNormalize",
                SwallowOnFailure: false,
                ct =>
                {
                    _manifestNormalizer.NormalizeToFlatModel(_profileDir);
                    return Task.CompletedTask;
                }));
    }

    /// <summary>
    /// 按序执行引导期自愈条目。核心 bundles 条目失败只告警不中断后续；其余条目失败原样上抛。
    /// </summary>
    /// <param name="cancellationToken">取消标记。</param>
    public async Task RunBootHealsAsync(CancellationToken cancellationToken)
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

        List<string> selfBuilt = [];
        if (Directory.Exists(_runtimeRootDir))
        {
            selfBuilt.AddRange(Directory
                .GetDirectories(_runtimeRootDir)
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
            _ = await _pluginOrchestrator
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
