using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using DshDesktop.Domain.Updates;
using MiKiNuo.Mvi.Platforms.Avalonia.Views;

namespace DshDesktop.Presentation.Avalonia.Features.Updates;

/// <summary>
/// 表示 Updates 视图：安装 / 激活 / 更新经载荷命令产生 Intent（View 只产生 Intent，§5 规则 1）。
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

    private void OnActivateRuntimeClicked(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { DataContext: DshRuntimeInfo runtime })
        {
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
