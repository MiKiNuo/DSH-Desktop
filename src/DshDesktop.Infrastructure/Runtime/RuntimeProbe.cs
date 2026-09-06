using DshDesktop.Application.Runtime;

namespace DshDesktop.Infrastructure.Runtime;

/// <summary>
/// 表示 <see cref="IRuntimeProbe"/> 的真实实现（Phase 8 评审 F9：自组合根下沉；
/// 持有探测用 HttpClient，随宿主 Dispose）。
/// </summary>
public sealed class RuntimeProbe : IRuntimeProbe, IDisposable
{
    private readonly HttpClient _httpClient = new();

    /// <inheritdoc />
    public bool IsProcessAlive(int processId)
    {
        try
        {
            using System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false; // 进程不存在
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsHttpAliveAsync(string host, int port, CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await _httpClient
                .GetAsync($"http://{host}:{port}/", cancellationToken)
                .ConfigureAwait(false);
            return (int)response.StatusCode < 500;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public void KillProcessTree(int processId)
    {
        // 进程已死时 GetProcessById 抛 ArgumentException，由 RuntimeReattacher 归一化为 NotFound。
        using System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById(processId);
        process.Kill(entireProcessTree: true);
        process.WaitForExit(10_000);
    }

    /// <summary>
    /// 释放探测用 HttpClient。
    /// </summary>
    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
