namespace DshDesktop.Infrastructure.Config;

/// <summary>
/// config 落盘的唯一串行化入口（CONTEXT.md: Composition Root 的持久化职责下沉）：
/// 所有写路径（设置开关 / Runtime 版本切换 / 快照驱动的后台保存）共用同一把锁，
/// 并发写不再丢写。组合根内禁止直调 <see cref="DshDesktopConfigStore.SaveAsync"/>。
/// </summary>
public sealed class ConfigPersistence
{
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private readonly Func<DshDesktopConfig, CancellationToken, Task> _save;

    public ConfigPersistence()
        : this(DshDesktopConfigStore.SaveAsync)
    {
    }

    /// <summary>
    /// internal 供 DshDesktop.Tests 直测（InternalsVisibleTo）：注入记录型保存委托，
    /// 避免测试落盘真实 config。
    /// </summary>
    internal ConfigPersistence(Func<DshDesktopConfig, CancellationToken, Task> save) => _save = save;

    /// <summary>
    /// 持锁保存配置：并发调用串行执行，写盘失败原样传播。
    /// </summary>
    public async Task SaveAsync(DshDesktopConfig config, CancellationToken cancellationToken = default)
    {
        await _saveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _save(config, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _saveLock.Release();
        }
    }
}
