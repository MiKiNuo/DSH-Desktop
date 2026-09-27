using DshDesktop.Application.Bootstrap;
using DshDesktop.Application.Runtime;

namespace DshDesktop.Tests;

public sealed class RuntimeActivationTests
{
    [Test]
    public async Task SaveFailure_RestoresSelectionAndPreviousRunningRuntime()
    {
        var host = new Host();
        var failure = new IOException("save target failed");
        host.Config.Save = attempt => attempt == 1 ? failure : null;

        Exception? error = await host.ActivateAsync();

        await Assert.That(error).IsEqualTo(failure);
        await Assert.That(host.Config.ActiveDshRuntime).IsEqualTo("old");
        await Assert.That(host.Config.Persisted).IsEqualTo("old");
        await Assert.That(string.Join(",", host.Started)).IsEqualTo("old");
        await Assert.That(host.Running).IsTrue();
        await Assert.That(host.Failed).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RollbackSaveFailure_StillRestartsAndPreservesBothErrors(bool startFails)
    {
        var host = new Host();
        var original = new IOException("target save failed");
        var rollback = new IOException("rollback save failed");
        var restart = new InvalidOperationException("old runtime failed");
        host.Config.Save = attempt => attempt == 1 ? original : rollback;
        host.StartFailure = _ => startFails ? restart : null;

        Exception? error = await host.ActivateAsync();

        await Assert.That(string.Join(",", host.Started)).IsEqualTo("old");
        await Assert.That(host.Config.ActiveDshRuntime).IsEqualTo("old");
        await Assert.That(error is AggregateException).IsTrue();
        var errors = ((AggregateException)error!).InnerExceptions;
        await Assert.That(errors[0]).IsEqualTo(original);
        await Assert.That(errors[1]).IsEqualTo(rollback);
        await Assert.That(errors.Count).IsEqualTo(startFails ? 3 : 2);
        if (startFails)
        {
            await Assert.That(errors[2]).IsEqualTo(restart);
            await Assert.That(host.Failed).Contains("old runtime failed");
        }
        else
        {
            await Assert.That(host.Running).IsTrue();
            await Assert.That(host.Failed).IsNull();
        }
    }

    [Test]
    public async Task ShutdownCancellation_RestoresMemoryWithoutRestartOrFailureSignal()
    {
        using var shutdown = new CancellationTokenSource();
        var host = new Host();
        host.Config.Save = _ => { shutdown.Cancel(); return new OperationCanceledException(shutdown.Token); };

        Exception? error = await host.ActivateAsync(shutdown.Token);

        await Assert.That(error is OperationCanceledException).IsTrue();
        await Assert.That(host.Config.ActiveDshRuntime).IsEqualTo("old");
        await Assert.That(host.Config.SaveCount).IsEqualTo(1);
        await Assert.That(host.Started.Count).IsEqualTo(0);
        await Assert.That(host.Failed).IsNull();
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Success_PreservesOriginalRunningPolicy(bool running)
    {
        var host = new Host { Running = running };
        Exception? error = await host.ActivateAsync();
        await Assert.That(error).IsNull();
        await Assert.That(host.Config.Persisted).IsEqualTo("new");
        await Assert.That(host.Running).IsEqualTo(running);
        await Assert.That(host.Started.Count).IsEqualTo(running ? 1 : 0);
        await Assert.That(host.Failed).IsNull();
    }

    [Test]
    [Arguments("old", true)]
    [Arguments(null, true)]
    [Arguments("missing", false)]
    public async Task StartupFailure_RestoresPreviousChoice(string? previous, bool available)
    {
        var host = new Host { CanRestore = available };
        host.Config.ActiveDshRuntime = previous;
        var original = new InvalidOperationException("new runtime failed");
        host.StartFailure = attempt => attempt == 1 ? original : null;
        Exception? error = await host.ActivateAsync();
        await Assert.That(error).IsEqualTo(original);
        await Assert.That(host.Config.ActiveDshRuntime).IsEqualTo(previous);
        await Assert.That(host.Config.Persisted).IsEqualTo(previous);
        await Assert.That(host.Started.Count).IsEqualTo(available ? 2 : 1);
        await Assert.That(host.Running).IsEqualTo(available);
        if (available)
            await Assert.That(host.Started[1]).IsEqualTo(previous);
        else
            await Assert.That(host.Failed).IsEqualTo("new runtime failed");
    }

    [Test]
    public async Task SaveFailure_OriginallyStopped_DoesNotStartOrSignalRuntimeFailure()
    {
        var host = new Host { Running = false };
        host.Config.Save = attempt => attempt == 1 ? new IOException("save failed") : null;
        Exception? error = await host.ActivateAsync();
        await Assert.That(error is IOException).IsTrue();
        await Assert.That(host.Config.ActiveDshRuntime).IsEqualTo("old");
        await Assert.That(host.Started.Count).IsEqualTo(0);
        await Assert.That(host.Failed).IsNull();
    }

    [Test]
    public async Task ShutdownDuringRollbackSave_DoesNotRestart()
    {
        using var shutdown = new CancellationTokenSource();
        var host = new Host();
        host.Config.Save = attempt =>
        {
            if (attempt == 1) return new IOException("save failed");
            shutdown.Cancel();
            return new OperationCanceledException(shutdown.Token);
        };
        Exception? error = await host.ActivateAsync(shutdown.Token);
        await Assert.That(error is OperationCanceledException).IsTrue();
        await Assert.That(host.Config.ActiveDshRuntime).IsEqualTo("old");
        await Assert.That(host.Started.Count).IsEqualTo(0);
        await Assert.That(host.Failed).IsNull();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task ShutdownAfterFailure_PreservesPriorErrors(int phase)
    {
        using var shutdown = new CancellationTokenSource();
        var host = new Host();
        var original = new IOException("activation failed");
        var rollback = new IOException("rollback save failed");
        var cancelled = new OperationCanceledException(shutdown.Token);
        host.Config.Save = attempt =>
        {
            if (attempt == 1)
            {
                if (phase == 0) shutdown.Cancel();
                return original;
            }
            if (phase == 1) { shutdown.Cancel(); return cancelled; }
            return rollback;
        };
        host.StartFailure = _ => { shutdown.Cancel(); return cancelled; };

        Exception? error = await host.ActivateAsync(shutdown.Token);

        await Assert.That(error is OperationCanceledException).IsTrue();
        await Assert.That(((OperationCanceledException)error!).CancellationToken).IsEqualTo(shutdown.Token);
        await Assert.That(error.InnerException is AggregateException).IsTrue();
        var errors = ((AggregateException)error.InnerException!).InnerExceptions;
        await Assert.That(errors[0]).IsEqualTo(original);
        await Assert.That(errors.Count).IsEqualTo(phase + 1);
        if (phase == 1) await Assert.That(errors[1]).IsEqualTo(cancelled);
        if (phase == 2)
        {
            await Assert.That(errors[1]).IsEqualTo(rollback);
            await Assert.That(errors[2]).IsEqualTo(cancelled);
        }
        await Assert.That(host.Config.ActiveDshRuntime).IsEqualTo("old");
        await Assert.That(host.Started.Count).IsEqualTo(phase == 2 ? 1 : 0);
        await Assert.That(host.Failed).IsNull();
    }

    private sealed class Config : IRuntimeBootstrapConfig
    {
        public string? DshEntryPath => "borrowed";
        public string? ActiveDshRuntime { get; set; } = "old";
        public string? Persisted { get; private set; } = "old";
        public Func<int, Exception?> Save { get; set; } = _ => null;
        public int SaveCount { get; private set; }
        public Task PersistAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Save(++SaveCount) is { } error) throw error;
            Persisted = ActiveDshRuntime;
            return Task.CompletedTask;
        }
    }

    private sealed class Host
    {
        public Config Config { get; } = new();
        public bool Running { get; set; } = true;
        public bool CanRestore { get; set; } = true;
        public List<string?> Started { get; } = [];
        public string? Failed { get; private set; }
        public Func<int, Exception?> StartFailure { get; set; } = _ => null;
        public async Task<Exception?> ActivateAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await RuntimeActivation.ActivateAsync(Config, "new", Running,
                    ct => { ct.ThrowIfCancellationRequested(); Running = false; return Task.CompletedTask; },
                    ct =>
                    {
                        ct.ThrowIfCancellationRequested();
                        Started.Add(Config.ActiveDshRuntime);
                        if (StartFailure(Started.Count) is { } error) throw error;
                        Running = true;
                        return Task.CompletedTask;
                    },
                    _ => CanRestore, message => Failed = message, cancellationToken);
                return null;
            }
            catch (Exception error) { return error; }
        }
    }
}
