using Avalonia.Styling;
using Avalonia.Threading;
using DshDesktop.Application.Paths;
using DshDesktop.Application.Startup;
using DshDesktop.Infrastructure.Config;
using DshDesktop.Presentation.Avalonia.Features.Settings;
using Serilog;

namespace DshDesktop.App.Composition;

/// <summary>桌面设置投影与持久化；所有写入复用宿主的 ConfigPersistence。</summary>
internal sealed class DesktopSettings(
    Func<DshDesktopConfig> getConfig,
    ConfigPersistence persistence,
    Action<bool> safeModeChanged)
{
    // 平台端口在 Runtime 初始化完成后绑定，保持启动前打开目录的失败语义。
    public IPathOpener? PathOpener { get; set; }
    public StartupRegistrationService? StartupRegistration { get; set; }

    public ValueTask<SettingsInfo> HandleGetSettingsInfo(
        GetSettingsInfoRequest request, CancellationToken cancellationToken)
    {
        DshDesktopConfig config = getConfig();
        string runtimeRoot = Path.Combine(Directory.GetParent(config.DshHome)!.FullName, "runtime", "dsh");
        string pluginsDirectory = Path.Combine(config.DshHome, "profiles", "web", "node_modules");
        string dshRuntimeDirectory = config.ActiveDshRuntime is { Length: > 0 } active
            ? Path.Combine(runtimeRoot, active)
            : runtimeRoot;
        return ValueTask.FromResult(new SettingsInfo(
            config.SafeMode, config.NotificationsEnabled, config.DshChannel, config.NodePath,
            config.DshHome, DshDesktopConfigStore.DataRoot, pluginsDirectory, dshRuntimeDirectory,
            config.MinimizeToTrayOnClose, config.LaunchOnStartup, config.BackgroundUpdateCheck,
            config.AutoDownloadUpdates, config.Theme));
    }

    public async ValueTask<bool> HandleSetThemeAsync(
        SetThemeRequest request, CancellationToken cancellationToken)
    {
        DshDesktopConfig config = getConfig();
        config.Theme = request.Theme;
        ApplyTheme(request.Theme);
        await persistence.SaveAsync(config, cancellationToken).ConfigureAwait(false);
        Log.Logger.Information("Settings.Theme {Theme}", request.Theme);
        return true;
    }

    public static void ApplyTheme(string theme)
    {
        global::Avalonia.Application? app = global::Avalonia.Application.Current;
        if (app is null)
        {
            return;
        }

        ThemeVariant variant = string.Equals(theme, "Light", StringComparison.Ordinal)
            ? ThemeVariant.Light
            : ThemeVariant.Dark;
        Dispatcher.UIThread.Post(() => app.RequestedThemeVariant = variant);
    }

    public async ValueTask<bool> HandleSetLaunchOnStartupAsync(
        SetLaunchOnStartupRequest request, CancellationToken cancellationToken)
    {
        DshDesktopConfig config = getConfig();
        config.LaunchOnStartup = request.Enabled;
        await persistence.SaveAsync(config, cancellationToken).ConfigureAwait(false);
        // 注册表失败仍上抛；已保存的配置不回滚，保持原设置语义。
        StartupRegistration?.SetEnabled(request.Enabled);
        Log.Logger.Information("Settings.LaunchOnStartup {Enabled}", request.Enabled);
        return true;
    }

    public ValueTask<bool> HandleOpenPath(OpenPathRequest request, CancellationToken cancellationToken)
    {
        if (PathOpener is null)
        {
            throw new InvalidOperationException("当前平台不支持打开目录。");
        }

        PathOpener.Open(request.Path);
        return ValueTask.FromResult(true);
    }

    public async Task SetSafeModeCoreAsync(bool enabled, CancellationToken cancellationToken)
    {
        DshDesktopConfig config = getConfig();
        config.SafeMode = enabled;
        await persistence.SaveAsync(config, cancellationToken).ConfigureAwait(false);
        Log.Logger.Information("Runtime.SafeMode {Enabled}", enabled);
        safeModeChanged(enabled);
    }

    public async ValueTask<bool> SetConfigFlagAsync<TRequest>(
        TRequest request, CancellationToken cancellationToken,
        Action<DshDesktopConfig> apply, string logTemplate, object logValue)
    {
        DshDesktopConfig config = getConfig();
        apply(config);
        await persistence.SaveAsync(config, cancellationToken).ConfigureAwait(false);
        Log.Logger.Information(logTemplate, logValue);
        return true;
    }
}
