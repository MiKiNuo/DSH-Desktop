using System.Diagnostics;

namespace DshDesktop.Infrastructure.Runtime;

/// <summary>
/// 表示 Profile 种子复制（Q11-B 决策）：
/// 首次运行前将既有 harness 的 profiles/web（插件 + node_modules，不含 sessions/settings/凭证）
/// 一次性复制到独立 DSH_HOME，保证 Offline First（§34），之后两套数据各自演进。
/// </summary>
public static class ProfileSeeder
{
    /// <summary>
    /// 当 DSH_HOME 下尚无 profiles/web 且种子来源存在时，执行一次性复制。
    /// 复制清单元数据与业务文件，但排除 pnpm 派生物（node_modules、.generations）
    /// 与 .dsh-module-fallback，由 DSH 首启自行重建，保证跨盘可迁移。
    /// </summary>
    /// <param name="dshHome">DSH_HOME 数据根目录。</param>
    /// <param name="seedProfileFrom">种子来源 harness 数据目录；null 时跳过。</param>
    /// <param name="cancellationToken">取消标记。</param>
    public static async Task SeedIfNeededAsync(
        string dshHome,
        string? seedProfileFrom,
        CancellationToken cancellationToken = default)
    {
        if (seedProfileFrom is null)
        {
            return;
        }

        string targetProfile = Path.Combine(dshHome, "profiles", "web");
        if (Directory.Exists(targetProfile))
        {
            return;
        }

        string sourceProfile = Path.Combine(seedProfileFrom, "profiles", "web");
        if (!Directory.Exists(sourceProfile))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(targetProfile)!);

        // robocopy 默认跟随联接点复制真实内容，得到自包含副本（不依赖 pnpm store）。
        // 退出码 0-7 均为成功（含"已复制/无额外文件"等非致命状态）。
        // 排除三类目录，均由 DSH 首启自行重建：
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
}
