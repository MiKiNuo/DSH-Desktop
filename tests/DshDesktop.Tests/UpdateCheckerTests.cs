using DshDesktop.Application.Plugins;
using DshDesktop.Application.Updates;
using DshDesktop.Domain.Plugins;
using DshDesktop.Domain.Updates;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace DshDesktop.Tests;

public sealed class UpdateCheckerTests
{
    [Test]
    public async Task Check_ReportsIndependentVersionsAndEnabledResolvablePluginUpdates()
    {
        var runtimes = new RuntimeRepository
        {
            Runtimes = [new("1.0.0", false, true), new("1.5.0", true, false)],
            PluginVersions = new()
            {
                ["dshmarket"] = "2.0.0",
                ["dsh-example"] = "1.1.0",
                ["dsh-current"] = "1.0.0",
                ["dsh-unknown"] = null,
            },
        };
        var plugins = new PluginManager(
        [
            new("dshmarket", "1.0.0", true, true, ""),
            new("dsh-example", "1.0.0", false, true, ""),
            new("dsh-current", "1.0.0", false, true, ""),
            new("dsh-unknown", "1.0.0", false, true, ""),
            new("dsh-disabled", "1.0.0", false, false, ""),
            new("@deepseek-ai/in-box", "in-box", true, true, "", false),
        ]);
        var desktop = new DesktopUpdater(new("0.2.0"));
        var checker = new UpdateChecker(runtimes, plugins, desktop, Logger.None);

        UpdateCheckResult result = await checker.CheckAsync("alpha", () => "1.5.0", () => false);

        await Assert.That(result.LatestDshVersion).IsEqualTo("1.6.0");
        await Assert.That(result.CurrentDshVersion).IsEqualTo("1.5.0");
        await Assert.That(result.LatestDesktopVersion).IsEqualTo("0.2.0");
        await Assert.That(result.Runtimes.Count).IsEqualTo(2);
        await Assert.That(result.PluginUpdates.Count).IsEqualTo(2);
        await Assert.That(result.PluginUpdates[0]).IsEqualTo(new PluginUpdateInfo("dshmarket", "1.0.0", "2.0.0"));
        await Assert.That(result.PluginUpdates[1]).IsEqualTo(new PluginUpdateInfo("dsh-example", "1.0.0", "1.1.0"));
        await Assert.That(runtimes.Channel).IsEqualTo("alpha");
        await Assert.That(runtimes.ActiveRuntime).IsEqualTo("1.5.0");
    }

