using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using DshDesktop.Domain.Diagnostics;
using MiKiNuo.Mvi.Platforms.Avalonia.Views;
using MiKiNuo.Mvi.Presentation.Disposables;

namespace DshDesktop.Presentation.Avalonia.Features.Diagnostics;

/// <summary>
/// 表示 Diagnostics 视图：Live 控制台 + 搜索/级别过滤 + 空状态切换 + 新事件到达时自动滚到底部；
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

    /// <summary>
    /// 初始化 Diagnostics 视图。
    /// </summary>
    public DiagnosticsView()
    {
        AvaloniaXamlLoader.Load(this);
        _entriesList = this.FindControl<ListBox>("EntriesList")
            ?? throw new InvalidOperationException("无法找到 EntriesList 控件。");
        _entriesList.ItemsSource = _rows;
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
        _rows.Clear();
        string q = _searchBox.Text?.Trim() ?? string.Empty;
        foreach (DiagnosticEvent entry in viewModel.Entries)
        {
            if (MatchesLevel(entry.Level, _levelFilter) && MatchesQuery(entry, q))
            {
                _rows.Add(new DiagnosticRow(entry));
            }
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

/// <summary>
/// 把 <see cref="DiagnosticRow"/> 映射为级别短标签（OK / INFO / WARN / ERROR），供 log-level 列展示。
/// </summary>
internal sealed class DiagnosticLevelConverter : IValueConverter
{
    /// <inheritdoc />
    public object? Convert(object? value, Type? targetType, object? parameter, CultureInfo? culture)
        => value switch
        {
            DiagnosticRow { IsOk: true } => "OK",
            DiagnosticRow { IsWarning: true } => "WARN",
            DiagnosticRow { IsError: true } => "ERROR",
            _ => "INFO",
        };

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type? targetType, object? parameter, CultureInfo? culture)
        => throw new NotSupportedException();
}
