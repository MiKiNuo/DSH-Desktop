using DshDesktop.Application.Plugins;

namespace DshDesktop.Infrastructure.Plugins;

/// <summary>
/// 表示 Profile 清单快照器（Q3-B / Q5 决策：清单四件套快照，保留最近 5 份；
/// 不快照 node_modules——pnpm-lock.yaml 本来就是可重建的完整快照）。
/// 恢复重建用 --no-frozen-lockfile：回滚场景 lockfile 与配置已漂移，冻结安装必失败且会 purge 改坏环境。
/// </summary>
public sealed class ProfileSnapshotter(
    string profileDir,
    string backupsDir,
    string nodePath,
    string pnpmCjsPath,
    Func<string, string, string, string[], CancellationToken, Task<(int ExitCode, string OutputTail)>>? runner = null)
    : IProfileSnapshotter
{
    private const int MaxSnapshots = 5;

    // runner 可注入以便测试；缺省走真实 NodeJsToolRunner.RunAsync。
    private readonly Func<string, string, string, string[], CancellationToken, Task<(int ExitCode, string OutputTail)>>
        _runner = runner ?? ((np, tc, wd, args, ct) => NodeJsToolRunner.RunAsync(np, tc, wd, args, ct));

    private static readonly string[] ManifestFiles =
    [
        "package.json",
        "pnpm-lock.yaml",
        "cordis.patch.yml",
        ".npmrc",
    ];

    /// <inheritdoc />
    public Task<string> CreateSnapshotAsync(CancellationToken cancellationToken)
    {
        string snapshotId = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
        string snapshotDir = Path.Combine(backupsDir, snapshotId);
        Directory.CreateDirectory(snapshotDir);

        foreach (string file in ManifestFiles)
        {
            string source = Path.Combine(profileDir, file);
            if (File.Exists(source))
            {
                File.Copy(source, Path.Combine(snapshotDir, file), overwrite: true);
            }
        }

        PruneOldSnapshots();
        return Task.FromResult(snapshotId);
    }

    /// <inheritdoc />
    public async Task RestoreAsync(string snapshotId, CancellationToken cancellationToken)
    {
        string snapshotDir = Path.Combine(backupsDir, snapshotId);
        if (!Directory.Exists(snapshotDir))
        {
            throw new InvalidOperationException($"找不到快照：{snapshotDir}");
        }

        foreach (string file in ManifestFiles)
        {
            string backup = Path.Combine(snapshotDir, file);
            if (File.Exists(backup))
            {
                File.Copy(backup, Path.Combine(profileDir, file), overwrite: true);
            }
        }

        (int exitCode, string outputTail) = await _runner(
            nodePath, pnpmCjsPath, profileDir,
            ["install", "--no-frozen-lockfile", "--offline"], cancellationToken).ConfigureAwait(false);
        if (exitCode != 0)
        {
            (exitCode, outputTail) = await _runner(
                nodePath, pnpmCjsPath, profileDir,
                ["install", "--no-frozen-lockfile"], cancellationToken).ConfigureAwait(false);
        }

        if (exitCode != 0)
        {
            throw new InvalidOperationException($"快照恢复后 pnpm 重建失败（退出码 {exitCode}）。{outputTail}");
        }

        // 回滚后 pnpm install 可能把 overrides 包重置为悬空 symlink/junction（2026-09-14 实机：
        // 回滚后 Runtime 启动期 resolveBundleDir 抛 cannot resolve profile bundle → ExitCode=1）。
        // 立即实体化，未显式传种子源时回退默认 harness 路径。
        ReparsePointMaterializer.Materialize(Path.Combine(profileDir, "node_modules"), sourceNodeModulesDir: null);
    }

    private void PruneOldSnapshots()
    {
        if (!Directory.Exists(backupsDir))
        {
            return;
        }

        string[] snapshots = Directory.GetDirectories(backupsDir);
        if (snapshots.Length <= MaxSnapshots)
        {
            return;
        }

        foreach (string old in snapshots
            .OrderBy(path => path, StringComparer.Ordinal)
            .Take(snapshots.Length - MaxSnapshots))
        {
            try
            {
                Directory.Delete(old, recursive: true);
            }
            catch (IOException)
            {
                // 快照清理失败不影响主流程。
            }
        }
    }
}
