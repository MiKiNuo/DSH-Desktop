namespace DshDesktop.Application.Runtime;

/// <summary>
/// 表示 Runtime 进程 / HTTP 探测端口（ADR-0005 重接管与诊断编排共用；
/// Phase 8 评审 F9：原语自组合根下沉，真实实现落 Infrastructure）。
/// </summary>
public interface IRuntimeProbe
{
    /// <summary>
    /// 按 PID 判定进程存活。
    /// </summary>
    /// <param name="processId">进程 ID。</param>
    /// <returns>进程存在且未退出返回 true。</returns>
    bool IsProcessAlive(int processId);

    /// <summary>
    /// 按 host/port 做 HTTP 健康检查（2xx-4xx 视为存活，5xx/不可达视为不存活）。
    /// </summary>
    /// <param name="host">监听地址。</param>
    /// <param name="port">监听端口。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>HTTP 可达返回 true。</returns>
    Task<bool> IsHttpAliveAsync(string host, int port, CancellationToken cancellationToken);

    /// <summary>
    /// 按 PID 结束进程树（退化重启前清场）。
    /// </summary>
    /// <param name="processId">进程 ID。</param>
    void KillProcessTree(int processId);
}
