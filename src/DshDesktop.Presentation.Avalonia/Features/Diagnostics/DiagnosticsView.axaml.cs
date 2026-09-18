using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using DshDesktop.Domain.Diagnostics;
using MiKiNuo.Mvi.Platforms.Avalonia.Views;
using MiKiNuo.Mvi.Presentation.Disposables;

namespace DshDesktop.Presentation.Avalonia.Features.Diagnostics;

/// <summary>
/// 表示 Diagnostics 视图：Live 控制台 + 搜索/级别过滤 + 空状态切换 + 新事件到达时自动滚到底部 +
/// 右键复制日志（单条 / 当前筛选全部）；
/// 导出诊断包经 Avalonia StorageProvider 保存对话框取目标路径（对话框属 View 层职责）。
/// </summary>
public sealed partial class DiagnosticsView : MviAvaloniaView<DiagnosticsViewModel>
{
    private static readonly FilePickerFileType ZipFileType = new("ZIP 压缩包")
    {
        Patterns = ["*.zip"],
    };

    private readonly ListBox _entriesList;
    private readonly ObservableCollection<DiagnosticRow> _rows = [];
    private readonly Border _emptyBorder;
    private readonly TextBox _searchBox;
    private readonly Button[] _filterButtons;
    private string _levelFilter = "all";
    private DiagnosticsViewModel? _viewModel;
    private DiagnosticRow? _contextRow;

    /// <summary>
    /// 初始化 Diagnostics 视图。
    /// </summary>
    public DiagnosticsView()
    {
        AvaloniaXamlLoader.Load(this);
        _entriesList = this.FindControl<ListBox>("EntriesList")
            ?? throw new InvalidOperationException("无法找到 EntriesList 控件。");
        _entriesList.ItemsSource = _rows;
        // 多选（XAML SelectionMode="Multiple"）+ Ctrl+C/右键复制选中行；
        // 右键单条/全部仍走 ContextRequested 登记的目标行。
        _entriesList.ContextRequested += OnContextRequested;
        _entriesList.KeyDown += OnEntriesListKeyDown;
        _emptyBorder = this.FindControl<Border>("EmptyState")
            ?? throw new InvalidOperationException("无法找到 EmptyState 控件。");
        _searchBox = this.FindControl<TextBox>("SearchBox")
            ?? throw new InvalidOperationException("无法找到 SearchBox 控件。");
        _filterButtons =
        [
            this.FindControl<Button>("FilterAll")
                ?? throw new InvalidOperationException("无法找到 FilterAll 控件。"),
            this.FindControl<Button>("FilterInfo")
                ?? throw new InvalidOperationException("无法找到 FilterInfo 控件。"),
            this.FindControl<Button>("FilterWarn")
                ?? throw new InvalidOperationException("无法找到 FilterWarn 控件。"),
            this.FindControl<Button>("FilterErr")
                ?? throw new InvalidOperationException("无法找到 FilterErr 控件。"),
        ];
        // 默认选中「全部」。
        _filterButtons[0].Classes.Add("primary");
    }

    /// <inheritdoc />
    protected override void OnBind(DiagnosticsViewModel viewModel, MviDisposableBag bindings)
    {
        base.OnBind(viewModel, bindings);
        _viewModel = viewModel;
        bindings.Add(() => _viewModel = null);

        SyncRows(viewModel);
        ScrollToEnd();

        PropertyChangedEventHandler handler = (_, args) =>
        {
            if (args.PropertyName is nameof(DiagnosticsViewModel.Entries))
            {
                SyncRows(viewModel);
                ScrollToEnd();
            }
        };

        viewModel.PropertyChanged += handler;
        bindings.Add(() => viewModel.PropertyChanged -= handler);
    }

    private void SyncRows(DiagnosticsViewModel viewModel)
    {
        // 过滤由 View 层承担（搜索词 + 级别分段），数据来自 viewModel.Entries。
        // Clear 重建会清空选择，先按源事件身份记下选中行，重建后恢复，Live 流水下多选才不会被刷掉。
        var selectedEvents = _entriesList.SelectedItems?
            .OfType<DiagnosticRow>()
            .Select(row => row.Event)
            .ToHashSet() ?? [];
        _rows.Clear();
        string q = _searchBox.Text?.Trim() ?? string.Empty;
        foreach (DiagnosticEvent entry in viewModel.Entries)
        {
            if (MatchesLevel(entry.Level, _levelFilter) && MatchesQuery(entry, q))
            {
                _rows.Add(new DiagnosticRow(entry));
            }
        }

        foreach (DiagnosticRow row in _rows.Where(row => selectedEvents.Contains(row.Event)))
        {
            _entriesList.SelectedItems.Add(row);
        }

        _emptyBorder.IsVisible = _rows.Count == 0;
        ScrollToEnd();
    }

    private static bool MatchesLevel(DiagnosticLevel level, string filter) => filter switch
    {
        "all" => true,
        "info" => level is DiagnosticLevel.Info or DiagnosticLevel.Debug or DiagnosticLevel.Success,
        "warn" => level == DiagnosticLevel.Warning,
        "err" => level == DiagnosticLevel.Error,
        _ => true,
    };

