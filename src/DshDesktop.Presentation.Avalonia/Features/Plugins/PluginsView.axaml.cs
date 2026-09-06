using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using DshDesktop.Domain.Plugins;
using MiKiNuo.Mvi.Platforms.Avalonia.Views;
using MiKiNuo.Mvi.Presentation.Disposables;

namespace DshDesktop.Presentation.Avalonia.Features.Plugins;

/// <summary>
/// 表示 Plugins 视图（Phase 8 Issue 06，原型 plugins section）：
/// 搜索框客户端过滤（纯逻辑在 <see cref="PluginRowProjection"/>）+ 行内启停/卸载经命令产生 Intent
/// （View 只产生 Intent，§5 规则 1）。
/// </summary>
public sealed partial class PluginsView : MviAvaloniaView<PluginsViewModel>
{
    private readonly TextBox _installSourceInput;
    private readonly TextBox _searchInput;
    private readonly TextBlock _countTag;
    private readonly ObservableCollection<PluginRow> _visibleRows = [];
    private PluginsViewModel? _viewModel;

    /// <summary>
    /// 初始化 Plugins 视图。
    /// </summary>
    public PluginsView()
    {
        AvaloniaXamlLoader.Load(this);
        _installSourceInput = this.FindControl<TextBox>("InstallSourceInput")
            ?? throw new InvalidOperationException("无法找到 InstallSourceInput 控件。");
        _searchInput = this.FindControl<TextBox>("SearchInput")
            ?? throw new InvalidOperationException("无法找到 SearchInput 控件。");
        _countTag = this.FindControl<TextBlock>("CountTag")
            ?? throw new InvalidOperationException("无法找到 CountTag 控件。");
        ItemsControl rows = this.FindControl<ItemsControl>("Rows")
            ?? throw new InvalidOperationException("无法找到 Rows 控件。");
        rows.ItemsSource = _visibleRows;
    }

    /// <inheritdoc />
    protected override void OnBind(PluginsViewModel viewModel, MviDisposableBag bindings)
    {
        base.OnBind(viewModel, bindings);
        _viewModel = viewModel;
        bindings.Add(() => _viewModel = null);

        ApplyFilter(viewModel.Plugins);

        PropertyChangedEventHandler handler = (_, args) =>
        {
            if (args.PropertyName is nameof(PluginsViewModel.Plugins) or nameof(PluginsViewModel.UpdatablePlugins))
            {
                ApplyFilter(viewModel.Plugins);
            }
        };

        viewModel.PropertyChanged += handler;
        bindings.Add(() => viewModel.PropertyChanged -= handler);
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs args)
    {
        ApplyFilter(_viewModel?.Plugins);
    }

    /// <summary>
    /// 客户端过滤：按搜索词过滤行并重投影头部总数 tag（总数不随过滤变化）。
    /// </summary>
    private void ApplyFilter(IReadOnlyList<PluginInfo>? plugins)
    {
        plugins ??= System.Array.Empty<PluginInfo>();
        IReadOnlySet<string> updatableNames = _viewModel?.UpdatablePlugins is { } names
            ? names.ToHashSet(StringComparer.Ordinal)
            : System.Array.Empty<string>().ToHashSet(StringComparer.Ordinal);
        IReadOnlyList<PluginRow> rows = PluginRowProjection.Filter(plugins, _searchInput.Text, updatableNames);

        _visibleRows.Clear();
        foreach (PluginRow row in rows)
        {
            _visibleRows.Add(row);
        }

        _countTag.Text = PluginRowProjection.CountText(plugins.Count);
    }

    private void OnInstallClicked(object? sender, RoutedEventArgs args)
    {
        string source = _installSourceInput.Text?.Trim() ?? string.Empty;
        if (source.Length > 0 && _viewModel is not null)
        {
            _viewModel.InstallPluginCommand.Execute(source);
        }
    }

    private void OnEnableClicked(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { DataContext: PluginRow row })
        {
            _viewModel?.EnablePluginCommand.Execute(row.Name);
        }
    }

    private void OnUpdateClicked(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { DataContext: PluginRow row })
        {
            _viewModel?.UpdatePluginCommand.Execute(row.Name);
        }
    }

    private void OnDisableClicked(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { DataContext: PluginRow row })
        {
            _viewModel?.DisablePluginCommand.Execute(row.Name);
        }
    }

    private void OnUninstallClicked(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { DataContext: PluginRow row })
        {
            _viewModel?.UninstallPluginCommand.Execute(row.Name);
        }
    }
}
