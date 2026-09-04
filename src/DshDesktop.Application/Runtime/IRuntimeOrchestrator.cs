namespace DshDesktop.Application.Runtime;

/// <summary>
/// 表示一次 Runtime 启动的全部输入。
/// </summary>
/// <remarks>
/// Spawn 形态复刻 Electron 壳（ADR-0001：Port=0 表示由宿主探测空闲端口）：
/// node.exe --expose-internals [HarnessNodeEntryPath] [EntryPath] web --no-open --host Host --port Port
/// （不带 Electron 专有的 --patch dsh-desktop.patch.yml，该文件引用 Electron 版专有插件）。
/// </remarks>
/// <param name="NodePath">node.exe 路径。</param>
/// <param name="EntryPath">DSH CLI 入口（@deepseek-ai/dsh/lib/bin.js）路径。</param>
/// <param name="HarnessNodeEntryPath">harness-node-entry.mjs 垫片路径；为 null 时直接以 EntryPath 启动。</param>
/// <param name="WorkingDirectory">DSH 进程工作目录。</param>
/// <param name="DshHome">DSH_HOME 数据根目录。</param>
/// <param name="Host">监听地址。</param>
/// <param name="Port">监听端口；0 表示启动时探测空闲端口。</param>
/// <param name="StartupTimeout">启动 + 就绪等待的超时时长。</param>
/// <param name="Progress">启动阶段进度回报；null 表示不回报。</param>
public sealed record RuntimeLaunchOptions(
    string NodePath,
    string EntryPath,
    string? HarnessNodeEntryPath,
    string WorkingDirectory,
    string DshHome,
    string Host,
    int Port,
    TimeSpan StartupTimeout,
    IProgress<DshDesktop.Domain.Runtime.RuntimeStartupStage>? Progress = null);

/// <summary>
/// 表示 Runtime 进程已拉起且 HTTP 就绪的结果。
/// </summary>
/// <param name="ProcessId">DSH 进程 ID。</param>
/// <param name="Port">实际监听端口。</param>
/// <param name="Url">DSH Web UI 完整地址（含会话 token，仅存内存，禁止落盘）。</param>
public sealed record RuntimeStartResult(int ProcessId, int Port, string Url);

/// <summary>
/// 表示 Runtime 进程退出事件参数。
/// </summary>
/// <param name="ExitCode">进程退出码，异常终止时可能为 null。</param>
public sealed class RuntimeExitedEventArgs(int? exitCode) : EventArgs
{
    /// <summary>获取进程退出码。</summary>
    public int? ExitCode { get; } = exitCode;
}

/// <summary>
/// 表示 Runtime 生命周期编排的最小边界（Q8 决策）：
/// 启动、停止、退出通知。Supervisor / 健康检查循环 / 崩溃恢复属于 Phase 2。
/// </summary>
public interface IRuntimeOrchestrator
{
    /// <summary>
    /// 启动 Runtime：拉起进程、从 stdout 捕获就绪 URL（含 token）、HTTP 轮询确认就绪。
    /// </summary>
    /// <param name="options">启动参数。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>启动结果。</returns>
    Task<RuntimeStartResult> StartAsync(RuntimeLaunchOptions options, CancellationToken cancellationToken);

    /// <summary>
    /// 停止 Runtime（ADR-0002：Windows 上直接结束进程树，与 Electron 实际行为等价）。
    /// </summary>
    /// <param name="cancellationToken">取消标记。</param>
    Task StopAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Runtime 进程退出时触发（无论正常或异常）。
    /// </summary>
    event EventHandler<RuntimeExitedEventArgs>? Exited;
}