    [Test]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task Check_DshAndDesktopFailures_StillReportOtherSources(bool failDsh, bool failDesktop)
    {
        var runtimes = new RuntimeRepository
        {
            LatestVersionResponse = failDsh
                ? Task.FromException<string>(new InvalidOperationException("npm unavailable"))
                : Task.FromResult("1.6.0"),
            Runtimes = [new("1.5.0", true, false)],
            PluginVersions = new() { ["dshmarket"] = "2.0.0" },
        };
        var plugins = new PluginManager([new("dshmarket", "1.0.0", true, true, "")]);
        var desktop = new DesktopUpdater(new("0.2.0"))
        {
            CheckFailure = failDesktop ? new InvalidOperationException("GitHub unavailable") : null,
        };
        var checker = new UpdateChecker(runtimes, plugins, desktop, Logger.None);

        UpdateCheckResult result = await checker.CheckAsync("latest", () => "1.5.0", () => false);

        await Assert.That(result.LatestDshVersion).IsEqualTo(failDsh ? null : "1.6.0");
        await Assert.That(result.LatestDesktopVersion).IsEqualTo(failDesktop ? null : "0.2.0");
        await Assert.That(result.CurrentDshVersion).IsEqualTo("1.5.0");
        await Assert.That(result.PluginUpdates.Count).IsEqualTo(1);
        await Assert.That(result.PluginUpdates[0]).IsEqualTo(new PluginUpdateInfo("dshmarket", "1.0.0", "2.0.0"));
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Check_PluginFailures_ArePropagated(bool failList)
    {
        var failure = new InvalidOperationException("plugin query failed");
        var runtimes = new RuntimeRepository { PluginQueryFailure = failList ? null : failure };
        var plugins = new PluginManager([new("dshmarket", "1.0.0", true, true, "")])
        {
            ListFailure = failList ? failure : null,
        };
        var checker = new UpdateChecker(runtimes, plugins, new DesktopUpdater(new("0.2.0")), Logger.None);
        Exception? observed = null;

        try
        {
            await checker.CheckAsync("latest", () => null, () => false);
        }
        catch (InvalidOperationException exception)
        {
            observed = exception;
        }

        await Assert.That(observed).IsEqualTo(failure);
    }

    [Test]
    [Arguments(true, true, true)]
    [Arguments(true, false, false)]
    [Arguments(false, true, false)]
    [Arguments(false, false, false)]
    public async Task Check_PreDownloadsOnlyWhenAvailableAndEnabled(
        bool available, bool enabled, bool shouldDownload)
    {
        var desktop = new DesktopUpdater(available ? new("0.2.0") : null);
        var checker = new UpdateChecker(new RuntimeRepository(), new PluginManager([]), desktop, Logger.None);

        await checker.CheckAsync("latest", () => null, () => enabled);

        await Assert.That(desktop.DownloadRequested).IsEqualTo(shouldDownload);
        await Assert.That(desktop.Applied).IsFalse();
    }

    [Test]
    public async Task Check_ObservesSettingsChangedDuringVersionLookup()
    {
        var lookup = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtimes = new RuntimeRepository { LatestVersionResponse = lookup.Task };
        var desktop = new DesktopUpdater(new("0.2.0"));
        var checker = new UpdateChecker(runtimes, new PluginManager([]), desktop, Logger.None);
        string activeRuntime = "1.0.0";
        bool autoDownload = false;

        Task<UpdateCheckResult> check = checker.CheckAsync("alpha", () => activeRuntime, () => autoDownload);
        activeRuntime = "1.5.0";
        autoDownload = true;
        lookup.SetResult("1.6.0");
        await check.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(runtimes.Channel).IsEqualTo("alpha");
        await Assert.That(runtimes.ActiveRuntime).IsEqualTo("1.5.0");
        await Assert.That(desktop.DownloadRequested).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Check_PreDownloadFinishesInBackgroundAndLogsOutcome(bool failDownload)
    {
        var download = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var desktop = new DesktopUpdater(new("0.2.0")) { DownloadResponse = download.Task };
        var sink = new LogSink();
        using var logger = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(sink).CreateLogger();
        var checker = new UpdateChecker(new RuntimeRepository(), new PluginManager([]), desktop, logger);
        using var source = new CancellationTokenSource();

        try
        {
            UpdateCheckResult result = await checker.CheckAsync("latest", () => null, () => true, source.Token)
                .WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(result.LatestDesktopVersion).IsEqualTo("0.2.0");
            await Assert.That(desktop.DownloadRequested).IsTrue();
            await Assert.That(desktop.DownloadToken).IsEqualTo(CancellationToken.None);
            source.Cancel();
            if (failDownload) download.SetException(new IOException("download failed"));
            else download.SetResult();

            LogEvent outcome = await sink.Event.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(outcome.Level).IsEqualTo(failDownload ? LogEventLevel.Debug : LogEventLevel.Information);
            await Assert.That(desktop.Applied).IsFalse();
        }
        finally
        {
            download.TrySetResult();
        }
    }

    private sealed class LogSink : ILogEventSink
    {
        public TaskCompletionSource<LogEvent> Event { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Emit(LogEvent logEvent) => Event.TrySetResult(logEvent);
    }

    private sealed class RuntimeRepository : IRuntimeRepository
    {
        public Task<string> LatestVersionResponse { get; init; } = Task.FromResult("1.6.0");
        public Exception? PluginQueryFailure { get; init; }
        public IReadOnlyList<DshRuntimeInfo> Runtimes { get; init; } = [];
        public Dictionary<string, string?> PluginVersions { get; init; } = [];
        public string? Channel { get; private set; }
        public string? ActiveRuntime { get; private set; }

        public Task<string> GetLatestVersionAsync(string channel, CancellationToken cancellationToken)
        {
            Channel = channel;
            return LatestVersionResponse;
        }

        public Task<string?> GetLatestPluginVersionAsync(string name, CancellationToken cancellationToken)
            => PluginQueryFailure is null
                ? Task.FromResult(PluginVersions[name])
                : Task.FromException<string?>(PluginQueryFailure);

        public Task<IReadOnlyList<DshRuntimeInfo>> ListRuntimesAsync(string? activeRuntime, CancellationToken cancellationToken)
        {
            ActiveRuntime = activeRuntime;
            return Task.FromResult(Runtimes);
        }

        public Task InstallAsync(string version, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class PluginManager(IReadOnlyList<PluginInfo> plugins) : IPluginManager
    {
        public Exception? ListFailure { get; init; }

        public Task<IReadOnlyList<PluginInfo>> ListPluginsAsync(CancellationToken cancellationToken)
            => ListFailure is null
                ? Task.FromResult(plugins)
                : Task.FromException<IReadOnlyList<PluginInfo>>(ListFailure);

        public Task SetEnabledAsync(string name, bool enabled, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task UninstallAsync(string name, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<string> InstallAsync(string source, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class DesktopUpdater(DesktopUpdateInfo? update) : IDesktopUpdater
    {
        public Exception? CheckFailure { get; init; }
        public Task DownloadResponse { get; init; } = Task.CompletedTask;
        public bool DownloadRequested { get; private set; }
        public bool Applied { get; private set; }
        public CancellationToken DownloadToken { get; private set; }
        public bool IsInstalled => true;

        public Task<DesktopUpdateInfo?> CheckForUpdatesAsync(CancellationToken cancellationToken)
            => CheckFailure is null ? Task.FromResult(update) : Task.FromException<DesktopUpdateInfo?>(CheckFailure);

        public Task DownloadAsync(IProgress<int>? progress, CancellationToken cancellationToken)
        {
            DownloadRequested = true;
            DownloadToken = cancellationToken;
            return DownloadResponse;
        }

        public void ApplyAndRestart() => Applied = true;
    }
}
