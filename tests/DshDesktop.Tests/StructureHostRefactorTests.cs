using System.IO;
using System.Text;

namespace DshDesktop.Tests;

/// <summary>
/// 组合根拆分（批 1 宿主侧）结构守卫：每项守卫读取其**真实 owner** 文件并断言具体不变量，
/// 不拼接全部文件做任意 Contains 伪验证；既有 CompositionRootGuardTests 的不变量断言保持不变。
/// 目的：锁定拆分后日志/诊断管道、托盘/通知生命周期、设置投影/开关、诊断命令各归其主，
/// 且组合根不再重复持有已迁出的状态。
/// </summary>
public sealed class StructureHostRefactorTests
{
    private static async Task<string> ReadOwnerAsync(string relativePath)
    {
        string? root = XamlScan.FindRepositoryRoot();
        await Assert.That(root).IsNotNull();
        string path = Path.Combine(root!, relativePath);
        await Assert.That(File.Exists(path)).IsTrue();
        return (await File.ReadAllTextAsync(path, Encoding.UTF8)).Replace("\r\n", "\n");
    }

    // ===== 提取 1：DesktopDiagnostics（日志 / 诊断管道） =====

    [Test]
    public async Task DesktopDiagnostics_OwnsMigrationAndLoggerInit()
    {
        string source = await ReadOwnerAsync("src/DshDesktop.App/Logging/DesktopDiagnostics.cs");

        // 迁移必须先于日志目录确立（ADR-0009：迁移把旧根 logs 搬走，日志须落新根）。
        int migration = source.IndexOf("MigrateLegacyDataRootIfNeeded", StringComparison.Ordinal);
        int logger = source.IndexOf("Log.Logger = new LoggerConfiguration", StringComparison.Ordinal);
        await Assert.That(migration >= 0).IsTrue();
        await Assert.That(logger >= 0).IsTrue();
        await Assert.That(migration < logger).IsTrue();

        // 日志 sink 复用诊断 hub（Live 控制台回流）。
        bool usesDiagnosticSink = source.Contains("WriteTo.Sink(new DiagnosticsSink", StringComparison.Ordinal);
        await Assert.That(usesDiagnosticSink).IsTrue();
    }

