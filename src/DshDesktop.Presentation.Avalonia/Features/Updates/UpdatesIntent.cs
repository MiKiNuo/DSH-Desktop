using DshDesktop.Domain.Updates;
using MiKiNuo.Mvi.Domain.MVI.Intent;

namespace DshDesktop.Presentation.Avalonia.Features.Updates;

/// <summary>
/// 表示 Updates 意图（业务语义命名，§7）。
/// </summary>
public abstract partial record UpdatesIntent : IMviIntent
{
    /// <summary>
    /// 表示检查更新意图。
    /// </summary>
    public sealed partial record CheckUpdates : UpdatesIntent;

    /// <summary>
    /// 表示检查更新完成的回流意图。
    /// </summary>
    /// <param name="Result">检查结果。</param>
    public sealed partial record CheckUpdatesCompleted(CheckUpdatesResponse Result) : UpdatesIntent;

    /// <summary>
    /// 表示安装指定版本 DSH Runtime 意图。
    /// </summary>
    /// <param name="Version">目标版本。</param>
    public sealed partial record InstallDshRuntime(string Version) : UpdatesIntent;

    /// <summary>
    /// 表示激活指定 Runtime 意图（空字符串 = 借用外部安装）。
    /// </summary>
    /// <param name="Version">版本目录名，空字符串表示借用。</param>
    public sealed partial record ActivateDshRuntime(string Version) : UpdatesIntent;

    /// <summary>
    /// 表示更新插件意图（走 §19 安装事务）。
    /// </summary>
    /// <param name="Name">插件包名。</param>
    public sealed partial record UpdatePlugin(string Name) : UpdatesIntent;

    /// <summary>
    /// 表示 Runtime 列表变化的回流意图。
    /// </summary>
    /// <param name="Runtimes">最新 Runtime 列表。</param>
    public sealed partial record RuntimeListChanged(IReadOnlyList<DshRuntimeInfo> Runtimes) : UpdatesIntent;

    /// <summary>
    /// 表示更新操作失败的回流意图。
    /// </summary>
    /// <param name="Error">错误信息。</param>
    public sealed partial record UpdatesOperationFailed(string Error) : UpdatesIntent;
}
