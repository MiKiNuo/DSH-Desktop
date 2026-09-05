using MiKiNuo.Mvi.Domain.MVI.State;

namespace DshDesktop.Presentation.Avalonia.Features.Settings;

/// <summary>
/// 表示 Settings 状态（最小版：安全模式 + DSH 通道 + 只读环境信息）。
/// </summary>
/// <param name="SafeMode">是否处于安全模式（权威来源为 config，此处为投影）。</param>
/// <param name="Channel">DSH 更新通道（npm dist-tag：latest / alpha）。</param>
/// <param name="NodePath">node.exe 路径（只读）。</param>
/// <param name="DshHome">DSH_HOME 数据根目录（只读）。</param>
/// <param name="DataDirectory">Desktop 数据根目录（只读）。</param>
/// <param name="DesktopVersion">Desktop 版本（只读；占位 "—"，§50 收口任务接通）。</param>
/// <param name="PendingOperation">进行中的操作描述；null 表示空闲。</param>
/// <param name="LastError">最近一次错误信息。</param>
public sealed record SettingsState(
    bool SafeMode,
    string Channel,
    string? NodePath,
    string? DshHome,
    string? DataDirectory,
    string? DesktopVersion,
    string? PendingOperation,
    string? LastError) : IMviState
{
    /// <summary>
    /// 获取初始状态。
    /// </summary>
    public static SettingsState Initial { get; } =
        new(false, "latest", null, null, null, null, null, null);
}
