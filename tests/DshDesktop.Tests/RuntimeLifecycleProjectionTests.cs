using DshDesktop.Domain.Runtime;
using DshDesktop.Presentation.Avalonia.Features.Runtime;

namespace DshDesktop.Tests;

/// <summary>
/// Runtime 生命周期投影测试（候选 01：收敛三处散落映射）。
///
/// 背景：图标键、着色、底色三项由生命周期决定的映射原分散在
/// <c>DashboardView.ApplyLifecycleIndicator</c> 与 <c>RuntimeView.ApplyIndicators</c>，
/// 两处 switch 各自演化后**已经分岔**——同一个 <see cref="RuntimeLifecycle"/>
/// 在两个页面显示不同图标（Stopped：Info vs Power；Starting：Activity vs Refresh）。
/// 颜色映射则早已共享自 <see cref="RuntimeLifecycleBrushes"/>，唯独图标键缺席。
///
/// 本测试锁定合并后的唯一口径（基准 = RuntimeView，其 6 态各有专指图标）。
/// 断言的是投影的**语义**（哪个状态配哪个图标/色系），不涉及控件树。
/// </summary>
public sealed class RuntimeLifecycleProjectionTests
{
    [Test]
    public async Task Running_ShowsCheckIconInOkColors()
    {
        LifecycleProjection p = RuntimeLifecycleProjection.For(RuntimeLifecycle.Running);

        await Assert.That(p.IconKey).IsEqualTo("IconCheck");
        await Assert.That(p.Color).IsSameReferenceAs(RuntimeLifecycleBrushes.Running);
        await Assert.That(p.Tint).IsSameReferenceAs(RuntimeLifecycleBrushes.TintRunning);
    }

    [Test]
    public async Task Failed_ShowsAlertIconInErrorColors()
    {
        LifecycleProjection p = RuntimeLifecycleProjection.For(RuntimeLifecycle.Failed);

        await Assert.That(p.IconKey).IsEqualTo("IconAlert");
        await Assert.That(p.Color).IsSameReferenceAs(RuntimeLifecycleBrushes.Failed);
        await Assert.That(p.Tint).IsSameReferenceAs(RuntimeLifecycleBrushes.TintFailed);
    }

    [Test]
    public async Task Stopped_ShowsPowerIconInNeutralColors()
    {
        // 合并基准取 RuntimeView：Stopped 有专指图标（电源），而非 Dashboard 的通用 IconInfo。
        // 这是本次收敛带来的唯一可见变化——Dashboard 的就绪图标将随之改变。
        LifecycleProjection p = RuntimeLifecycleProjection.For(RuntimeLifecycle.Stopped);

        await Assert.That(p.IconKey).IsEqualTo("IconPower");
        await Assert.That(p.Color).IsSameReferenceAs(RuntimeLifecycleBrushes.Stopped);
        await Assert.That(p.Tint).IsSameReferenceAs(RuntimeLifecycleBrushes.TintStopped);
    }

    [Test]
    [Arguments(RuntimeLifecycle.Starting)]
    [Arguments(RuntimeLifecycle.Stopping)]
    [Arguments(RuntimeLifecycle.Recovering)]
    public async Task TransitionalStates_ShareRefreshIconAndWarnColors(RuntimeLifecycle lifecycle)
    {
        // 三个过渡态共用一个投影（与 RuntimeLifecycleBrushes.For 的分组一致）。
        LifecycleProjection p = RuntimeLifecycleProjection.For(lifecycle);

        await Assert.That(p.IconKey).IsEqualTo("IconRefresh");
        await Assert.That(p.Color).IsSameReferenceAs(RuntimeLifecycleBrushes.Transition);
        await Assert.That(p.Tint).IsSameReferenceAs(RuntimeLifecycleBrushes.TintTransition);
    }

    [Test]
    public async Task EveryLifecycleValue_MapsToDistinctOrDeliberateIcon()
    {
        // 守住「无状态落空」：任何 RuntimeLifecycle 成员都必须得到一个非空图标键。
        // 新增枚举成员却忘记补映射时，此条会失败。
        foreach (RuntimeLifecycle lifecycle in Enum.GetValues<RuntimeLifecycle>())
        {
            LifecycleProjection p = RuntimeLifecycleProjection.For(lifecycle);

            await Assert.That(p.IconKey).IsNotNull().And.IsNotEmpty();
            await Assert.That(p.Color).IsNotNull();
            await Assert.That(p.Tint).IsNotNull();
        }
    }

    [Test]
    public async Task ProjectionIsPure_SameInputSameReference()
    {
        // 投影必须无状态（View 每次属性变更都会调用它）：同输入恒等返回。
        LifecycleProjection first = RuntimeLifecycleProjection.For(RuntimeLifecycle.Running);
        LifecycleProjection second = RuntimeLifecycleProjection.For(RuntimeLifecycle.Running);

        await Assert.That(second.IconKey).IsEqualTo(first.IconKey);
        await Assert.That(second.Color).IsSameReferenceAs(first.Color);
        await Assert.That(second.Tint).IsSameReferenceAs(first.Tint);
    }
}
