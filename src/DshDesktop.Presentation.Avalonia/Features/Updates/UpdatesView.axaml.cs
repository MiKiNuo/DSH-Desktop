using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using DshDesktop.Domain.Updates;
using DshDesktop.Presentation.Avalonia.Features.AppShell;
using MiKiNuo.Mvi.Platforms.Avalonia.Views;
using MiKiNuo.Mvi.Presentation.Disposables;

namespace DshDesktop.Presentation.Avalonia.Features.Updates;

/// <summary>
/// 表示 Updates 视图：安装 / 激活 / 更新经载荷命令产生 Intent（View 只产生 Intent，§5 规则 1）。
/// 激活 Runtime 前插入二次确认（契约 §9）。安装最新版成功后直接弹激活确认，
/// 引导用户完成版本切换而不是面对再次可点的安装按钮（2026-09-18 实机投诉）。
/// </summary>
public sealed partial class UpdatesView : MviAvaloniaView<UpdatesViewModel>
{
    /// <summary>
    /// 初始化 Updates 视图。
    /// </summary>
    public UpdatesView()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <inheritdoc />
    protected override void OnBind(UpdatesViewModel viewModel, MviDisposableBag bindings)
    {
        base.OnBind(viewModel, bindings);

        // 安装终态回流可能来自派发线程，弹确认框前须编组到 UI 线程。
        viewModel.ActivationConfirmationRequested += OnActivationConfirmationRequested;
        bindings.Add(() => viewModel.ActivationConfirmationRequested -= OnActivationConfirmationRequested);
    }

    private void OnInstallLatestClicked(object? sender, RoutedEventArgs args)
    {
        ViewModel.InstallLatestRuntime();
    }

    private async void OnActivateLatestClicked(object? sender, RoutedEventArgs args)
    {
        if (ViewModel.LatestDshVersion is { Length: > 0 } latest)
        {
            await PromptActivateRuntimeAsync(latest);
        }
    }

    /// <summary>
    /// ViewModel 仅在本次安装成功且本机副本待激活时请求一次确认。
    /// </summary>
    private void OnActivationConfirmationRequested(object? sender, string version)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnActivationConfirmationRequested(sender, version));
            return;
        }

        _ = PromptActivateRuntimeAsync(version);
    }

    private async Task PromptActivateRuntimeAsync(string version)
    {
        // 本机非借用副本的激活载荷直接用版本号（借用才传空串）。
        string current = ViewModel.CurrentDshVersion ?? "—";
        bool ok = await ConfirmDialog.ShowAsync(ConfirmAction.ActivateRuntime, $"{current}  →  {version}");
        if (ok)
        {
            ViewModel.ActivateDshRuntimeCommand.Execute(version);
        }
    }

    private async void OnActivateRuntimeClicked(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { DataContext: DshRuntimeInfo runtime })
        {
            string current = ViewModel.CurrentDshVersion ?? "—";
            bool ok = await ConfirmDialog.ShowAsync(ConfirmAction.ActivateRuntime, $"{current}  →  {runtime.Version}");
            if (!ok)
            {
                return;
            }

            ViewModel.ActivateDshRuntimeCommand.Execute(runtime.IsBorrowed ? string.Empty : runtime.Version);
        }
    }

    private void OnUpdatePluginClicked(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { DataContext: PluginUpdateInfo plugin })
        {
            ViewModel.UpdatePluginCommand.Execute(plugin.Name);
        }
    }
}

/// <summary>
/// 视图内局部值转换器：运行时为「本机副本」当且仅当既非当前激活也非借用。
/// </summary>
internal static class Converters
{
    /// <summary>是否为本机副本（!IsActive &amp;&amp; !IsBorrowed）。</summary>
    public static LocalCopyConverter LocalCopy { get; } = new();

    /// <summary>值是否非 null（页面进度条仅在 Desktop 下载存在真实百分比时出现）。</summary>
    public static IsNotNullConverter IsNotNull { get; } = new();

    /// <summary>枚举值是否等于 <c>ConverterParameter</c> 指定的成员名（DSH Runtime 卡片三态互斥显隐）。</summary>
    public static EnumEqualsConverter EnumEquals { get; } = new();
}

internal sealed class LocalCopyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is DshRuntimeInfo info && !info.IsActive && !info.IsBorrowed;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

internal sealed class IsNotNullConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

internal sealed class EnumEqualsConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not null
            && parameter is not null
            && string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
