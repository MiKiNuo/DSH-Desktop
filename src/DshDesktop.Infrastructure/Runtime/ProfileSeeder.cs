using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DshDesktop.Infrastructure.Runtime;

/// <summary>
/// 表示 Profile 种子复制（Q11-B 决策）：
/// 首次运行前将既有 harness 的 profiles/web（插件清单与业务文件，不含 sessions/settings/凭证）
/// 一次性复制到独立 DSH_HOME，随后重建依赖树，保证 Offline First（§34），之后两套数据各自演进。
/// </summary>
public static class ProfileSeeder
{
    /// <summary>
    /// 依赖安装委托：入参为 profile 目录与取消标记，返回 pnpm 退出码与输出尾部。
    /// 抽为参数以便测试注入（真实实现 = vendored pnpm install）。
    /// </summary>
    public delegate Task<(int ExitCode, string OutputTail)> DependencyInstaller(
        string profileDir,
        CancellationToken cancellationToken);

    /// <summary>
    /// 当 DSH_HOME 下尚无 profiles/web 且种子来源存在时，执行一次性复制，并确保依赖树可用。
    /// 复制清单元数据与业务文件，但排除 pnpm 派生物（node_modules、.generations）
    /// 与 .dsh-module-fallback，随后必须真正重建依赖树——
    /// 只复制清单不装依赖会让 DSH 启动时 resolveBundleDir 抛
    /// "cannot resolve profile bundle"（2026-09-13 现场：Runtime ExitCode=1）。
    /// </summary>
    /// <param name="dshHome">DSH_HOME 数据根目录。</param>
    /// <param name="seedProfileFrom">种子来源 harness 数据目录；null 时跳过。</param>
    /// <param name="nodePath">node.exe 路径。</param>
    /// <param name="pnpmCjsPath">vendored pnpm 入口（pnpm.cjs）路径；null 时无法重建，跳过安装。</param>
    /// <param name="cancellationToken">取消标记。</param>
    public static Task SeedIfNeededAsync(
        string dshHome,
        string? seedProfileFrom,
        string nodePath,
        string? pnpmCjsPath,
        CancellationToken cancellationToken = default)
    {
        return SeedIfNeededAsync(
            dshHome,
            seedProfileFrom,
            (profileDir, ct) => InstallDependenciesAsync(nodePath, pnpmCjsPath, profileDir, ct),
            cancellationToken);
    }

