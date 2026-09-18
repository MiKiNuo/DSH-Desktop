using System.ComponentModel;
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
    private bool _installToActivatePending;

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

        // 安装终态回流在派发线程直触发，弹确认框前须编组到 UI 线程（见 OnViewModelPropertyChanged）。
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        bindings.Add(() => viewModel.PropertyChanged -= OnViewModelPropertyChanged);
    }

    private void OnInstallLatestClicked(object? sender, RoutedEventArgs args)
    {
        if (ViewModel.LatestDshVersion is { Length: > 0 } latest)
        {
            _installToActivatePending = true;
            ViewModel.InstallDshRuntimeCommand.Execute(latest);
        }
    }

    private async void OnActivateLatestClicked(object? sender, RoutedEventArgs args)
    {
        await PromptActivateLatestAsync();
    }

    /// <summary>
    /// 安装终态检测：安装期间 PendingOperation 非空且壳遮罩锁导航，故「非空 → null」即安装/激活终态。
    /// 仅当本次安装由本视图发起且最新版已落本机（ReadyToActivate）时弹激活确认；
    /// 安装失败阶段停在 Available，只复位标志不弹窗。
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnViewModelPropertyChanged(sender, args));
            return;
        }

        if (args.PropertyName is nameof(UpdatesViewModel.PendingOperation)
            && ViewModel.PendingOperation is null
            && _installToActivatePending)
        {
            _installToActivatePending = false;
            if (ViewModel.DshStage is DshRuntimeStage.ReadyToActivate)
            {
                _ = PromptActivateLatestAsync();
            }
        }
    }

    private async Task PromptActivateLatestAsync()
    {
        if (ViewModel.LatestDshVersion is not { Length: > 0 } latest)
        {
            return;
        }

        // DshStage 保证 latest 是本机非借用副本，载荷直接用版本号（借用才传空串）。
        string current = ViewModel.CurrentDshVersion ?? "—";
        bool ok = await ConfirmDialog.ShowAsync(ConfirmAction.ActivateRuntime, $"{current}  →  {latest}");
        if (ok)
        {
            ViewModel.ActivateDshRuntimeCommand.Execute(latest);
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
