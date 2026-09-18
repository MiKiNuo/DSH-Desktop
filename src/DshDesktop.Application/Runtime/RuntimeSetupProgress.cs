namespace DshDesktop.Application.Runtime;

/// <summary>
/// 首启 Runtime 安装编排的进度投影：组合根 → App → MainWindow 弹层。
/// </summary>
/// <param name="Stage">阶段文案（来自 RuntimeSetupText，已本地化）。</param>
/// <param name="Percent">确定进度 0-100；负值 = 不确定态（npm 安装无进度信号）。</param>
public readonly record struct RuntimeSetupProgress(string Stage, int Percent);
