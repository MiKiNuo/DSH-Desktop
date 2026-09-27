using DshDesktop.Application.Diagnostics;
using DshDesktop.Application.Plugins;
using DshDesktop.Application.Runtime;
using DshDesktop.Domain.Diagnostics;
using DshDesktop.Domain.Runtime;
using DshDesktop.Infrastructure.Config;
using DshDesktop.Infrastructure.Diagnostics;
using DshDesktop.Presentation.Avalonia.Features.Diagnostics;
using DshDesktop.Presentation.Avalonia.Features.Settings;

namespace DshDesktop.App.Composition;

/// <summary>运行诊断、导出日志与打开日志目录；平台打开路径仍复用设置端口。</summary>
internal sealed class DesktopDiagnosticsCommands(
    DiagnosticsHub hub,
    Func<DshDesktopConfig> getConfig,
    Func<IRuntimeSupervisor> getSupervisor,
    Func<IRuntimeProbe> getProbe,
    Func<IPluginManager> getPlugins,
    DesktopSettings settings)
{
    private static string LogDirectory => Path.Combine(DshDesktopConfigStore.DataRoot, "logs");

    public async ValueTask<bool> HandleRunDiagnosisAsync(
        RunDiagnosisRequest request, CancellationToken cancellationToken)
    {
        DshDesktopConfig config = getConfig();
        RuntimeSnapshot snapshot = getSupervisor().Current;
        string profileDir = Path.Combine(config.DshHome, "profiles", "web");
        string host = config.Host;
        IPluginManager pluginRepository = getPlugins();
        DiagnosisRunner runner = new(hub);
        await runner.RunAsync(
        [
            new DiagnosisCheck("Runtime 进程健康",
                _ => Task.FromResult(snapshot.Lifecycle is RuntimeLifecycle.Running)),
            new DiagnosisCheck("HTTP 端点可达",
                token => snapshot.Port is { } port
                    ? getProbe().IsHttpAliveAsync(host, port, token)
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

    public ValueTask<bool> HandleExportDiagnosticsBundle(
        ExportDiagnosticsBundleRequest request, CancellationToken cancellationToken)
    {
        try
        {
            DiagnosticsBundleExporter.Export(LogDirectory, request.DestinationPath);
            hub.Publish(new DiagnosticEvent(
                DateTimeOffset.Now, DiagnosticSource.App, DiagnosticLevel.Success,
                $"\u2713 {DiagnosticEventNames.DiagnosisExportCompleted} {request.DestinationPath}"));
        }
        catch (Exception exception)
        {
            hub.Publish(new DiagnosticEvent(
                DateTimeOffset.Now, DiagnosticSource.App, DiagnosticLevel.Error,
                $"\u2717 {DiagnosticEventNames.DiagnosisExportFailed} {exception.Message}"));
        }

        return ValueTask.FromResult(true);
    }

    public ValueTask<bool> HandleOpenLogsDirectory(
        OpenLogsDirectoryRequest request, CancellationToken cancellationToken)
        => settings.HandleOpenPath(new OpenPathRequest(LogDirectory), cancellationToken);
}
