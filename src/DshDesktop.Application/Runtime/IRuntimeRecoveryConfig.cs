namespace DshDesktop.Application.Runtime;

/// <summary>
/// 恢复 / 重接管 / 指标判定所需的配置读取端口（只读投影）：组合根把 DshDesktopConfig 适配进来，
/// Application 不新增对 Infrastructure 的项目引用（批 2b 端口先例）。
/// </summary>
public interface IRuntimeRecoveryConfig
{
    /// <summary>关闭窗口后保持 Runtime（ADR-0005 重接管谓词）。</summary>
    bool KeepRuntimeOnClose { get; }

    /// <summary>监听地址（重接管探测 host 参数）。</summary>
    string Host { get; }

    /// <summary>上次保留的 Runtime 进程 ID（重接管谓词）。</summary>
    int? LastRuntimePid { get; }

    /// <summary>上次保留的监听端口（重接管谓词）。</summary>
    int? LastRuntimePort { get; }

    /// <summary>"异常启动自动进入安全模式"开关（失败计数门控）。</summary>
    bool AutoSafeModeOnFailure { get; }

    /// <summary>上一个启动耗时（Dashboard 投影"上次"基准；写盘由端口承担）。</summary>
    long? LastStartupElapsedMs { get; }
}
