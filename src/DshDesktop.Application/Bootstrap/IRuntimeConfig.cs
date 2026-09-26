namespace DshDesktop.Application.Bootstrap;

/// <summary>
/// 启动编排所需的配置端口（组合根拆分批 2a）：在 <see cref="IRuntimeBootstrapConfig"/> 自愈读写语义之上，
/// 扩展配置加载与编排期读取/回写字段，使 Application 层编排器可经抽象消费 <c>DshDesktopConfig</c>
/// （具体件仍在组合根适配器中，Application 不新增对 Infrastructure 的项目引用）。
/// 路径字段在编排期会被写回（pnpm/node 自举后），故为可读写。
/// </summary>
public interface IRuntimeConfig : IRuntimeBootstrapConfig
{
    /// <summary>加载并装载配置（组合根适配器内部转调 <c>DshDesktopConfigStore.LoadOrDetectAsync</c>）。</summary>
    /// <param name="cancellationToken">取消标记。</param>
    Task LoadAsync(CancellationToken cancellationToken);

    /// <summary>外观主题（"Dark"/"Light"）。</summary>
    string Theme { get; }

    /// <summary>node.exe 路径（pnpm/node 自举后会被回写）。</summary>
    string NodePath { get; set; }

    /// <summary>npm-cli.js 路径（node 自举后会被回写）。</summary>
    string? NpmCjsPath { get; set; }

    /// <summary>vendored pnpm 入口路径（pnpm 自举后会被回写）。</summary>
    string? PnpmCjsPath { get; set; }

    /// <summary>DSH_HOME 数据根目录。</summary>
    string DshHome { get; }

    /// <summary>Profile 种子来源 harness 数据目录；null 时跳过。</summary>
    string? SeedProfileFrom { get; }

    /// <summary>DSH 更新通道（npm dist-tag）。</summary>
    string DshChannel { get; }
}
