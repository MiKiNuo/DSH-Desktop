using DshDesktop.App.Composition;
using DshDesktop.Application.Diagnostics;
using DshDesktop.Domain.Diagnostics;
using DshDesktop.Infrastructure.Config;
using DshDesktop.Presentation.Avalonia.Features.Diagnostics;
using R3;

namespace DshDesktop.Tests;

public sealed class DesktopDiagnosticsCommandsTests
{
    [Test]
    public async Task ExportFailure_PublishesErrorWithoutThrowingOrRequiringRuntime()
    {
        DiagnosticsHub hub = new();
        List<DiagnosticEvent> events = [];
        using IDisposable subscription = hub.Events.Subscribe(events.Add);
        DshDesktopConfig GetConfig() => throw new InvalidOperationException("uninitialized");
        DesktopSettings settings = new(GetConfig, new ConfigPersistence((_, _) => Task.CompletedTask), _ => { });
        DesktopDiagnosticsCommands commands = new(hub, GetConfig,
            () => throw new InvalidOperationException("unexpected supervisor"),
            () => throw new InvalidOperationException("unexpected probe"),
            () => throw new InvalidOperationException("unexpected plugins"), settings);

        bool result = await commands.HandleExportDiagnosticsBundle(
            new ExportDiagnosticsBundleRequest("\0"), CancellationToken.None);

        await Assert.That(result).IsTrue();
        await Assert.That(events.Count).IsEqualTo(1);
        await Assert.That(events[0].Level).IsEqualTo(DiagnosticLevel.Error);
        await Assert.That(events[0].Message.Contains(DiagnosticEventNames.DiagnosisExportFailed, StringComparison.Ordinal)).IsTrue();
    }
}
