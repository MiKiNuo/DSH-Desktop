namespace DshDesktop.Presentation.Avalonia.Features.Updates;

/// <summary>Updates Store 自有的操作种类；插件事务由 Plugins Store 承载。</summary>
public enum UpdatesOperationKind
{
    /// <summary>下载 Desktop 更新。</summary>
    DesktopDownload,
    /// <summary>安装 DSH Runtime。</summary>
    RuntimeInstall,
    /// <summary>激活 DSH Runtime。</summary>
    RuntimeActivation,
}

/// <summary>Updates 自有操作的执行阶段。</summary>
public enum UpdatesOperationPhase
{
    /// <summary>操作进行中。</summary>
    Running,
    /// <summary>操作成功完成。</summary>
    Completed,
    /// <summary>操作失败。</summary>
    Failed,
}

/// <summary>Updates 自有操作事实；文案与进度仅为只读展示查询。</summary>
/// <param name="Kind">操作种类。</param>
/// <param name="Version">操作目标版本；激活时空字符串表示借用安装。</param>
/// <param name="Phase">执行阶段。</param>
/// <param name="Percent">最近一次 Desktop 下载进度；null 表示尚无进度回调。</param>
public sealed record UpdatesOperation(
    UpdatesOperationKind Kind,
    string Version,
    UpdatesOperationPhase Phase = UpdatesOperationPhase.Running,
    int? Percent = null)
{
    /// <summary>操作是否仍在执行。</summary>
    public bool IsInProgress => Phase is UpdatesOperationPhase.Running;

    /// <summary>Desktop 下载期间的确定进度；其他种类与终态为 null。</summary>
    public int? DesktopDownloadProgress =>
        IsInProgress && Kind is UpdatesOperationKind.DesktopDownload ? Percent ?? 0 : null;

    /// <summary>进行中操作的中文文案；终态为 null。</summary>
    public string? PendingOperation => !IsInProgress ? null : Kind switch
    {
        UpdatesOperationKind.DesktopDownload => Percent is { } percent
            ? $"下载 Desktop 更新 {Version}（{percent}%）…"
            : $"下载 Desktop 更新 {Version}…",
        UpdatesOperationKind.RuntimeInstall => $"安装 DSH Runtime {Version}…",
        UpdatesOperationKind.RuntimeActivation =>
            $"切换到 {(Version.Length == 0 ? "借用安装" : Version)} 并重启 Runtime…",
        _ => null,
    };
}
