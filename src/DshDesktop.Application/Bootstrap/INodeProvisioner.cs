using System.ComponentModel;

namespace DshDesktop.Application.Bootstrap;

/// <summary>
/// node 自举端口（组合根拆分批 2a）：把 <c>NodeProvisioner</c>（Infrastructure）的「可用性判定」与
/// 「下载解压」抽象到 Application 层，供启动编排与首启安装链消费，避免 Application 新增对 Infrastructure
/// 的项目引用。组合根注入适配器转调真实实现，返回应用层自持的 <see cref="NodeProvisionResult"/>（不泄漏
/// Infrastructure 的 <c>NodeToolchain</c>）。
/// </summary>
public interface INodeProvisioner
{
    /// <summary>判定 node 是否可用（与 <c>HealRuntimePaths</c> 同口径：PATH 裸名 "node" 视为可用）。</summary>
    /// <param name="nodePath">配置中的 node 路径。</param>
    /// <returns>可用为 true。</returns>
    bool IsNodeAvailable(string? nodePath);

    /// <summary>确保 &lt;dataRoot&gt;\tools\node 下 node 可用，返回工具链路径（已装过短路返回）。</summary>
    /// <param name="dataRoot">数据根；产物落在 &lt;dataRoot&gt;\tools\node。</param>
    /// <param name="progress">下载进度百分比（0-100）；可为 null。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>node.exe 与随包 npm-cli.js 的路径。</returns>
    Task<NodeProvisionResult> EnsureAvailableAsync(
        string dataRoot, IProgress<int>? progress, CancellationToken cancellationToken);
}

/// <summary>node 自举产物（Application 自持，不泄漏 Infrastructure 类型）：node.exe 与随包 npm-cli.js 路径。</summary>
/// <param name="NodePath">node.exe 绝对路径。</param>
/// <param name="NpmCjsPath">npm-cli.js 绝对路径；官方包内置，异常缺失时为 null。</param>
public sealed record NodeProvisionResult(string NodePath, string? NpmCjsPath);
