namespace DshDesktop.Presentation.Avalonia.Features.AppShell;

/// <summary>
/// 首启「缺少 DSH Runtime」弹窗的文案集中地（与 ConfirmDialogText 同一约定：
/// 规则集中在此以便单测，MainWindow 只按结果消费）。
/// </summary>
public static class RuntimeSetupText
{
    /// <summary>弹窗标题。</summary>
    public const string Title = "未检测到 DSH Runtime";

    /// <summary>弹窗正文：说明直接后果（工作台不可用）、动作内容（下载什么）与前提（需要网络）。</summary>
    public const string Body =
        "当前电脑尚未安装 DSH Runtime，DSH 工作台无法启动。"
        + "是否立即下载并安装？将下载 Node.js 运行时与最新版 DSH（需要网络连接）。";

    /// <summary>确认按钮文案：用具体动词说明后果。</summary>
    public const string AcceptLabel = "下载并安装";

    /// <summary>放弃按钮文案。</summary>
    public const string DeclineLabel = "暂不安装";

    /// <summary>失败态唯一按钮文案。</summary>
    public const string CloseLabel = "关闭";

    /// <summary>进度阶段：解析最新版本号。</summary>
    public const string ResolvingVersionStage = "正在查询最新版本…";

    /// <summary>进度阶段：下载 Node.js（附真实百分比进度条）。</summary>
    public const string DownloadingNodeStage = "正在下载 Node.js 运行时…";

    /// <summary>进度阶段：安装指定版本的 DSH Runtime（npm 安装无进度信号，不确定态）。</summary>
    public static string InstallingRuntimeStage(string version) => $"正在安装 DSH Runtime {version}…";

    /// <summary>失败态标题。</summary>
    public const string ErrorTitle = "安装失败";

    /// <summary>失败态正文：保留真实原因 + 告知重试路径（每次启动都会自检）。</summary>
    public static string ErrorBody(string reason) =>
        reason + "\n下次启动时会再次询问，届时可重试。";

    /// <summary>用户点「暂不安装」后的 toast：说清楚现状与下次机会。</summary>
    public const string DeclinedToast = "未安装 DSH Runtime，工作台暂不可用；下次启动会再次询问";

    /// <summary>安装完成 toast（随后自动启动 Runtime）。</summary>
    public const string CompletedToast = "DSH Runtime 安装完成，正在启动…";
}