    [Test]
    public async Task DesktopDiagnostics_PreservesRedactionAndStoreRouting()
    {
        string source = await ReadOwnerAsync("src/DshDesktop.App/Logging/DesktopDiagnostics.cs");

        // 进程输出落盘须打码（CONTEXT.md: Session URL 全仓单源）。
        bool redacts = source.Contains("SessionUrlRedactor.Redact", StringComparison.Ordinal);
        await Assert.That(redacts).IsTrue();

        // 诊断事件回流 Diagnostics Store（容器就绪后订阅）。
        await Assert.That(source.Contains("_hub.Events.Subscribe(onDiagnosticEvent)", StringComparison.Ordinal)).IsTrue();
        string root = await ReadOwnerAsync("src/DshDesktop.App/Composition/DshCompositionRoot.cs");
        await Assert.That(root.Contains("_diagnostics.SubscribeToStore(diagnosticEvent =>", StringComparison.Ordinal)).IsTrue();
        await Assert.That(root.Contains(".DispatchAsync(new DiagnosticsIntent.DiagnosticEventReceived(diagnosticEvent))", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task CompositionRoot_NoLongerHoldsDiagnosticsState()
    {
        string source = await ReadOwnerAsync("src/DshDesktop.App/Composition/DshCompositionRoot.cs");

        // 根不再重复持有 hub / 子 logger 状态（状态收口到 DesktopDiagnostics）。
        await Assert.That(source.Contains("_diagnosticsHub", StringComparison.Ordinal)).IsFalse();
        await Assert.That(source.Contains("_dshStdoutLogger", StringComparison.Ordinal)).IsFalse();
        await Assert.That(source.Contains("_dshStderrLogger", StringComparison.Ordinal)).IsFalse();

        // 但根须通过模块转发进程输出与 hub。
        await Assert.That(source.Contains("stack.ProcessHost.OutputReceived += _diagnostics.OnProcessOutputReceived;", StringComparison.Ordinal)).IsTrue();
        int logging = source.IndexOf("_diagnostics.InitializeLogging()", StringComparison.Ordinal);
        int container = source.IndexOf("_container = new GeneratedMviContainer(uiDispatcher)", StringComparison.Ordinal);
        int subscribe = source.IndexOf("_diagnostics.SubscribeToStore(", StringComparison.Ordinal);
        int startup = source.IndexOf("_diagnostics.LogStartup(migrationOutcome)", StringComparison.Ordinal);
        await Assert.That(logging >= 0 && container > logging && subscribe > container && startup > subscribe).IsTrue();
    }

    // ===== 提取 2：DesktopTrayLifetime（托盘 / 通知生命周期） =====

    [Test]
    public async Task DesktopTrayLifetime_OwnsTrayIconAndNotifications()
    {
        string source = await ReadOwnerAsync("src/DshDesktop.App/Composition/DesktopTrayLifetime.cs");

        bool hasConfigure = source.Contains("TrayIcon.SetIcons", StringComparison.Ordinal);
        bool hasShow = source.Contains("ShowMainWindow", StringComparison.Ordinal);
        bool hasBalloon = source.Contains("BalloonNotificationService", StringComparison.Ordinal);
        bool hasSubscriber = source.Contains("DiagnosticsNotificationSubscriber", StringComparison.Ordinal);
        await Assert.That(hasConfigure).IsTrue();
        await Assert.That(hasShow).IsTrue();
        await Assert.That(hasBalloon).IsTrue();
        await Assert.That(hasSubscriber).IsTrue();

        // NotificationsEnabled 延迟配置读取（订阅后才读，_config 未加载时仍可构造）。
        await Assert.That(source.Contains("Func<bool> notificationsEnabled", StringComparison.Ordinal)).IsTrue();
        await Assert.That(source.Contains("            notificationsEnabled);", StringComparison.Ordinal)).IsTrue();
        string root = await ReadOwnerAsync("src/DshDesktop.App/Composition/DshCompositionRoot.cs");
        await Assert.That(root.Contains("window, shellViewModel, _diagnostics.Hub, () => _config?.NotificationsEnabled ?? true", StringComparison.Ordinal)).IsTrue();
        await Assert.That(source.Contains("exitItem.Click += (_, _) => window.RequestExit();", StringComparison.Ordinal)).IsTrue();
        int disposeSubscriber = source.IndexOf("_notificationSubscriber?.Dispose();", StringComparison.Ordinal);
        int disposeService = source.IndexOf("_notificationService?.Dispose();", StringComparison.Ordinal);
        await Assert.That(disposeSubscriber >= 0 && disposeService > disposeSubscriber).IsTrue();
    }

    [Test]
    public async Task CompositionRoot_NoLongerHoldsNotificationFields()
    {
        string source = await ReadOwnerAsync("src/DshDesktop.App/Composition/DshCompositionRoot.cs");

        await Assert.That(source.Contains("_notificationService", StringComparison.Ordinal)).IsFalse();
        await Assert.That(source.Contains("_notificationSubscriber", StringComparison.Ordinal)).IsFalse();
    }

    // ===== 提取 3：DesktopSettings（设置投影 / 开关处理器） =====

    [Test]
    public async Task DesktopSettings_OwnsSettingsHandlersAndFlagHelper()
    {
        string source = await ReadOwnerAsync("src/DshDesktop.App/Composition/DesktopSettings.cs");

        bool hasFlag = source.Contains("SetConfigFlagAsync", StringComparison.Ordinal);
        bool hasTheme = source.Contains("ApplyTheme", StringComparison.Ordinal);
        bool hasSafeMode = source.Contains("SetSafeModeCoreAsync", StringComparison.Ordinal);
        bool hasGetInfo = source.Contains("HandleGetSettingsInfo", StringComparison.Ordinal);
        bool hasSetTheme = source.Contains("HandleSetThemeAsync", StringComparison.Ordinal);
        bool hasLaunch = source.Contains("HandleSetLaunchOnStartupAsync", StringComparison.Ordinal);
        bool hasOpenPath = source.Contains("HandleOpenPath", StringComparison.Ordinal);
        await Assert.That(hasFlag).IsTrue();
        await Assert.That(hasTheme).IsTrue();
        await Assert.That(hasSafeMode).IsTrue();
        await Assert.That(hasGetInfo).IsTrue();
        await Assert.That(hasSetTheme).IsTrue();
        await Assert.That(hasLaunch).IsTrue();
        await Assert.That(hasOpenPath).IsTrue();

        // 统一经 ConfigPersistence 同一把锁落盘，禁止绕过直调 DshDesktopConfigStore.SaveAsync。
        bool persists = source.Contains("SaveAsync", StringComparison.Ordinal);
        bool bypasses = source.Contains("DshDesktopConfigStore.SaveAsync", StringComparison.Ordinal);
        await Assert.That(persists).IsTrue();
        await Assert.That(bypasses).IsFalse();
    }

    // ===== 提取 4：DesktopDiagnosticsCommands（诊断命令） =====

    [Test]
    public async Task DesktopDiagnosticsCommands_OwnsDiagnosisCommands()
    {
        string source = await ReadOwnerAsync("src/DshDesktop.App/Composition/DesktopDiagnosticsCommands.cs");

        bool hasRunner = source.Contains("DiagnosisRunner", StringComparison.Ordinal);
        await Assert.That(hasRunner).IsTrue();

        int checks = 0;
        int idx = source.IndexOf("new DiagnosisCheck(", StringComparison.Ordinal);
        while (idx >= 0)
        {
            checks++;
            idx = source.IndexOf("new DiagnosisCheck(", idx + 1, StringComparison.Ordinal);
        }
        await Assert.That(checks).IsEqualTo(4);

        // 导出异常被吞并、仍发布诊断（成功/失败都回流 Live 控制台）。
        bool exports = source.Contains("DiagnosticsBundleExporter.Export", StringComparison.Ordinal);
        bool swallows = source.Contains("catch", StringComparison.Ordinal);
        await Assert.That(exports).IsTrue();
        await Assert.That(swallows).IsTrue();
    }
}
