namespace DshDesktop.Application.Bootstrap;

/// <summary>
/// Profile 种子复制端口（组合根拆分批 2a）：把 <c>ProfileSeeder</c>（Infrastructure）抽象到 Application 层，
/// 供启动编排经配置加载端口调用，不新增 Application 对 Infrastructure 的项目引用。组合根注入适配器转调真实实现。
/// </summary>
public interface IProfileSeeder
{
    /// <summary>当 DSH_HOME 下尚无 profiles/web 且种子来源存在时，执行一次性复制并确保依赖树可用。</summary>
    /// <param name="dshHome">DSH_HOME 数据根目录。</param>
    /// <param name="seedProfileFrom">种子来源 harness 数据目录；null 时跳过。</param>
    /// <param name="nodePath">node.exe 路径。</param>
    /// <param name="pnpmCjsPath">vendored pnpm 入口（pnpm.cjs）路径；null 时无法重建，跳过安装。</param>
    /// <param name="cancellationToken">取消标记。</param>
    Task SeedIfNeededAsync(
        string dshHome,
        string? seedProfileFrom,
        string nodePath,
        string? pnpmCjsPath,
        CancellationToken cancellationToken);
}
