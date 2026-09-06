using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using MiKiNuo.Mvi.Platforms.Avalonia.Views;
using MiKiNuo.Mvi.Presentation.Disposables;

namespace DshDesktop.Presentation.Avalonia.Features.Diagnostics;

/// <summary>
/// 表示 Diagnostics 视图：Live 控制台 + 新事件到达时自动滚到底部；
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
        IReadOnlyList<DshDesktop.Domain.Diagnostics.DiagnosticEvent> entries = viewModel.Entries;

        // 快速路径：Reducer 语义为尾部追加（未满 1000 时），共享前缀仅做 O(1) 边界校验；
        // 触发头部截断（计数不变）时回退全量重建。
        if (entries.Count > _rows.Count
            && (_rows.Count == 0
                || entries[entries.Count - _rows.Count - 1].Equals(_rows[_rows.Count - 1].Event)))
        {
            int appendStart = _rows.Count; // 追加期间 _rows.Count 会变，先捕获起点。
            for (int i = appendStart; i < entries.Count; i++)
            {
                _rows.Add(new DiagnosticRow(entries[i]));
            }

            return;
        }

        _rows.Clear();
        foreach (DshDesktop.Domain.Diagnostics.DiagnosticEvent entry in entries)
        {
            _rows.Add(new DiagnosticRow(entry));
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