    /// <summary>
    /// 测试重载：允许注入依赖安装实现，避免测试真实调用 pnpm。
    /// </summary>
    internal static async Task SeedIfNeededAsync(
        string dshHome,
        string? seedProfileFrom,
        DependencyInstaller installer,
        CancellationToken cancellationToken = default)
    {
        if (seedProfileFrom is null)
        {
            return;
        }

        string targetProfile = Path.Combine(dshHome, "profiles", "web");
        if (!Directory.Exists(targetProfile))
        {
            string sourceProfile = Path.Combine(seedProfileFrom, "profiles", "web");
            if (!Directory.Exists(sourceProfile))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetProfile)!);
            await CopyProfileAsync(sourceProfile, targetProfile, cancellationToken).ConfigureAwait(false);
        }

        await EnsureDependenciesAsync(targetProfile, installer, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 校验并（必要时）重建 profile 依赖树：清单声明了 bundle 却缺 node_modules 时安装。
    /// 目标 profile 已存在也校验——复制已完成但依赖缺失的半成功现场必须能自愈，
    /// 旧实现「目录存在即 return」会让它永久卡死。
    /// 局限：判据只认 node_modules 目录是否存在，pnpm 半损坏（目录在但包缺失）不在
    /// 此处识别，交由 DSH 自身报错。
    /// </summary>
    private static async Task EnsureDependenciesAsync(
        string profileDir,
        DependencyInstaller installer,
        CancellationToken cancellationToken)
    {
        if (Directory.Exists(Path.Combine(profileDir, "node_modules")) || !DeclaresBundles(profileDir))
        {
            return;
        }

        (int exitCode, string outputTail) = await installer(profileDir, cancellationToken).ConfigureAwait(false);
        if (exitCode != 0)
        {
            // 静默失败会让「有清单无依赖」的不可启动状态无痕延续，必须让调用方看到。
            throw new InvalidOperationException(
                $"Profile 依赖树重建失败（pnpm 退出码 {exitCode}）：{profileDir}。{outputTail}");
        }
    }

    /// <summary>
    /// 清单是否声明了 profile bundle。未声明时 DSH 不加载任何包，不需要依赖树。
    /// </summary>
    private static bool DeclaresBundles(string profileDir)
    {
        string manifestPath = Path.Combine(profileDir, "package.json");
        if (!File.Exists(manifestPath))
        {
            return false;
        }

        try
        {
            return JsonNode.Parse(File.ReadAllText(manifestPath)) is JsonObject manifest
                && manifest["dsh"]?["profile"]?["bundles"] is JsonArray { Count: > 0 };
        }
        catch (JsonException)
        {
            // 清单损坏时由 DSH 自己给出更准确的错误，播种期不越权处理。
            return false;
        }
    }

    private static async Task CopyProfileAsync(
        string sourceProfile,
        string targetProfile,
        CancellationToken cancellationToken)
    {
        // robocopy 默认跟随联接点复制真实内容，得到自包含副本（不依赖 pnpm store）。
        // 退出码 0-7 均为成功（含"已复制/无额外文件"等非致命状态）。
        // 排除三类目录，均由随后的依赖重建或 DSH 首启接管：
        // - .dsh-module-fallback：DSH 启动自愈要求该目录由自己管理
        //   （实目录会报 "exists and is not a symlink or dsh-managed module proxy"）。
        // - node_modules：pnpm 的符号链接图，其 .pnpm-workspace-state-v1.json / .modules.yaml
        //   内嵌源盘绝对路径，复制到异盘后 pnpm 报 ERR_PNPM_UNEXPECTED_VIRTUAL_STORE 并中止。
        // - .generations：package.json 的 link:../.generations/live/... overrides 目标，
        //   源盘存在才有效，目标盘需重新生成。
        ProcessStartInfo psi = new()
        {
            FileName = "robocopy",
            WorkingDirectory = Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add(sourceProfile);
        psi.ArgumentList.Add(targetProfile);
        psi.ArgumentList.Add("/E");
        psi.ArgumentList.Add("/COPY:D");
        psi.ArgumentList.Add("/DCOPY:D");
        psi.ArgumentList.Add("/XD");
        psi.ArgumentList.Add(".dsh-module-fallback");
        psi.ArgumentList.Add("node_modules");
        psi.ArgumentList.Add(".generations");
        psi.ArgumentList.Add("/NFL");
        psi.ArgumentList.Add("/NDL");
        psi.ArgumentList.Add("/NJH");
        psi.ArgumentList.Add("/NJS");

        using Process process = Process.Start(psi)
            ?? throw new InvalidOperationException("无法启动 robocopy 进行 Profile 种子复制。");

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (process.ExitCode > 7)
        {
            throw new InvalidOperationException(
                $"Profile 种子复制失败（robocopy 退出码 {process.ExitCode}）：{sourceProfile} → {targetProfile}");
        }
    }

    /// <summary>
    /// 用 vendored pnpm 重建 profile 依赖树。优先 --offline（§34 Offline First），
    /// 失败后允许联网重试一次（同 PluginProfileRepository.RebuildLockfileAsync 策略）。
    /// </summary>
    private static async Task<(int ExitCode, string OutputTail)> InstallDependenciesAsync(
        string nodePath,
        string? pnpmCjsPath,
        string profileDir,
        CancellationToken cancellationToken)
    {
        // 未配置 vendored pnpm（配置语义：「null 时 lockfile 重建不可用」）时依赖树无法重建——
        // 与 PluginProfileRepository 同口径报错：跳过会让「有清单无依赖」的不可启动状态无痕延续。
        if (string.IsNullOrWhiteSpace(pnpmCjsPath) || !File.Exists(pnpmCjsPath))
        {
            return (1, $"找不到 vendored pnpm（{pnpmCjsPath}），无法重建 Profile 依赖树。请检查 dsh-desktop.config.json。");
        }

        (int exitCode, string outputTail) = await Plugins.NodeJsToolRunner.RunAsync(
            nodePath, pnpmCjsPath, profileDir,
            ["install", "--no-frozen-lockfile", "--offline"], cancellationToken).ConfigureAwait(false);
        if (exitCode != 0)
        {
            (exitCode, outputTail) = await Plugins.NodeJsToolRunner.RunAsync(
                nodePath, pnpmCjsPath, profileDir,
                ["install", "--no-frozen-lockfile"], cancellationToken).ConfigureAwait(false);
        }

        return (exitCode, outputTail);
    }
}
