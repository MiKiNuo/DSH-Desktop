namespace DshDesktop.Presentation.Avalonia.Features.AppShell;

/// <summary>
/// 需要二次确认的高影响操作。这些操作此前点击即执行，无任何确认。
/// </summary>
public enum ConfirmAction
{
    /// <summary>卸载第三方插件（走插件事务，失败回滚）。</summary>
    UninstallPlugin,

    /// <summary>停止 DSH Runtime（工作台断连）。</summary>
    StopRuntime,

    /// <summary>切换并重启 Runtime 到另一版本（失败自动回退）。</summary>
    ActivateRuntime,
}

/// <summary>
/// 二次确认弹窗的文案与风险等级映射（视觉基准 docs/DSH-Desktop-UI-Redesign.html）。
/// 规则集中在此以便单测，MainWindow 只按结果消费。
/// </summary>
public static class ConfirmDialogText
{
    /// <summary>弹窗标题。</summary>
    public static string Title(ConfirmAction action) => action switch
    {
        ConfirmAction.UninstallPlugin => "卸载插件",
        ConfirmAction.StopRuntime => "停止 DSH Runtime",
        ConfirmAction.ActivateRuntime => "切换 Runtime 版本",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
    };

    /// <summary>
    /// 弹窗正文：说明直接后果，以及失败时的安全网。用户点确认前必须能看到这两点。
    /// </summary>
    public static string Body(ConfirmAction action) => action switch
    {
        ConfirmAction.UninstallPlugin =>
            "该插件将从 Profile 的 bundles 中移除，并删除 node_modules 中的包文件。"
            + "事务失败会自动回滚，并重启原 Runtime。",
        ConfirmAction.StopRuntime =>
            "工作台会与 DSH Web UI 断开，正在进行的 Agent 会话随之中断。"
            + "会话内容不会丢失——仅追加日志已落盘，重连后可继续。",
        ConfirmAction.ActivateRuntime =>
            "激活会立即切换 Runtime 并重启，失败将自动回退到当前版本。"
            + "为旧版构建的第三方插件可能加载失败，届时可用安全模式修复。",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
    };

    /// <summary>确认按钮文案。用具体动词，不用「确定」——按钮本身要说明后果。</summary>
    public static string ConfirmLabel(ConfirmAction action) => action switch
    {
        ConfirmAction.UninstallPlugin => "确认卸载",
        ConfirmAction.StopRuntime => "停止 Runtime",
        ConfirmAction.ActivateRuntime => "激活并重启",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
    };

    /// <summary>
    /// 是否用危险样式渲染确认按钮。切换 Runtime 失败会自动回退，属高影响但非破坏性，
    /// 因此走主按钮样式，避免「处处红色」导致用户对危险色脱敏。
    /// </summary>
    public static bool IsDangerous(ConfirmAction action) => action switch
    {
        ConfirmAction.UninstallPlugin => true,
        ConfirmAction.StopRuntime => true,
        ConfirmAction.ActivateRuntime => false,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
    };
}

/// <summary>
/// 二次确认的跨视图请求通道：Feature 视图发起请求，壳（MainWindow）渲染弹层并回传结果。
/// 与 <c>MainWindow.ShowToast</c> 同属表现层交互，不走 MVI Store——弹窗是瞬态的 View 状态，
/// 不是 Feature 状态。
/// </summary>
public static class ConfirmDialog
{
    private static Func<ConfirmAction, string, Task<bool>>? _handler;

    /// <summary>由壳在启动时注册渲染器。重复注册会覆盖（测试与重建窗口场景）。</summary>
    public static void Register(Func<ConfirmAction, string, Task<bool>> handler)
    {
        _handler = handler;
    }

    /// <summary>
    /// 弹出确认框。<paramref name="subject"/> 是弹窗里那行等宽上下文（插件包名 / PID·端口 / 版本变更）。
    /// </summary>
    /// <returns>用户确认返回 true；取消或未接线返回 false。</returns>
    public static Task<bool> ShowAsync(ConfirmAction action, string subject)
    {
        // 未接线时失败关闭（返回 false）：这三处都是破坏性操作，静默执行比不执行危险得多。
        return _handler is null
            ? Task.FromResult(false)
            : _handler(action, subject);
    }
}
