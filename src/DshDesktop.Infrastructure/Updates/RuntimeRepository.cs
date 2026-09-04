using System.Text.Json.Nodes;
using DshDesktop.Application.Updates;
using DshDesktop.Domain.Updates;
using DshDesktop.Infrastructure.Plugins;

namespace DshDesktop.Infrastructure.Updates;

/// <summary>
/// 表示 DSH Runtime 仓库（§36 side-by-side；安装走 npm——调研实测 pnpm 全新目录 EPERM，
/// docs/DSH-Runtime-Bootstrap-Research.md §5）。
/// </summary>
public sealed class RuntimeRepository : IRuntimeRepository
{
    private const string DshPackageName = "@deepseek-ai/dsh";

    private readonly string _runtimeRootDir;
    private readonly string _nodePath;
    private readonly string? _npmCjsPath;
    private readonly string _borrowedEntryPath;

    /// <summary>
    /// 初始化 Runtime 仓库。
    /// </summary>
    /// <param name="runtimeRootDir">side-by-side 版本根目录（不存在则创建）。</param>
    /// <param name="nodePath">node.exe 路径。</param>
    /// <param name="npmCjsPath">npm-cli.js 路径；null 时安装与查询不可用。</param>
    /// <param name="borrowedEntryPath">借用的外部 DSH 入口（bin.js）路径。</param>
    public RuntimeRepository(
        string runtimeRootDir,
        string nodePath,
        string? npmCjsPath,
        string borrowedEntryPath)
    {
        _runtimeRootDir = runtimeRootDir;
        _nodePath = nodePath;
        _npmCjsPath = npmCjsPath;
        _borrowedEntryPath = borrowedEntryPath;
        Directory.CreateDirectory(_runtimeRootDir);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DshRuntimeInfo>> ListRuntimesAsync(
        string? activeRuntime,
        CancellationToken cancellationToken)
    {
        List<DshRuntimeInfo> runtimes = [];

        string borrowedVersion = ReadDshVersion(
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(_borrowedEntryPath)!, "..")));
        runtimes.Add(new DshRuntimeInfo(borrowedVersion, activeRuntime is null, IsBorrowed: true));

        foreach (string dir in Directory.GetDirectories(_runtimeRootDir))
        {
            string versionDir = Path.GetFileName(dir);
            string entryPackageDir = Path.Combine(dir, "node_modules", "@deepseek-ai", "dsh");
            if (Directory.Exists(entryPackageDir))
            {
                runtimes.Add(new DshRuntimeInfo(
                    ReadDshVersion(entryPackageDir),
                    string.Equals(activeRuntime, versionDir, StringComparison.Ordinal),
                    IsBorrowed: false));
            }
        }

        return Task.FromResult<IReadOnlyList<DshRuntimeInfo>>(runtimes);
    }

    /// <inheritdoc />
    public async Task<string> GetLatestVersionAsync(string channel, CancellationToken cancellationToken)
    {
        string npm = RequireNpm();
        (int exitCode, string stdout) = await NodeJsToolRunner.RunCaptureAsync(
            _nodePath, npm, _runtimeRootDir,
            ["view", $"{DshPackageName}@{channel}", "version"], cancellationToken).ConfigureAwait(false);
        if (exitCode != 0 || string.IsNullOrWhiteSpace(stdout))
        {
            throw new InvalidOperationException($"npm view 查询失败（退出码 {exitCode}）：{stdout}");
        }

        return stdout.Split('\n')[0].Trim();
    }

    /// <inheritdoc />
    public async Task InstallAsync(string version, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        string npm = RequireNpm();
        string targetDir = Path.Combine(_runtimeRootDir, version);
        Directory.CreateDirectory(targetDir);

        (int exitCode, string outputTail) = await NodeJsToolRunner.RunAsync(
            _nodePath, npm, targetDir,
            ["install", $"{DshPackageName}@{version}", "--no-audit", "--no-fund"], cancellationToken)
            .ConfigureAwait(false);
        if (exitCode != 0)
        {
            TryDeleteFailedInstall(targetDir);
            throw new InvalidOperationException($"npm 安装 DSH {version} 失败（退出码 {exitCode}）。{outputTail}");
        }

        string entry = Path.Combine(targetDir, "node_modules", "@deepseek-ai", "dsh", "lib", "bin.js");
        if (!File.Exists(entry))
        {
            TryDeleteFailedInstall(targetDir);
            throw new InvalidOperationException($"安装完成但找不到 DSH 入口：{entry}");
        }
    }

    /// <inheritdoc />
    public async Task<string?> GetLatestPluginVersionAsync(string name, CancellationToken cancellationToken)
    {
        if (_npmCjsPath is null)
        {
            return null;
        }

        (int exitCode, string stdout) = await NodeJsToolRunner.RunCaptureAsync(
            _nodePath, _npmCjsPath, _runtimeRootDir,
            ["view", name, "version"], cancellationToken).ConfigureAwait(false);
        return exitCode == 0 && !string.IsNullOrWhiteSpace(stdout)
            ? stdout.Split('\n')[0].Trim()
            : null;
    }

    private string RequireNpm()
    {
        if (string.IsNullOrWhiteSpace(_npmCjsPath) || !File.Exists(_npmCjsPath))
        {
            throw new InvalidOperationException(
                "找不到 npm（npmCjsPath），Runtime 自举安装不可用。请检查 dsh-desktop.config.json。");
        }

        return _npmCjsPath;
    }

    private static string ReadDshVersion(string packageDir)
    {
        string packageJsonPath = Path.Combine(packageDir, "package.json");
        if (!File.Exists(packageJsonPath))
        {
            return "未知";
        }

        JsonNode? node = JsonNode.Parse(File.ReadAllText(packageJsonPath));
        return node?["version"]?.GetValue<string>() ?? "未知";
    }

    private static void TryDeleteFailedInstall(string targetDir)
    {
        try
        {
            Directory.Delete(targetDir, recursive: true);
        }
        catch (IOException)
        {
            // 残留目录不影响下次安装（同名覆盖）。
        }
    }
}
