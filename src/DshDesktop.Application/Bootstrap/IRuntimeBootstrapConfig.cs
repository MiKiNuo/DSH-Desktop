namespace DshDesktop.Application.Bootstrap;

/// <summary>
/// 引导期自愈所需的配置读写端口（组合根拆分批 1）。
/// 把 <c>DshDesktopConfig</c>（Infrastructure）的运行时激活态与落盘语义抽象到 Application 层，
/// 避免 Application 新增对 Infrastructure 的项目引用。组合根注入适配器，内部转调 config 字段与
/// <c>ConfigPersistence</c> 同一把锁落盘。
/// </summary>
public interface IRuntimeBootstrapConfig
{
    /// <summary>借用入口路径（非空且文件存在 = 借用安装仍可用）。</summary>
    string? DshEntryPath { get; }

    /// <summary>当前激活的自建 Runtime 版本目录名（null/空 = 借用模式）。</summary>
    string? ActiveDshRuntime { get; set; }

    /// <summary>落盘当前配置（与组合根其余写路径共用 ConfigPersistence 同一把锁）。</summary>
    /// <param name="cancellationToken">取消标记。</param>
    Task PersistAsync(CancellationToken cancellationToken);
}
