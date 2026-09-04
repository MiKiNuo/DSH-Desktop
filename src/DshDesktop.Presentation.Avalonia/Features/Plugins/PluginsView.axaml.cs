using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using DshDesktop.Domain.Plugins;
using MiKiNuo.Mvi.Platforms.Avalonia.Views;

namespace DshDesktop.Presentation.Avalonia.Features.Plugins;

/// <summary>
/// 表示 Plugins 视图：行内切换/卸载与头部安装/全禁经命令产生 Intent（View 只产生 Intent，§5 规则 1）。
/// </summary>
public sealed partial class PluginsView : MviAvaloniaView<PluginsViewModel>
{
    private readonly TextBox _installSourceInput;

    /// <summary>
    /// 初始化 Plugins 视图。
    /// </summary>
    public PluginsView()
    {
        AvaloniaXamlLoader.Load(this);
        _installSourceInput = this.FindControl<TextBox>("InstallSourceInput")
            ?? throw new InvalidOperationException("无法找到 InstallSourceInput 控件。");
    }

    private void OnInstallClicked(object? sender, RoutedEventArgs args)
    {
        string source = _installSourceInput.Text?.Trim() ?? string.Empty;
        if (source.Length > 0)
        {
            ViewModel.InstallPluginCommand.Execute(source);
        }
    }

    private void OnToggleClicked(object? sender, RoutedEventArgs args)
    {
        if (sender is ToggleSwitch { DataContext: PluginInfo plugin })
        {
            if (plugin.Enabled)
            {
                ViewModel.DisablePluginCommand.Execute(plugin.Name);
            }
            else
            {
                ViewModel.EnablePluginCommand.Execute(plugin.Name);
            }
        }
    }

    private void OnUninstallClicked(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { DataContext: PluginInfo plugin })
        {
            ViewModel.UninstallPluginCommand.Execute(plugin.Name);
        }
    }
}
