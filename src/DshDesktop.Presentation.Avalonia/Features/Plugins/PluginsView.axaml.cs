using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using DshDesktop.Domain.Plugins;
using DshDesktop.Presentation.Avalonia.Features.AppShell;
using MiKiNuo.Mvi.Platforms.Avalonia.Views;
using MiKiNuo.Mvi.Presentation.Disposables;

namespace DshDesktop.Presentation.Avalonia.Features.Plugins;

/// <summary>
/// 表示 Plugins 视图（UI 重设计）：搜索 + 分段过滤（纯客户端）经
/// <see cref="PluginRowProjection"/> 投影；行内启停/卸载经命令产生 Intent（View 只产生 Intent，§5 规则 1）。
/// 卸载前插入二次确认（契约 §9）。
/// </summary>
public sealed partial class PluginsView : MviAvaloniaView<PluginsViewModel>
{
    private readonly TextBox _installSourceInput;
    private readonly TextBox _searchInput;
    private readonly TextBlock _countTag;
    private readonly Border _installPanel;
    private readonly Border _emptyState;
    private readonly Button _tabAll;
    private readonly Button _tabOn;
    private readonly Button _tabOff;
    private readonly ObservableCollection<PluginRow> _visibleRows = [];
    private PluginsViewModel? _viewModel;
    private string _activeFilter = "all";

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
        _installPanel = this.FindControl<Border>("InstallPanel")
            ?? throw new InvalidOperationException("无法找到 InstallPanel 控件。");
        _emptyState = this.FindControl<Border>("EmptyState")
            ?? throw new InvalidOperationException("无法找到 EmptyState 控件。");
        _tabAll = this.FindControl<Button>("TabAll")
            ?? throw new InvalidOperationException("无法找到 TabAll 控件。");
        _tabOn = this.FindControl<Button>("TabOn")
            ?? throw new InvalidOperationException("无法找到 TabOn 控件。");
        _tabOff = this.FindControl<Button>("TabOff")
            ?? throw new InvalidOperationException("无法找到 TabOff 控件。");
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
    /// 客户端过滤：搜索词 + 分段（全部/已启用/已禁用）过滤行；头部总数 tag 不随过滤变化。
    /// </summary>
    private void ApplyFilter(IReadOnlyList<PluginInfo>? plugins)
    {
        plugins ??= System.Array.Empty<PluginInfo>();
        IReadOnlySet<string> updatableNames = _viewModel?.UpdatablePlugins is { } names
            ? names.ToHashSet(StringComparer.Ordinal)
            : System.Array.Empty<string>().ToHashSet(StringComparer.Ordinal);
        IReadOnlyList<PluginRow> rows = PluginRowProjection.Filter(plugins, _searchInput.Text, updatableNames);

        if (_activeFilter == "on")
        {
            rows = rows.Where(r => r.Info.Enabled).ToArray();
        }
        else if (_activeFilter == "off")
        {
            rows = rows.Where(r => !r.Info.Enabled).ToArray();
        }

        _visibleRows.Clear();
        foreach (PluginRow row in rows)
        {
            _visibleRows.Add(row);
        }

        _countTag.Text = PluginRowProjection.CountText(plugins.Count);
        _emptyState.IsVisible = _visibleRows.Count == 0;
    }

    private void OnFilterAllClicked(object? sender, RoutedEventArgs args)
    {
        SetActiveTab("all", _tabAll);
        ApplyFilter(_viewModel?.Plugins);
    }

    private void OnFilterOnClicked(object? sender, RoutedEventArgs args)
    {
        SetActiveTab("on", _tabOn);
        ApplyFilter(_viewModel?.Plugins);
    }

    private void OnFilterOffClicked(object? sender, RoutedEventArgs args)
    {
        SetActiveTab("off", _tabOff);
        ApplyFilter(_viewModel?.Plugins);
    }

    private void SetActiveTab(string filter, Button selected)
    {
        _activeFilter = filter;
        foreach (Button tab in new[] { _tabAll, _tabOn, _tabOff })
        {
            if (tab != selected && tab.Classes.Contains("primary"))
            {
                tab.Classes.Remove("primary");
            }
        }

        if (!selected.Classes.Contains("primary"))
        {
            selected.Classes.Add("primary");
        }
    }

    private void OnInstallClicked(object? sender, RoutedEventArgs args)
    {
        _installPanel.IsVisible = true;
        _installSourceInput.Focus();
    }

    private void OnInstallStartClicked(object? sender, RoutedEventArgs args)
    {
        string source = _installSourceInput.Text?.Trim() ?? string.Empty;
        if (source.Length > 0 && _viewModel is not null)
        {
            _viewModel.InstallPluginCommand.Execute(source);
        }
    }

    private void OnInstallCancelClicked(object? sender, RoutedEventArgs args)
    {
        _installPanel.IsVisible = false;
        _installSourceInput.Text = string.Empty;
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

    private async void OnUninstallClicked(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { DataContext: PluginRow row })
        {
            bool ok = await ConfirmDialog.ShowAsync(ConfirmAction.UninstallPlugin, row.Name);
            if (!ok)
            {
                return;
            }

            _viewModel?.UninstallPluginCommand.Execute(row.Name);
        }
    }
}

/// <summary>
/// 视图内局部值转换器：表格里「真实值 / 骨架占位」二选一的显隐。
/// </summary>
internal static class Converters
{
    /// <summary>字符串为空 → true（骨架占位）。</summary>
    public static StringEmptyConverter IsEmpty { get; } = new(true);

    /// <summary>字符串非空 → true（真实值）。</summary>
    public static StringEmptyConverter IsNotEmpty { get; } = new(false);
}

internal sealed class StringEmptyConverter : IValueConverter
{
    private readonly bool _emptyWhen;

    public StringEmptyConverter(bool emptyWhen) => _emptyWhen = emptyWhen;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value as string) == _emptyWhen;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
