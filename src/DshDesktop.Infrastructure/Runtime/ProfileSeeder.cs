using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using DshDesktop.Infrastructure.Plugins;

namespace DshDesktop.Infrastructure.Runtime;

/// <summary>
/// 表示 Profile 种子复制（Q11-B 决策）：
/// 首次运行前将既有 harness 的 profiles/web（插件清单、业务文件与 node_modules，不含
/// sessions/settings/凭证）一次性复制到独立 DSH_HOME——robocopy 跟随符号链接展开真实依赖，
/// 得到不依赖源盘的自包含副本，保证 Offline First（§34），之后两套数据各自演进。
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
    /// 复制清单元数据、业务文件与 node_modules，仅排除 .generations 与 .dsh-module-fallback。
    /// 依赖树在复制中即已 materialize；安装只是空壳现场（声明的 bundle 一个都解析不出来）的兜底，
    /// 缺了它 DSH 启动时 resolveBundleDir 会抛
    /// "cannot resolve profile bundle"（2026-09-13 / 09-14 两次现场：Runtime ExitCode=1）。
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

        // 每次启动归一化清单（清除代际投影残留 + 悬空 overrides）：目标已存在时复制分支不执行，
        // 而工作台市场的更新随时可能重新写入代际模型元数据（2026-09-17 实机：市场按代际模型
        // 更新后 5 个投影插件实目录被删、重新启用即 cannot resolve profile bundle → ExitCode=1）。
        ProfileManifestFixups.NormalizeToFlatModel(targetProfile);

        // 每次启动重写虚拟存储指向（同理不能只挂在复制分支）：数据根迁移 / 快照还原 / 更早的种子
        // 都会让目标 profile 带着**旧位置**的 virtualStoreDir，此后任何 pnpm 操作都报
        // ERR_PNPM_UNEXPECTED_VIRTUAL_STORE 并回滚（2026-09-19 v0.1.6 实机：数据根迁移把
        // 旧根 .modules.yaml 一并带入，插件安装 dshmarket@latest 失败回滚）。幂等：值已一致不写盘。
        RewriteVirtualStoreDir(targetProfile);

        await EnsureDependenciesAsync(targetProfile, installer, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 校验并（必要时）重建 profile 依赖树：清单声明了 bundle、却一个都解析不出来时安装。
    /// 目标 profile 已存在也校验——复制已完成但依赖缺失的半成功现场必须能自愈。
    /// 判据是「声明的 bundle 全部不可解析」而非「node_modules 目录是否存在」：
    /// 2026-09-14 实机现场 node_modules 存在，但真实依赖被埋在 node_modules/node_modules/
    /// （一次未完成的安装留下的空壳），旧判据直接跳过，DSH 仍抛
    /// "cannot resolve profile bundle" → Runtime ExitCode=1，该状态永久无法自愈。
    /// 反之只要还能解析出任一 bundle 就不重装：pnpm install 会用 link: 悬空链接覆盖已
    /// materialize 的真实依赖目录（overrides 的 .generations 目标已被排除），
    /// 把可启动现场改成不可启动现场。
    /// </summary>
    private static async Task EnsureDependenciesAsync(
        string profileDir,
        DependencyInstaller installer,
        CancellationToken cancellationToken)
    {
        string[] bundles = ProfileBundleProbe.DeclaredBundles(profileDir);
        if (bundles.Length == 0 || bundles.Any(bundle => ProfileBundleProbe.IsBundleResolved(profileDir, bundle)))
        {
            return;
        }

        // 重建前归一化清单（剥离代际投影残留 + 悬空 overrides）：否则 pnpm install 会把
        // 已 materialize 的真实依赖重建为悬空 junction，打坏 profile。
        ProfileManifestFixups.NormalizeToFlatModel(profileDir);

        (int exitCode, string outputTail) = await installer(profileDir, cancellationToken).ConfigureAwait(false);
        if (exitCode != 0)
        {
            // 静默失败会让「有清单无依赖」的不可启动状态无痕延续，必须让调用方看到。
            throw new InvalidOperationException(
                $"Profile 依赖树重建失败（pnpm 退出码 {exitCode}）：{profileDir}。{outputTail}");
        }
    }

    private static async Task CopyProfileAsync(
        string sourceProfile,
        string targetProfile,
        CancellationToken cancellationToken)
    {
        // robocopy 默认跟随联接点复制真实内容，得到自包含副本（不依赖 pnpm store）。
        // 退出码 0-7 均为成功（含"已复制/无额外文件"等非致命状态）。
        // 只排除两类目录：
        // - .dsh-module-fallback：DSH 启动自愈要求该目录由自己管理
        //   （实目录会报 "exists and is not a symlink or dsh-managed module proxy"）。
        // - .generations：package.json 的 link:../.generations/live/... overrides 目标，
        //   其内容已由 node_modules 下的符号链接展开承载，无需重复复制。
        //
        // ⚠️ node_modules 必须复制（2026-09-14 实机教训）：源 profile 的 node_modules 里
        // dsh-context / dshmarket 等是指向 profiles/.generations/live/<genId>/ 的符号链接，
        // robocopy 跟随联接点把它们展开成真实目录，这正是自包含副本的由来。
        // 一旦排除它，再跑 pnpm install 只能产出 link: 悬空符号链接（overrides 目标同时被排除），
        // DSH resolveBundleDir 抛 cannot resolve profile bundle → Runtime ExitCode=1。
        // 为规避「跨盘后 pnpm 操作报 ERR_PNPM_UNEXPECTED_VIRTUAL_STORE」而排除它是本末倒置：
        // 该报错只影响后续 pnpm 操作，启动可用性由 Node 解析 node_modules/<bundle> 决定，
        // 根本不读 virtualStoreDir；旧方案依赖 NodeJsToolRunner 的自动重试去重置虚拟存储，
        // 但实机证明该重试本身因缺少 --force / --no-frozen-lockfile 而失败（2026-09 现场），
        // 且即便成功也会 purge 再重建、风险高。现改为：每次启动把 .modules.yaml 的 virtualStoreDir
        // 重写为本 profile 的绝对路径（SeedIfNeededAsync 复制分支之外统一调用 RewriteVirtualStoreDir），
        // 从源头消除跨盘错位，使复制出的 profile 立刻可离线、无破坏地执行 pnpm 操作。只改 virtualStoreDir；
        // storeDir 由 .npmrc 决定、跨盘仍匹配，不处理 .pnpm-workspace-state-v1.json（无证据有害）。
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

        // robocopy 可能把源端 junction 原样复制成悬空 junction（源端 .generations 在复制根之外、
        // 目标端必然悬空）→ DSH 启动期 resolveBundleDir 失败。复制后立即实体化：把悬空联接点
        // 替换为种子源端的真实内容（2026-09-14 实机根因）。
        ReparsePointMaterializer.Materialize(
            Path.Combine(targetProfile, "node_modules"),
            Path.Combine(sourceProfile, "node_modules"));
    }

    /// <summary>
    /// 重写 node_modules/.modules.yaml 的 virtualStoreDir 为当前 profile 的绝对路径，
    /// 从源头消除跨盘后的 ERR_PNPM_UNEXPECTED_VIRTUAL_STORE（实机根因）。只改 virtualStoreDir，
    /// 不改 storeDir（由 .npmrc 决定、跨盘仍匹配）；文件/字段缺失则静默跳过；值已一致则不动（幂等）。
    ///
    /// ⚠️ 该文件**文件名是 yaml、内容是 pnpm 写出的 JSON**，故按 JSON 字符串值做外科式替换
    /// （早期按 YAML 标量 `virtualStoreDir: &lt;path&gt;` 匹配的写法在本仓实机上完全空转）。
    /// </summary>
    internal static void RewriteVirtualStoreDir(string profileDir)
    {
        string modulesManifest = Path.Combine(profileDir, "node_modules", ".modules.yaml");
        if (!File.Exists(modulesManifest))
        {
            return;
        }

        string json = File.ReadAllText(modulesManifest);
        int keyAt = json.IndexOf("\"virtualStoreDir\"", StringComparison.Ordinal);
        if (keyAt < 0)
        {
            return;
        }

        int valueStart = json.IndexOf('"', json.IndexOf(':', keyAt) + 1);
        int valueEnd = valueStart < 0 ? -1 : json.IndexOf('"', valueStart + 1);
        if (valueStart < 0 || valueEnd < 0)
        {
            return;
        }

        // Windows 绝对路径在 JSON 里只有反斜杠需转义（pnpm 自身也是这么写的）。
        string target = Path.Combine(profileDir, "node_modules", ".pnpm").Replace("\\", "\\\\", StringComparison.Ordinal);
        if (json.AsSpan(valueStart + 1, valueEnd - valueStart - 1).SequenceEqual(target))
        {
            return; // 已一致，幂等，无需写入。
        }

        File.WriteAllText(
            modulesManifest,
            string.Concat(json.AsSpan(0, valueStart + 1), target, json.AsSpan(valueEnd)));
    }

    /// <summary>
    /// 用 vendored pnpm 重建 profile 依赖树（离线优先策略收敛在
    /// <see cref="Plugins.NodeJsToolRunner.InstallWithOfflineFallbackAsync"/>）。
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

        return await Plugins.NodeJsToolRunner.InstallWithOfflineFallbackAsync(
            nodePath, pnpmCjsPath, profileDir, cancellationToken).ConfigureAwait(false);
    }
}
