using DshDesktop.Application.Bootstrap;

namespace DshDesktop.Application.Runtime;

/// <summary>Runtime 版本选择、持久化与原运行态恢复；宿主提供已接入 MVI 的启停动作。</summary>
public static class RuntimeActivation
{
    public static async Task ActivateAsync(
        IRuntimeBootstrapConfig config,
        string? target,
        bool wasRunning,
        Func<CancellationToken, Task> stop,
        Func<CancellationToken, Task> start,
        Func<string?, bool> canRestore,
        Action<string> failed,
        CancellationToken cancellationToken)
    {
        string? previous = config.ActiveDshRuntime;
        try
        {
            await stop(cancellationToken).ConfigureAwait(false);
            config.ActiveDshRuntime = target;
            await config.PersistAsync(cancellationToken).ConfigureAwait(false);
            if (wasRunning)
                await start(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            config.ActiveDshRuntime = previous;
            throw;
        }
        catch (Exception exception)
        {
            config.ActiveDshRuntime = previous;
            List<Exception> errors = [exception];
            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException("Runtime 激活回退已取消。", new AggregateException(errors), cancellationToken);
            try
            {
                await config.PersistAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception rollbackException)
            {
                errors.Add(rollbackException);
            }

            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException("Runtime 激活回退已取消。", new AggregateException(errors), cancellationToken);
            if (wasRunning)
            {
                if (canRestore(previous))
                {
                    try
                    {
                        await start(cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException cancelled)
                    {
                        errors.Add(cancelled);
                        throw new OperationCanceledException(
                            "Runtime 激活回退已取消。", new AggregateException(errors), cancelled.CancellationToken);
                    }
                    catch (Exception rollbackException)
                    {
                        errors.Add(rollbackException);
                        failed(new AggregateException(errors).Message);
                    }
                }
                else
                {
                    failed(exception.Message);
                }
            }
            if (errors.Count > 1)
                throw new AggregateException("Runtime 激活失败，回退也发生错误。", errors);
            throw;
        }
    }
}
