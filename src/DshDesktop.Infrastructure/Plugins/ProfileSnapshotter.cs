using DshDesktop.Application.Plugins;

namespace DshDesktop.Infrastructure.Plugins;

/// <summary>
/// 表示 Profile 清单快照器（Q3-B / Q5 决策：清单四件套 + frozen-lockfile 重建，
/// 保留最近 5 份；不快照 node_modules——pnpm-lock.yaml 本来就是可重建的完整快照）。
/// </summary>
public sealed class ProfileSnapshotter(
    string profileDir,
    string backupsDir,
    string nodePath,
    string pnpmCjsPath) : IProfileSnapshotter
{
    private const int MaxSnapshots = 5;

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

        (int exitCode, string outputTail) = await NodeJsToolRunner.RunAsync(
            nodePath, pnpmCjsPath, profileDir,
            ["install", "--frozen-lockfile", "--offline"], cancellationToken).ConfigureAwait(false);
        if (exitCode != 0)
        {
            (exitCode, outputTail) = await NodeJsToolRunner.RunAsync(
                nodePath, pnpmCjsPath, profileDir,
                ["install", "--frozen-lockfile"], cancellationToken).ConfigureAwait(false);
        }

        if (exitCode != 0)
        {
            throw new InvalidOperationException($"快照恢复后 pnpm 重建失败（退出码 {exitCode}）。{outputTail}");
        }
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
