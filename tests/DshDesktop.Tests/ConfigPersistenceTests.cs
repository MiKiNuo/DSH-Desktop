using DshDesktop.Infrastructure.Config;

namespace DshDesktop.Tests;

/// <summary>
/// ConfigPersistence 守卫：config 落盘的唯一串行化入口（组合根 _configSaveLock 下沉至 Infrastructure）。
/// 缺陷背景：组合根内 4 处直调 DshDesktopConfigStore.SaveAsync 绕过 _configSaveLock，
/// 与快照驱动的后台保存并发时丢写。收口后：① 并发写串行不重叠；② 保存失败原样传播。
/// 注入保存委托为唯一测试缝（避免落盘真实 config）。
/// </summary>
public sealed class ConfigPersistenceTests
{
    [Test]
    public async Task SaveAsync_SerializesConcurrentCalls()
    {
        int inside = 0;
        int overlap = 0;
        int calls = 0;
        ConfigPersistence persistence = new(async (_, _) =>
        {
            if (Interlocked.Increment(ref inside) != 1)
            {
                Interlocked.Increment(ref overlap);
            }

            await Task.Delay(10);
            Interlocked.Decrement(ref inside);
            Interlocked.Increment(ref calls);
        });

        DshDesktopConfig config = new();
        await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => persistence.SaveAsync(config))
            .ToArray());

        await Assert.That(overlap).IsEqualTo(0);
        await Assert.That(calls).IsEqualTo(8);
    }

    [Test]
    public async Task SaveAsync_PropagatesSaveFailure()
    {
        ConfigPersistence persistence = new((_, _) => throw new InvalidOperationException("boom"));
        DshDesktopConfig config = new();

        await Assert.That(async () => await persistence.SaveAsync(config))
            .Throws<InvalidOperationException>();
    }
}
