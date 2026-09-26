using DshDesktop.Application.Bootstrap;
using DshDesktop.Application.Plugins;
using DshDesktop.Application.Runtime;
using DshDesktop.Application.Updates;
using DshDesktop.Domain.Plugins;
using DshDesktop.Domain.Updates;
using Serilog;
using Serilog.Core;

namespace DshDesktop.Tests;

/// <summary>
/// RuntimeBootstrapper 启动编排测试（组合根拆分批 2a）：RunAsync 的编排顺序与错误语义
/// （配置加载→主题→pnpm 门控自举→种子→栈装配→引导自愈）、RebuildStackAsync 的首启安装链
/// （工具链补全→重建绑定件→装最新版→激活落盘）。全部经端口/工厂 Fake 断言，
/// 不触及真实文件系统之外的外部世界（ActiveRuntime 枚举沿用真实临时目录）。
/// 换算自文本守卫 CompositionRootGuardTests.InitializeRuntimeAsync_SkipsPnpmProvisionWithoutNode。
/// </summary>
public sealed class RuntimeBootstrapperRunTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "dsh-bootstrap-run-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Test]
    public async Task RunAsync_RunsOrchestrationInDeclaredOrder()
    {
        List<string> order = [];
        FakeConfig config = new(order) { DshHome = CreateDshHomeWithSelfBuiltRuntimes(["0.1.5", "0.1.10"]) };
        // 借用入口不存在 ⇒ ActiveRuntime 自愈触发落盘（Persist 写入顺序流）。
        config.DshEntryPath = Path.Combine(_tempDir, "missing.js");
        var pnpm = new FakePnpmProvisioner(order) { Result = null };
        var bootstrapper = CreateBootstrapper(config, order, pnpm: pnpm);

        RuntimeStack stack = await bootstrapper.RunAsync(CancellationToken.None);

        await Assert.That(stack).IsNotNull();
        await Assert.That(order.Count).IsEqualTo(8);
        await Assert.That(order[0]).IsEqualTo("LoadConfig");
        await Assert.That(order[1]).IsEqualTo("Theme:Dark");
        await Assert.That(order[2]).IsEqualTo("PnpmEnsure");
        await Assert.That(order[3]).IsEqualTo("Seed");
        await Assert.That(order[4]).IsEqualTo("StackCreate");
        await Assert.That(order[5]).IsEqualTo("CoreBundles");
        await Assert.That(order[6]).IsEqualTo("Persist");
        await Assert.That(order[7]).IsEqualTo("Normalize");
        // 借用失效 + 有自建候选 ⇒ 激活数值最高版本。
        await Assert.That(config.ActiveDshRuntime).IsEqualTo("0.1.10");
    }

    [Test]
    public async Task RunAsync_WithoutNode_SkipsPnpmProvision()
    {
        // 守卫换算：无可用 node（干净机器首启）⇒ 跳过 pnpm 自举，但种子与栈装配照常。
        List<string> order = [];
        FakeConfig config = new(order) { DshHome = CreateDshHomeWithSelfBuiltRuntimes([]) };
        config.DshEntryPath = CreateFile("entry.js"); // 借用可用 ⇒ ActiveRuntime 不动。
        var node = new FakeNodeProvisioner(order) { Available = false };
        var pnpm = new FakePnpmProvisioner(order);
        var bootstrapper = CreateBootstrapper(config, order, node: node, pnpm: pnpm);

        await bootstrapper.RunAsync(CancellationToken.None);

        await Assert.That(pnpm.CallCount).IsEqualTo(0);
        await Assert.That(order.Contains("Seed", StringComparer.Ordinal)).IsTrue();
        await Assert.That(order.Contains("StackCreate", StringComparer.Ordinal)).IsTrue();
    }

    [Test]
    public async Task RunAsync_PnpmPathChanged_UpdatesConfigAndPersists()
    {
        List<string> order = [];
        FakeConfig config = new(order)
        {
            DshHome = CreateDshHomeWithSelfBuiltRuntimes([]),
            DshEntryPath = CreateFile("entry.js"),
            PnpmCjsPath = "pnpm-old.cjs",
        };
        var pnpm = new FakePnpmProvisioner(order) { Result = "pnpm-new.cjs" };
        var bootstrapper = CreateBootstrapper(config, order, pnpm: pnpm);

        await bootstrapper.RunAsync(CancellationToken.None);

        await Assert.That(config.PnpmCjsPath).IsEqualTo("pnpm-new.cjs");
        await Assert.That(config.PersistCount).IsEqualTo(1);
    }

    [Test]
    public async Task RunAsync_PnpmPersistFails_StillCompletesBootstrap()
    {
        // pnpm 落盘失败只 Warning 不中断：种子与栈装配照常，RunAsync 正常返回。
        List<string> order = [];
        FakeConfig config = new(order)
        {
            DshHome = CreateDshHomeWithSelfBuiltRuntimes([]),
            DshEntryPath = CreateFile("entry.js"),
            PnpmCjsPath = "pnpm-old.cjs",
            PersistException = new InvalidOperationException("磁盘写失败（Fake）"),
        };
        var pnpm = new FakePnpmProvisioner(order) { Result = "pnpm-new.cjs" };
        var bootstrapper = CreateBootstrapper(config, order, pnpm: pnpm);

        RuntimeStack stack = await bootstrapper.RunAsync(CancellationToken.None);

        await Assert.That(stack).IsNotNull();
        await Assert.That(order.Contains("Seed", StringComparer.Ordinal)).IsTrue();
        await Assert.That(order.Contains("StackCreate", StringComparer.Ordinal)).IsTrue();
    }

    [Test]
    public async Task RunAsync_SeedThrows_PropagatesAndSkipsAssembly()
    {
        // 种子失败 = 整个初始化失败（原样上抛给 App.BootstrapRuntimeAsync），栈装配不得执行。
        List<string> order = [];
        FakeConfig config = new(order)
        {
            DshHome = CreateDshHomeWithSelfBuiltRuntimes([]),
            DshEntryPath = CreateFile("entry.js"),
        };
        var seeder = new FakeSeeder(order) { SeedException = new InvalidOperationException("种子复制失败（Fake）") };
        var stackFactory = new FakeStackFactory(order);
        var bootstrapper = CreateBootstrapper(config, order, seeder: seeder, stackFactory: stackFactory);

        await Assert.That(async () => await bootstrapper.RunAsync(CancellationToken.None))
            .Throws<InvalidOperationException>();
        await Assert.That(stackFactory.CreateCount).IsEqualTo(0);
    }

    [Test]
    public async Task RunAsync_CoreBundlesHealFails_StackStillReturned()
    {
        // 批 1 语义经新入口保持：核心 bundles 自愈失败只告警，引导完成、栈正常回流。
        List<string> order = [];
        FakeConfig config = new(order)
        {
            DshHome = CreateDshHomeWithSelfBuiltRuntimes([]),
            DshEntryPath = CreateFile("entry.js"),
        };
        var stackFactory = new FakeStackFactory(order) { CoreBundlesException = new InvalidOperationException("自愈失败（Fake）") };
        var bootstrapper = CreateBootstrapper(config, order, stackFactory: stackFactory);

        RuntimeStack stack = await bootstrapper.RunAsync(CancellationToken.None);

        await Assert.That(stack).IsNotNull();
        await Assert.That(order.Contains("Normalize", StringComparer.Ordinal)).IsTrue();
    }

    [Test]
    public async Task RebuildStackAsync_NodeAlreadyAvailable_InstallsLatestAndActivates()
    {
        List<string> order = [];
        FakeConfig config = new(order)
        {
            DshHome = CreateDshHomeWithSelfBuiltRuntimes([]),
            DshEntryPath = CreateFile("entry.js"),
        };
        var node = new FakeNodeProvisioner(order) { Available = true };
        var stackFactory = new FakeStackFactory(order);
        var bootstrapper = CreateBootstrapper(config, order, node: node, stackFactory: stackFactory);
        await bootstrapper.RunAsync(CancellationToken.None);
        order.Clear();

        await bootstrapper.RebuildStackAsync(progress: null, CancellationToken.None);

        // node 可用 ⇒ 不补工具链、不重建绑定件；直接查版本→安装→激活→落盘。
        await Assert.That(node.EnsureCallCount).IsEqualTo(0);
        await Assert.That(stackFactory.RebuildCount).IsEqualTo(0);
        await Assert.That(stackFactory.Repository.LatestVersionChannel).IsEqualTo("latest");
        await Assert.That(stackFactory.Repository.InstalledVersions).IsEquivalentTo(new[] { "9.9.9" });
        await Assert.That(config.ActiveDshRuntime).IsEqualTo("9.9.9");
        await Assert.That(order.Contains("Persist", StringComparer.Ordinal)).IsTrue();
    }

    [Test]
    public async Task RebuildStackAsync_WithoutNode_TopsUpToolchainThenRebuildsBoundStack()
    {
        List<string> order = [];
        FakeConfig config = new(order)
        {
            DshHome = CreateDshHomeWithSelfBuiltRuntimes([]),
            DshEntryPath = CreateFile("entry.js"),
            NodePathValue = string.Empty,
        };
        var node = new FakeNodeProvisioner(order) { Available = false };
        var pnpm = new FakePnpmProvisioner(order) { Result = "pnpm-bootstrapped.cjs" };
        var stackFactory = new FakeStackFactory(order);
        var bootstrapper = CreateBootstrapper(config, order, node: node, pnpm: pnpm, stackFactory: stackFactory);
        // RunAsync 阶段无 node ⇒ pnpm 自举被跳过；Rebuild 时才补全工具链。
        await bootstrapper.RunAsync(CancellationToken.None);
        order.Clear();
        node.Available = false;

        await bootstrapper.RebuildStackAsync(progress: null, CancellationToken.None);

        await Assert.That(node.EnsureCallCount).IsEqualTo(1);
        await Assert.That(config.NodePath).IsEqualTo("node-provisioned.exe");
        await Assert.That(config.NpmCjsPath).IsEqualTo("npm-provisioned.cjs");
        await Assert.That(config.PnpmCjsPath).IsEqualTo("pnpm-bootstrapped.cjs");
        await Assert.That(stackFactory.RebuildCount).IsEqualTo(1);
        await Assert.That(config.ActiveDshRuntime).IsEqualTo("9.9.9");
        // 顺序：node 下载 → pnpm 自举 → 落盘 → 重建绑定件 → 安装后落盘。
        await Assert.That(order.Count).IsEqualTo(5);
        await Assert.That(order[0]).IsEqualTo("NodeEnsure");
        await Assert.That(order[1]).IsEqualTo("PnpmEnsure");
        await Assert.That(order[2]).IsEqualTo("Persist");
        await Assert.That(order[3]).IsEqualTo("RebuildToolchainBound");
        await Assert.That(order[4]).IsEqualTo("Persist");
    }

    [Test]
    public async Task TryHealIncompatiblePluginCrash_AfterRunAsync_UsesStackOrchestrator()
    {
        // 批 1 反应式自愈经重塑后的入口保持：RunAsync 装配的编排器承接事务化升级。
        List<string> order = [];
        FakeConfig config = new(order)
        {
            DshHome = CreateDshHomeWithSelfBuiltRuntimes([]),
            DshEntryPath = CreateFile("entry.js"),
        };
        var stackFactory = new FakeStackFactory(order);
        var bootstrapper = CreateBootstrapper(config, order, stackFactory: stackFactory, crashOffender: "dshmarket");
        await bootstrapper.RunAsync(CancellationToken.None);

        bool healed = await bootstrapper.TryHealIncompatiblePluginCrashAsync(
            "does not provide an export ...", CancellationToken.None);

        await Assert.That(healed).IsTrue();
        await Assert.That(stackFactory.PluginOrchestrator.InstallCalls)
            .IsEquivalentTo(new[] { ("dshmarket@latest", PluginOperationKind.Update) });
    }

    [Test]
    public async Task HealActiveRuntime_BorrowedEntryUsable_NoChange()
    {
        List<string> order = [];
        FakeConfig config = new(order)
        {
            DshHome = CreateDshHomeWithSelfBuiltRuntimes(["0.1.10"]),
            DshEntryPath = CreateFile("entry.js"),
            ActiveDshRuntime = null,
        };
        var bootstrapper = CreateBootstrapper(config, order);

        await bootstrapper.RunAsync(CancellationToken.None);

        // 借用可用 ⇒ Select 返回 null ⇒ 不写激活态、不因自愈落盘。
        await Assert.That(config.ActiveDshRuntime).IsNull();
        await Assert.That(config.PersistCount).IsEqualTo(0);
    }

    [Test]
    public async Task HealActiveRuntime_NoCandidate_NoChange()
    {
        List<string> order = [];
        // versions 为空 ⇒ runtime 根目录不存在 ⇒ 无候选。
        FakeConfig config = new(order)
        {
            DshHome = CreateDshHomeWithSelfBuiltRuntimes([]),
            DshEntryPath = Path.Combine(_tempDir, "missing.js"),
            ActiveDshRuntime = null,
        };
        var bootstrapper = CreateBootstrapper(config, order);

        await bootstrapper.RunAsync(CancellationToken.None);

        await Assert.That(config.ActiveDshRuntime).IsNull();
        await Assert.That(config.PersistCount).IsEqualTo(0);
    }

    [Test]
    public async Task NormalizeEntry_RunsEveryBoot()
    {
        // 归一化无谓词、每次启动恒执行：两次 RunAsync ⇒ 两次归一化。
        List<string> order = [];
        FakeConfig config = new(order)
        {
            DshHome = CreateDshHomeWithSelfBuiltRuntimes([]),
            DshEntryPath = CreateFile("entry.js"),
        };
        var bootstrapper = CreateBootstrapper(config, order);

        await bootstrapper.RunAsync(CancellationToken.None);
        await bootstrapper.RunAsync(CancellationToken.None);

        await Assert.That(order.FindAll(o => string.Equals(o, "Normalize", StringComparison.Ordinal)).Count)
            .IsEqualTo(2);
    }

    [Test]
    public async Task TryHealIncompatiblePluginCrash_ProbeMiss_NoInstall()
    {
        List<string> order = [];
        FakeConfig config = new(order)
        {
            DshHome = CreateDshHomeWithSelfBuiltRuntimes([]),
            DshEntryPath = CreateFile("entry.js"),
        };
        var stackFactory = new FakeStackFactory(order);
        var bootstrapper = CreateBootstrapper(config, order, stackFactory: stackFactory, crashOffender: null);
        await bootstrapper.RunAsync(CancellationToken.None);

        bool healed = await bootstrapper.TryHealIncompatiblePluginCrashAsync("无关错误", CancellationToken.None);

        await Assert.That(healed).IsFalse();
        await Assert.That(stackFactory.PluginOrchestrator.InstallCalls.Count).IsEqualTo(0);
    }

    [Test]
    public async Task TryHealIncompatiblePluginCrash_AlreadyAttempted_NoSecondInstall()
    {
        // 每会话每插件只试一次：首次成功，第二次不再调用 InstallAsync。
        List<string> order = [];
        FakeConfig config = new(order)
        {
            DshHome = CreateDshHomeWithSelfBuiltRuntimes([]),
            DshEntryPath = CreateFile("entry.js"),
        };
        var stackFactory = new FakeStackFactory(order);
        var bootstrapper = CreateBootstrapper(config, order, stackFactory: stackFactory, crashOffender: "dshmarket");
        await bootstrapper.RunAsync(CancellationToken.None);

        bool first = await bootstrapper.TryHealIncompatiblePluginCrashAsync("does not provide an export", CancellationToken.None);
        bool second = await bootstrapper.TryHealIncompatiblePluginCrashAsync("does not provide an export", CancellationToken.None);

        await Assert.That(first).IsTrue();
        await Assert.That(second).IsFalse();
        await Assert.That(stackFactory.PluginOrchestrator.InstallCalls.Count).IsEqualTo(1);
    }

    // ===== 测试夹具 =====

    private RuntimeBootstrapper CreateBootstrapper(
        FakeConfig config,
        List<string> order,
        FakeNodeProvisioner? node = null,
        FakePnpmProvisioner? pnpm = null,
        FakeSeeder? seeder = null,
        FakeStackFactory? stackFactory = null,
        string? crashOffender = null) =>
        new(
            config,
            new FakeThemeApplier(order),
            node ?? new FakeNodeProvisioner(order),
            pnpm ?? new FakePnpmProvisioner(order),
            seeder ?? new FakeSeeder(order),
            stackFactory ?? new FakeStackFactory(order),
            new FixedProbe(crashOffender),
            new RecordingNormalizer(order),
            Path.Combine(_tempDir, "data"),
            Logger.None);

    private string CreateFile(string name)
    {
        Directory.CreateDirectory(_tempDir);
        string path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    /// <summary>建 DSH_HOME 布局：&lt;temp&gt;\dsh-home &lt;temp&gt;\runtime\dsh\&lt;version&gt;\node_modules\@deepseek-ai\dsh。</summary>
    private string CreateDshHomeWithSelfBuiltRuntimes(string[] versions)
    {
        string dshHome = Path.Combine(_tempDir, "dsh-home-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dshHome);
        string runtimeRoot = Path.Combine(Directory.GetParent(dshHome)!.FullName, "runtime", "dsh");
        foreach (string version in versions)
        {
            Directory.CreateDirectory(Path.Combine(runtimeRoot, version, "node_modules", "@deepseek-ai", "dsh"));
        }

        return dshHome;
    }

    private sealed class FakeConfig : IRuntimeConfig
    {
        private readonly List<string> _order;

        public FakeConfig(List<string> order)
        {
            _order = order;
            DshHome = string.Empty;
            DshEntryPath = null;
        }

        public int PersistCount { get; private set; }

        public Exception? PersistException { get; set; }

        public string NodePathValue { get; set; } = "node";

        public string Theme => "Dark";

        public string? DshEntryPath { get; set; }

        public string? ActiveDshRuntime { get; set; }

        public string NodePath
        {
            get => NodePathValue;
            set => NodePathValue = value;
        }

        public string? NpmCjsPath { get; set; }

        public string? PnpmCjsPath { get; set; }

        public string DshHome { get; set; }

        public string? SeedProfileFrom => null;

        public string DshChannel => "latest";

        public Task LoadAsync(CancellationToken cancellationToken)
        {
            _order.Add("LoadConfig");
            return Task.CompletedTask;
        }

        public Task PersistAsync(CancellationToken cancellationToken)
        {
            if (PersistException is not null)
            {
                Exception exception = PersistException;
                PersistException = null; // 只抛第一次：模拟一次性写失败。
                throw exception;
            }

            PersistCount++;
            _order.Add("Persist");
            return Task.CompletedTask;
        }
    }

    private sealed class FakeThemeApplier(List<string> order) : IThemeApplier
    {
        public void ApplyTheme(string theme) => order.Add("Theme:" + theme);
    }

    private sealed class FakeNodeProvisioner(List<string> order) : INodeProvisioner
    {
        public bool Available { get; set; } = true;

        public int EnsureCallCount { get; private set; }

        public bool IsNodeAvailable(string? nodePath) => Available;

        public Task<NodeProvisionResult> EnsureAvailableAsync(
            string dataRoot, IProgress<int>? progress, CancellationToken cancellationToken)
        {
            EnsureCallCount++;
            order.Add("NodeEnsure");
            return Task.FromResult(new NodeProvisionResult("node-provisioned.exe", "npm-provisioned.cjs"));
        }
    }

    private sealed class FakePnpmProvisioner(List<string> order) : IPnpmProvisioner
    {
        public string? Result { get; set; }

        public int CallCount { get; private set; }

        public Task<string?> EnsureAvailableAsync(
            string dataRoot,
            string nodePath,
            string? npmCjsPath,
            string? currentPnpmCjsPath,
            CancellationToken cancellationToken)
        {
            CallCount++;
            order.Add("PnpmEnsure");
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeSeeder(List<string> order) : IProfileSeeder
    {
        public Exception? SeedException { get; set; }

        public Task SeedIfNeededAsync(
            string dshHome,
            string? seedProfileFrom,
            string nodePath,
            string? pnpmCjsPath,
            CancellationToken cancellationToken)
        {
            if (SeedException is not null)
            {
                throw SeedException;
            }

            order.Add("Seed");
            return Task.CompletedTask;
        }
    }

    private sealed class FakeProbe : IRuntimeProbe
    {
        public bool IsProcessAlive(int processId) => false;

        public Task<bool> IsHttpAliveAsync(string host, int port, CancellationToken cancellationToken)
            => Task.FromResult(false);

        public void KillProcessTree(int processId)
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeOrchestrator : IRuntimeOrchestrator
    {
        public event EventHandler<RuntimeExitedEventArgs>? Exited;

        public event EventHandler<ProcessOutputLineEventArgs>? OutputReceived;

        public Task<RuntimeStartResult> StartAsync(RuntimeLaunchOptions options, CancellationToken cancellationToken)
            => Task.FromResult(new RuntimeStartResult(42, 5000, "http://127.0.0.1:5000/?token=x"));

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakePluginManager(List<string> order, Exception? healException) : IPluginManager
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
        {
            if (healException is not null)
            {
                throw healException;
            }

            order.Add("CoreBundles");
            return Task.CompletedTask;
        }
    }

    private sealed class FakePluginOrchestrator : IPluginOrchestrator
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

    private sealed class FakeRuntimeRepository : IRuntimeRepository
    {
        public string? LatestVersionChannel { get; private set; }

        public List<string> InstalledVersions { get; } = [];

        public Task<IReadOnlyList<DshRuntimeInfo>> ListRuntimesAsync(
            string? activeRuntime, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<DshRuntimeInfo>>(Array.Empty<DshRuntimeInfo>());

        public Task<string> GetLatestVersionAsync(string channel, CancellationToken cancellationToken)
        {
            LatestVersionChannel = channel;
            return Task.FromResult("9.9.9");
        }

        public Task InstallAsync(string version, CancellationToken cancellationToken)
        {
            InstalledVersions.Add(version);
            return Task.CompletedTask;
        }

        public Task<string?> GetLatestPluginVersionAsync(string name, CancellationToken cancellationToken)
            => Task.FromResult<string?>(null);
    }

    private sealed class FakeDesktopUpdater : IDesktopUpdater
    {
        public bool IsInstalled => false;

        public Task<DesktopUpdateInfo?> CheckForUpdatesAsync(CancellationToken cancellationToken)
            => Task.FromResult<DesktopUpdateInfo?>(null);

        public Task DownloadAsync(IProgress<int>? progress, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public void ApplyAndRestart()
        {
        }
    }

    private sealed class FakeStackFactory(List<string> order) : IRuntimeStackFactory
    {
        private readonly FakePluginOrchestrator _pluginOrchestrator = new();

        public Exception? CoreBundlesException { get; set; }

        public int CreateCount { get; private set; }

        public int RebuildCount { get; private set; }

        public FakePluginOrchestrator PluginOrchestrator => _pluginOrchestrator;

        public FakeRuntimeRepository Repository { get; } = new();

        public RuntimeStack Create(IRuntimeConfig config)
        {
            CreateCount++;
            order.Add("StackCreate");
            FakeOrchestrator processHost = new();
            FakeProbe probe = new();
            return new RuntimeStack(
                processHost,
                new RuntimeSupervisor(processHost, Logger.None),
                probe,
                new RuntimeReattacher(probe, Logger.None),
                new FakePluginManager(order, CoreBundlesException),
                _pluginOrchestrator,
                Repository,
                new FakeDesktopUpdater());
        }

        public ToolchainBoundStack RebuildToolchainBound(IRuntimeConfig config, RuntimeSupervisor supervisor)
        {
            RebuildCount++;
            order.Add("RebuildToolchainBound");
            return new ToolchainBoundStack(
                new FakePluginManager(order, CoreBundlesException), _pluginOrchestrator, Repository);
        }
    }

    private sealed class FixedProbe(string? result) : IIncompatiblePluginCrashProbe
    {
        public string? TryParseOffender(string startFailureMessage) => result;
    }

    private sealed class RecordingNormalizer(List<string> order) : IProfileManifestNormalizer
    {
        public void NormalizeToFlatModel(string profileDir) => order.Add("Normalize");
    }
}
