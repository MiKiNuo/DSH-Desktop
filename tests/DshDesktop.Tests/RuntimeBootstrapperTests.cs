using DshDesktop.Application.Bootstrap;
using DshDesktop.Application.Plugins;
using DshDesktop.Domain.Plugins;
using DshDesktop.Domain.Runtime;
using Serilog;
using Serilog.Core;

namespace DshDesktop.Tests;

/// <summary>
/// RuntimeBootstrapper 测试（组合根拆分批 1）：覆盖引导期自愈登记册的按序执行、幂等、
/// 核心 bundles 失败吞错，以及反应式崩溃漂移自愈的三分支。接缝为 RuntimeBootstrapper 的公共
/// API；Infrastructure 具体件经新建最小接口（IRuntimeBootstrapConfig /
/// IIncompatiblePluginCrashProbe / IProfileManifestNormalizer）以 Fake 注入，不触及真实文件系统逻辑
/// （ActiveRuntime 枚举走真实临时目录，验证与 ActiveRuntimeFallback.Select 的集成）。
/// </summary>
public sealed class RuntimeBootstrapperTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "dsh-bootstrapper-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Test]
    public async Task RunBootHealsAsync_RunsEntriesInDeclaredOrder()
    {
        List<string> order = [];
        var pluginManager = new RecordingPluginManager(order);
        var config = new RecordingConfig(order) { DshEntryPath = Path.Combine(_tempDir, "missing.js"), ActiveDshRuntime = null };
        var normalizer = new RecordingNormalizer(order);
        // 借用失效 + 有自建候选 ⇒ ActiveRuntime 条目落盘并写入顺序。
        string runtimeRoot = CreateRuntimeRoot(["0.1.5", "0.1.10"]);
        var bootstrapper = new RuntimeBootstrapper(
            pluginManager, new NoopOrchestrator(), config, new FixedProbe(null),
            normalizer, runtimeRoot, _tempDir, Logger.None);

        await bootstrapper.RunBootHealsAsync(CancellationToken.None);

        await Assert.That(order.Count).IsEqualTo(3);
        await Assert.That(order[0]).IsEqualTo("CoreBundles");
        await Assert.That(order[1]).IsEqualTo("ActiveRuntimeSelection");
        await Assert.That(order[2]).IsEqualTo("ProfileManifestNormalize");
    }

    [Test]
    public async Task RunBootHealsAsync_CoreBundlesFailureIsSwallowed_StillRunsRemaining()
    {
        List<string> order = [];
        var pluginManager = new ThrowingPluginManager();
        var config = new RecordingConfig(order) { DshEntryPath = Path.Combine(_tempDir, "missing.js"), ActiveDshRuntime = null };
        var normalizer = new RecordingNormalizer(order);
        string runtimeRoot = CreateRuntimeRoot(["0.1.5"]);
        var bootstrapper = new RuntimeBootstrapper(
            pluginManager, new NoopOrchestrator(), config, new FixedProbe(null),
            normalizer, runtimeRoot, _tempDir, Logger.None);

        // 核心 bundles 抛错不得上抛，ActiveRuntime + 归一化仍执行；若上抛则测试直接失败。
        await bootstrapper.RunBootHealsAsync(CancellationToken.None);

        await Assert.That(order.Count).IsEqualTo(2);
        await Assert.That(order[0]).IsEqualTo("ActiveRuntimeSelection");
        await Assert.That(order[1]).IsEqualTo("ProfileManifestNormalize");
    }

    [Test]
    public async Task HealActiveRuntime_BorrowedEntryUsable_NoChange()
    {
        var config = new RecordingConfig(new List<string>()) { DshEntryPath = CreateFile("entry.js"), ActiveDshRuntime = null };
        var bootstrapper = new RuntimeBootstrapper(
            new NoopPluginManager(), new NoopOrchestrator(), config, new FixedProbe(null),
            new NoopNormalizer(), _tempDir, _tempDir, Logger.None);

        await bootstrapper.RunBootHealsAsync(CancellationToken.None);

        // 借用可用 ⇒ Select 返回 null ⇒ 不写激活态、不落盘。
        await Assert.That(config.ActiveDshRuntime).IsNull();
        await Assert.That(config.PersistCount).IsEqualTo(0);
    }

    [Test]
    public async Task HealActiveRuntime_BorrowedDeadWithSelfBuilt_SelectsHighestAndPersists()
    {
        var config = new RecordingConfig(new List<string>()) { DshEntryPath = Path.Combine(_tempDir, "missing.js"), ActiveDshRuntime = null };
        string runtimeRoot = CreateRuntimeRoot(["0.1.9", "0.1.10", "0.1.5-rc.1"]);
        var bootstrapper = new RuntimeBootstrapper(
            new NoopPluginManager(), new NoopOrchestrator(), config, new FixedProbe(null),
            new NoopNormalizer(), runtimeRoot, _tempDir, Logger.None);

        await bootstrapper.RunBootHealsAsync(CancellationToken.None);

        // 借用失效 + 有自建候选 ⇒ 选数值最高版本并落盘。
        await Assert.That(config.ActiveDshRuntime).IsEqualTo("0.1.10");
        await Assert.That(config.PersistCount).IsEqualTo(1);
    }

    [Test]
    public async Task HealActiveRuntime_NoCandidate_NoChange()
    {
        var config = new RecordingConfig(new List<string>()) { DshEntryPath = Path.Combine(_tempDir, "missing.js"), ActiveDshRuntime = null };
        // runtimeRoot 下无含 @deepseek-ai/dsh 的合法版本目录 ⇒ 无候选。
        string runtimeRoot = CreateRuntimeRootWithoutDeepseek();
        var bootstrapper = new RuntimeBootstrapper(
            new NoopPluginManager(), new NoopOrchestrator(), config, new FixedProbe(null),
            new NoopNormalizer(), runtimeRoot, _tempDir, Logger.None);

        await bootstrapper.RunBootHealsAsync(CancellationToken.None);

        await Assert.That(config.ActiveDshRuntime).IsNull();
        await Assert.That(config.PersistCount).IsEqualTo(0);
    }

    [Test]
    public async Task NormalizeEntry_RunsEveryTime()
    {
        var normalizer = new RecordingNormalizer(new List<string>());
        var config = new RecordingConfig(new List<string>()) { DshEntryPath = CreateFile("entry.js"), ActiveDshRuntime = null };
        var bootstrapper = new RuntimeBootstrapper(
            new NoopPluginManager(), new NoopOrchestrator(), config, new FixedProbe(null),
            normalizer, _tempDir, _tempDir, Logger.None);

        await bootstrapper.RunBootHealsAsync(CancellationToken.None);
        await bootstrapper.RunBootHealsAsync(CancellationToken.None);

        await Assert.That(normalizer.CallCount).IsEqualTo(2);
    }

    [Test]
    public async Task TryHealIncompatiblePluginCrash_ProbeMiss_ReturnsFalse_NoInstall()
    {
        var orchestrator = new RecordingOrchestrator();
        var bootstrapper = new RuntimeBootstrapper(
            new NoopPluginManager(), orchestrator, new NoopConfig(), new FixedProbe(null),
            new NoopNormalizer(), _tempDir, _tempDir, Logger.None);

        bool healed = await bootstrapper.TryHealIncompatiblePluginCrashAsync("无关错误", CancellationToken.None);

        await Assert.That(healed).IsFalse();
        await Assert.That(orchestrator.InstallCalls.Count).IsEqualTo(0);
    }

    [Test]
    public async Task TryHealIncompatiblePluginCrash_ProbeHitNotAttempted_InstallsAndReturnsTrue()
    {
        var orchestrator = new RecordingOrchestrator();
        var bootstrapper = new RuntimeBootstrapper(
            new NoopPluginManager(), orchestrator, new NoopConfig(), new FixedProbe("dshmarket"),
            new NoopNormalizer(), _tempDir, _tempDir, Logger.None);

        bool healed = await bootstrapper.TryHealIncompatiblePluginCrashAsync(
            "does not provide an export ...", CancellationToken.None);

        await Assert.That(healed).IsTrue();
        await Assert.That(orchestrator.InstallCalls.Count).IsEqualTo(1);
        await Assert.That(orchestrator.InstallCalls[0].Source).IsEqualTo("dshmarket@latest");
        await Assert.That(orchestrator.InstallCalls[0].Kind).IsEqualTo(PluginOperationKind.Update);
    }

    [Test]
    public async Task TryHealIncompatiblePluginCrash_ProbeHitAlreadyAttempted_NoInstall()
    {
        var orchestrator = new RecordingOrchestrator();
        var bootstrapper = new RuntimeBootstrapper(
            new NoopPluginManager(), orchestrator, new NoopConfig(), new FixedProbe("dshmarket"),
            new NoopNormalizer(), _tempDir, _tempDir, Logger.None);

        bool first = await bootstrapper.TryHealIncompatiblePluginCrashAsync("does not provide an export", CancellationToken.None);
        bool second = await bootstrapper.TryHealIncompatiblePluginCrashAsync("does not provide an export", CancellationToken.None);

        // 每会话每插件只试一次：首次成功，第二次不再调用 InstallAsync。
        await Assert.That(first).IsTrue();
        await Assert.That(second).IsFalse();
        await Assert.That(orchestrator.InstallCalls.Count).IsEqualTo(1);
    }

    // ===== 测试夹具 =====

    private string CreateFile(string name)
    {
        Directory.CreateDirectory(_tempDir);
        string path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    private string CreateRuntimeRoot(string[] versions)
    {
        string root = Path.Combine(_tempDir, "runtime-" + Guid.NewGuid().ToString("N"));
        foreach (string version in versions)
        {
            string dir = Path.Combine(root, version);
            Directory.CreateDirectory(Path.Combine(dir, "node_modules", "@deepseek-ai", "dsh"));
        }

        return root;
    }

    private string CreateRuntimeRootWithoutDeepseek()
    {
        string root = Path.Combine(_tempDir, "runtime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "0.1.5", "node_modules", "something-else"));
        return root;
    }

    private sealed class NoopPluginManager : IPluginManager
    {
        public Task<IReadOnlyList<PluginInfo>> ListPluginsAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<PluginInfo>>(Array.Empty<PluginInfo>());

        public Task SetEnabledAsync(string name, bool enabled, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task UninstallAsync(string name, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<string> InstallAsync(string source, CancellationToken cancellationToken)
            => Task.FromResult(source);

        public Task HealCoreBundlesAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class RecordingPluginManager : IPluginManager
    {
        private readonly List<string> _order;

        public RecordingPluginManager(List<string> order) => _order = order;

        public Task<IReadOnlyList<PluginInfo>> ListPluginsAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<PluginInfo>>(Array.Empty<PluginInfo>());

        public Task SetEnabledAsync(string name, bool enabled, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task UninstallAsync(string name, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<string> InstallAsync(string source, CancellationToken cancellationToken)
            => Task.FromResult(source);

        public Task HealCoreBundlesAsync(CancellationToken cancellationToken)
        {
            _order.Add("CoreBundles");
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingPluginManager : IPluginManager
    {
        public Task<IReadOnlyList<PluginInfo>> ListPluginsAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<PluginInfo>>(Array.Empty<PluginInfo>());

        public Task SetEnabledAsync(string name, bool enabled, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task UninstallAsync(string name, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<string> InstallAsync(string source, CancellationToken cancellationToken)
            => Task.FromResult(source);

        public Task HealCoreBundlesAsync(CancellationToken cancellationToken)
            => throw new InvalidOperationException("核心 bundles 自愈失败（Fake）");
    }

    private sealed class NoopNormalizer : IProfileManifestNormalizer
    {
        public void NormalizeToFlatModel(string profileDir)
        {
        }
    }

    private sealed class RecordingNormalizer : IProfileManifestNormalizer
    {
        private readonly List<string> _order;

        public RecordingNormalizer(List<string> order) => _order = order;

        public int CallCount { get; private set; }

        public void NormalizeToFlatModel(string profileDir)
        {
            CallCount++;
            _order.Add("ProfileManifestNormalize");
        }
    }

    private sealed class NoopConfig : IRuntimeBootstrapConfig
    {
        public string? DshEntryPath { get; set; }

        public string? ActiveDshRuntime { get; set; }

        public Task PersistAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingConfig : IRuntimeBootstrapConfig
    {
        private readonly List<string> _order;

        public RecordingConfig(List<string> order) => _order = order;

        public int PersistCount { get; private set; }

        public string? DshEntryPath { get; set; }

        public string? ActiveDshRuntime { get; set; }

        public Task PersistAsync(CancellationToken cancellationToken)
        {
            PersistCount++;
            _order.Add("ActiveRuntimeSelection");
            return Task.CompletedTask;
        }
    }

    private sealed class FixedProbe : IIncompatiblePluginCrashProbe
    {
        private readonly string? _result;

        public FixedProbe(string? result) => _result = result;

        public string? TryParseOffender(string startFailureMessage) => _result;
    }

    private sealed class NoopOrchestrator : IPluginOrchestrator
    {
        public event EventHandler<PluginOperation>? OperationChanged;

        public Task<string> InstallAsync(string source, PluginOperationKind kind, CancellationToken cancellationToken)
            => Task.FromResult(source);

        public Task UninstallAsync(string name, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task SetEnabledAsync(string name, bool enabled, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task DisableAllThirdPartyAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class RecordingOrchestrator : IPluginOrchestrator
    {
        public List<(string Source, PluginOperationKind Kind)> InstallCalls { get; } = [];

        public event EventHandler<PluginOperation>? OperationChanged;

        public Task<string> InstallAsync(string source, PluginOperationKind kind, CancellationToken cancellationToken)
        {
            InstallCalls.Add((source, kind));
            return Task.FromResult(source);
        }

        public Task UninstallAsync(string name, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task SetEnabledAsync(string name, bool enabled, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task DisableAllThirdPartyAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
