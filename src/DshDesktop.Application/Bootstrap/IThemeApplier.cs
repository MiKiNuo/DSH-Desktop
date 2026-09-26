namespace DshDesktop.Application.Bootstrap;

/// <summary>
/// 外观主题套用端口（组合根拆分批 2a）：把组合根的 <c>ApplyTheme</c>（走 UI 线程 Post）抽象到 Application 层，
/// 供启动编排在配置加载同一时点触发主题套用，可观测顺序与原组合根一致。
/// </summary>
public interface IThemeApplier
{
    /// <summary>套用外观主题到当前 Application（RequestedThemeVariant 须走 UI 线程）。</summary>
    /// <param name="theme">主题名（"Dark"/"Light"）。</param>
    void ApplyTheme(string theme);
}
