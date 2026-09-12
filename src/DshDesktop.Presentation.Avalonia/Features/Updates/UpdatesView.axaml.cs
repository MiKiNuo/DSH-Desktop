using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using DshDesktop.Domain.Updates;
using DshDesktop.Presentation.Avalonia.Features.AppShell;
using MiKiNuo.Mvi.Platforms.Avalonia.Views;

namespace DshDesktop.Presentation.Avalonia.Features.Updates;

/// <summary>
/// 表示 Updates 视图：安装 / 激活 / 更新经载荷命令产生 Intent（View 只产生 Intent，§5 规则 1）。
/// 激活 Runtime 前插入二次确认（契约 §9）。
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

    private void OnInstallLatestClicked(object? sender, RoutedEventArgs args)
    {
        if (ViewModel.LatestDshVersion is { Length: > 0 } latest)
        {
            ViewModel.InstallDshRuntimeCommand.Execute(latest);
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
}

internal sealed class LocalCopyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is DshRuntimeInfo info && !info.IsActive && !info.IsBorrowed;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
