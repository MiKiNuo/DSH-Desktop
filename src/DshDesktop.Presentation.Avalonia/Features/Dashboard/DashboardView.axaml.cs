using Avalonia.Markup.Xaml;
using DshDesktop.Presentation.Avalonia.Features.Runtime;
using MiKiNuo.Mvi.Platforms.Avalonia.Views;

namespace DshDesktop.Presentation.Avalonia.Features.Dashboard;

/// <summary>
/// 表示 Dashboard 视图（§26：直接复用 RuntimeViewModel 投影与命令——
/// 同一 Store 即同一真实来源，物理上不可能出现双份状态分歧；
/// 库生成容器不支持同三元组注册第二个 ViewModel，故不立独立三元组）。
/// </summary>
public sealed partial class DashboardView : MviAvaloniaView<RuntimeViewModel>
{
    /// <summary>
    /// 初始化 Dashboard 视图。
    /// </summary>
    public DashboardView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
