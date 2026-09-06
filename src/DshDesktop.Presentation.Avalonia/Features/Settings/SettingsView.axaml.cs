using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using MiKiNuo.Mvi.Platforms.Avalonia.Views;

namespace DshDesktop.Presentation.Avalonia.Features.Settings;

/// <summary>
/// 表示 Settings 视图：开关/下拉经命令产生 Intent（View 只产生 Intent，§5 规则 1）。
/// </summary>
public sealed partial class SettingsView : MviAvaloniaView<SettingsViewModel>
{
    private static readonly string[] Channels = ["latest", "alpha"];

    /// <summary>
    /// 初始化 Settings 视图。
    /// </summary>
    public SettingsView()
    {
        AvaloniaXamlLoader.Load(this);
        ComboBox channelBox = this.FindControl<ComboBox>("ChannelBox")
            ?? throw new InvalidOperationException("无法找到 ChannelBox 控件。");
        channelBox.ItemsSource = Channels;
    }

    private void OnSafeModeToggled(object? sender, RoutedEventArgs args)
    {
        // 无载荷翻转：目标状态由 Reducer 从 State 推导（消双击视觉/状态分歧窗口）。
        ViewModel.ToggleSafeModeCommand.Execute(null);
    }

    private void OnNotificationsToggled(object? sender, RoutedEventArgs args)
    {
        // 无载荷翻转，同 SafeMode 先例。
        ViewModel.ToggleNotificationsCommand.Execute(null);
    }

    private void OnChannelSelectionChanged(object? sender, SelectionChangedEventArgs args)
    {
        // 守卫：加载回流导致的选中同步不重复落盘。
        if (sender is ComboBox { SelectedItem: string channel }
            && !string.Equals(channel, ViewModel.Channel, StringComparison.Ordinal))
        {
            ViewModel.ChangeChannelCommand.Execute(channel);
        }
    }

    // ===== Phase 8 Issue 05：桌面行为 / 更新策略开关（无载荷翻转）与打开目录（载荷来自 State 投影） =====

    private void OnMinimizeToTrayOnCloseToggled(object? sender, RoutedEventArgs args)
    {
        ViewModel.ToggleMinimizeToTrayOnCloseCommand.Execute(null);
    }

    private void OnLaunchOnStartupToggled(object? sender, RoutedEventArgs args)
    {
        ViewModel.ToggleLaunchOnStartupCommand.Execute(null);
    }

    private void OnBackgroundUpdateCheckToggled(object? sender, RoutedEventArgs args)
    {
        ViewModel.ToggleBackgroundUpdateCheckCommand.Execute(null);
    }

    private void OnAutoDownloadUpdatesToggled(object? sender, RoutedEventArgs args)
    {
        ViewModel.ToggleAutoDownloadUpdatesCommand.Execute(null);
    }

    private void OnOpenDataDirectoryClicked(object? sender, RoutedEventArgs args)
    {
        ViewModel.OpenDirectoryCommand.Execute(ViewModel.DataDirectory);
    }

    private void OnOpenPluginsDirectoryClicked(object? sender, RoutedEventArgs args)
    {
        ViewModel.OpenDirectoryCommand.Execute(ViewModel.PluginsDirectory);
    }

    private void OnOpenDshRuntimeDirectoryClicked(object? sender, RoutedEventArgs args)
    {
        ViewModel.OpenDirectoryCommand.Execute(ViewModel.DshRuntimeDirectory);
    }
}