    private static bool MatchesQuery(DiagnosticEvent e, string q)
        => string.IsNullOrEmpty(q)
            || e.Message.Contains(q, StringComparison.OrdinalIgnoreCase)
            || e.Timestamp.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)
                .Contains(q, StringComparison.OrdinalIgnoreCase);

    private void OnSearchChanged(object? sender, TextChangedEventArgs args)
    {
        if (_viewModel is not null)
        {
            SyncRows(_viewModel);
        }
    }

    private void OnFilterClicked(object? sender, RoutedEventArgs args)
    {
        if (sender is not Button btn)
        {
            return;
        }

        foreach (Button b in _filterButtons)
        {
            b.Classes.Remove("primary");
        }

        btn.Classes.Add("primary");
        _levelFilter = btn.Name switch
        {
            "FilterInfo" => "info",
            "FilterWarn" => "warn",
            "FilterErr" => "err",
            _ => "all",
        };
        if (_viewModel is not null)
        {
            SyncRows(_viewModel);
        }
    }

    private void ScrollToEnd()
    {
        if (_rows.Count > 0)
        {
            _entriesList.ScrollIntoView(_rows[_rows.Count - 1]);
        }
    }

    /// <summary>
    /// 右键请求：登记本次要拷贝的目标行。
    /// 右键菜单挂在 ListBox 上，弹层不继承行 DataContext（Avalonia 12 的 Popup 只把菜单挂到逻辑父级），
    /// 故从命中元素沿视觉树上溯 ListBoxItem 取行数据；列表空白区命中不到行时回落到当前选中行。
    /// </summary>
    private void OnContextRequested(object? sender, ContextRequestedEventArgs args)
    {
        ListBoxItem? item = (args.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true);
        _contextRow = item?.DataContext as DiagnosticRow ?? _entriesList.SelectedItem as DiagnosticRow;
    }

    /// <summary>Ctrl+C：有选中行时复制选中集（按列表顺序），无选中时不拦截，交给系统默认处理。</summary>
    private async void OnEntriesListKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key == Key.C
            && args.KeyModifiers == KeyModifiers.Control
            && _entriesList.SelectedItems?.Count > 0)
        {
            args.Handled = true;
            await CopySelectedRowsAsync();
        }
    }

    /// <summary>复制当前选中的日志行；无选中时回落到被右键的那条。</summary>
    private async void OnCopySelectedClicked(object? sender, RoutedEventArgs args)
    {
        if (_entriesList.SelectedItems?.Count > 0)
        {
            await CopySelectedRowsAsync();
        }
        else if (_contextRow is { } row)
        {
            await CopyToClipboardAsync(DiagnosticCopyText.ForRow(row));
        }
    }

    /// <summary>把选中行按列表显示顺序拼装后写剪贴板（SelectedItems 不保证顺序）。</summary>
    private async Task CopySelectedRowsAsync()
    {
        var selected = _rows.Where(row => _entriesList.SelectedItems?.Contains(row) == true).ToList();
        if (selected.Count > 0)
        {
            await CopyToClipboardAsync(DiagnosticCopyText.ForRows(selected));
        }
    }

    /// <summary>复制被右键的那条日志（时间戳 + 级别 + 正文，多行堆栈原样保留）。</summary>
    private async void OnCopyRowClicked(object? sender, RoutedEventArgs args)
    {
        if (_contextRow is { } row)
        {
            await CopyToClipboardAsync(DiagnosticCopyText.ForRow(row));
        }
    }

    /// <summary>复制当前筛选（级别分段 + 搜索词）命中的全部日志。</summary>
    private async void OnCopyAllClicked(object? sender, RoutedEventArgs args)
    {
        if (_rows.Count > 0)
        {
            await CopyToClipboardAsync(DiagnosticCopyText.ForRows(_rows));
        }
    }

    /// <summary>
    /// 清除日志：经 MVI 命令清空 UI Store 展示窗口（右键弹层不继承行 DataContext，故走 Click 处理器）。
    /// 只清界面，磁盘日志 data/logs/ 与后续 Live 事件流不受影响。
    /// </summary>
    private void OnClearClicked(object? sender, RoutedEventArgs args)
    {
        _viewModel?.ClearEntriesCommand.Execute(null);
    }

    /// <summary>写系统剪贴板；写入失败非致命（async void 无人兜底，吞掉防崩进程）。</summary>
    private async Task CopyToClipboardAsync(string text)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }

        try
        {
            await clipboard.SetTextAsync(text);
        }
        catch (Exception)
        {
            return; // 剪贴板被占用等情况下静默放弃，不影响页面其余功能。
        }
    }

    /// <summary>
    /// 导出诊断包：保存文件对话框取目标路径后经命令产生 Intent（View 只产生 Intent，§5 规则 1）。
    /// </summary>
    private async void OnExportClicked(object? sender, RoutedEventArgs args)
    {
        if (_viewModel is null || TopLevel.GetTopLevel(this) is not { StorageProvider.CanSave: true } topLevel)
        {
            return;
        }

        IStorageFile? file;
        try
        {
            file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "导出诊断包",
                SuggestedFileName = $"dsh-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
                DefaultExtension = "zip",
                FileTypeChoices = [ZipFileType],
            });
        }
        catch (Exception)
        {
            return; // 对话框失败非致命（async void 无人兜底，吞掉防崩进程）。
        }

        if (file is not null)
        {
            _viewModel.ExportDiagnosticsBundleCommand.Execute(file.Path.LocalPath);
        }
    }
}
