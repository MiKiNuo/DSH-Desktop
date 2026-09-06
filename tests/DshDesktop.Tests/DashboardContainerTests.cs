using DshDesktop.Presentation.Avalonia.Composition;
using DshDesktop.Presentation.Avalonia.Features.Dashboard;

namespace DshDesktop.Tests;

/// <summary>
/// 生成容器冒烟测试（Phase 8 Issue 03 决策证据）：
/// Dashboard 独立 MVI 三元组（State/Intent/Effect/Reducer/ViewModel + 4 个兄弟 Store 构造注入）
/// 必须能被 GeneratedMviContainer 解析；若库仍不支持则本测试失败，退回混合方案并在 spec 记录偏离。
/// </summary>
public sealed class DashboardContainerTests
{
    [Test]
    public async Task GeneratedContainer_ResolvesDashboardViewModelWithSiblingStores()
    {
        var container = new GeneratedMviContainer(null!);

        DashboardViewModel viewModel = container.Resolve<DashboardViewModel>();

        await Assert.That(viewModel).IsNotNull();

        // 初始投影来自兄弟 Store 的 Initial 状态。
        await Assert.That(viewModel.PluginCount).IsEqualTo(0);
        await Assert.That(viewModel.HeroTitle).IsEqualTo("DSH 服务未运行");
    }
}
