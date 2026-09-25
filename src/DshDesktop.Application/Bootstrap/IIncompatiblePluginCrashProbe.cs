namespace DshDesktop.Application.Bootstrap;

/// <summary>
/// 从「DSH 进程在就绪前退出」失败消息解析肇事插件包名的端口（组合根拆分批 1）。
/// 把 <c>IncompatiblePluginCrashProbe</c>（Infrastructure）的纯函数探针抽象到 Application 层，
/// 供崩溃漂移自愈入口消费，避免 Application 新增对 Infrastructure 的项目引用。
/// </summary>
public interface IIncompatiblePluginCrashProbe
{
    /// <summary>识别失败消息中的「命名导出缺失」崩溃并解析肇事插件包名；无法定位返回 null。</summary>
    /// <param name="startFailureMessage">启动失败异常消息（内含 stderr 末尾）。</param>
    /// <returns>肇事插件包名；无法定位或 in-box bundle 返回 null。</returns>
    string? TryParseOffender(string startFailureMessage);
}
