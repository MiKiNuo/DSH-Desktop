using System.Text.Json.Nodes;
using DshDesktop.Application.Updates;
using DshDesktop.Domain.Updates;
using DshDesktop.Infrastructure.Plugins;
using Serilog;

namespace DshDesktop.Infrastructure.Updates;

/// <summary>
/// 表示 DSH Runtime 仓库（§36 side-by-side；安装走 npm——调研实测 pnpm 全新目录 EPERM，
/// docs/DSH-Runtime-Bootstrap-Research.md §5）。
/// </summary>
/// <remarks>
/// 版本发现（ADR-0011）分两段：**发现**走 GitHub Releases（<see cref="GitHubRuntimeVersionSource"/>），
/// **安装**仍走 npm。分段的起因是上游把 rc 版只打在 dist-tag <c>next</c> 上、<c>latest</c> 停在
/// 0.2.0-rc.2，导致查 dist-tag 的客户端看不到已发布的 0.2.1-rc.2。GitHub 不可用时回退 dist-tag，
/// 保证检查通道故障不致于让用户完全看不到更新。
/// </remarks>
public sealed class RuntimeRepository : IRuntimeRepository
{
    private const string DshPackageName = "@deepseek-ai/dsh";

    private readonly string _runtimeRootDir;
    private readonly string _nodePath;
    private readonly string? _npmCjsPath;
    private readonly string _borrowedEntryPath;

    /// <summary>版本发现源；null 表示不查 GitHub（直接走 npm dist-tag）。</summary>
    private readonly IRuntimeVersionSource? _versionSource;

    /// <summary>npm dist-tag 查询委托；null 时用基于 NodeJsToolRunner 的真实实现。</summary>
    private readonly Func<string, CancellationToken, Task<string>> _npmDistTagLookup;

    /// <summary>
    /// 初始化 Runtime 仓库（生产入口：GitHub 版本发现 + 真实 npm 回退）。
    /// </summary>
    /// <param name="runtimeRootDir">side-by-side 版本根目录（不存在则创建）。</param>
    /// <param name="nodePath">node.exe 路径。</param>
    /// <param name="npmCjsPath">npm-cli.js 路径；null 时安装与 dist-tag 回退不可用。</param>
    /// <param name="borrowedEntryPath">借用的外部 DSH 入口（bin.js）路径。</param>
    /// <param name="logger">结构化日志。</param>
    public RuntimeRepository(
        string runtimeRootDir,
        string nodePath,
        string? npmCjsPath,
        string borrowedEntryPath,
        ILogger? logger = null)
        : this(
            runtimeRootDir,
            nodePath,
            npmCjsPath,
            borrowedEntryPath,
            new GitHubRuntimeVersionSource(logger ?? Log.Logger),
            npmVersionLookup: null)
    {
    }

    /// <summary>
    /// 初始化 Runtime 仓库（全缝注入）。internal 供 DshDesktop.Tests 直测（InternalsVisibleTo）。
    /// </summary>
    /// <param name="runtimeRootDir">side-by-side 版本根目录（不存在则创建）。</param>
    /// <param name="nodePath">node.exe 路径。</param>
    /// <param name="npmCjsPath">npm-cli.js 路径。</param>
    /// <param name="borrowedEntryPath">借用的外部 DSH 入口（bin.js）路径。</param>
    /// <param name="versionSource">版本发现源；null 表示跳过 GitHub。</param>
    /// <param name="npmVersionLookup">npm dist-tag 查询委托；null 时用真实实现。</param>
    internal RuntimeRepository(
        string runtimeRootDir,
        string nodePath,
        string? npmCjsPath,
        string borrowedEntryPath,
        IRuntimeVersionSource? versionSource,
        Func<string, CancellationToken, Task<string>>? npmVersionLookup)
    {
        _runtimeRootDir = runtimeRootDir;
        _nodePath = nodePath;
        _npmCjsPath = npmCjsPath;
        _borrowedEntryPath = borrowedEntryPath;
        _versionSource = versionSource;
        _npmDistTagLookup = npmVersionLookup ?? QueryNpmDistTagAsync;
        Directory.CreateDirectory(_runtimeRootDir);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DshRuntimeInfo>> ListRuntimesAsync(
        string? activeRuntime,
        CancellationToken cancellationToken)
    {
        List<DshRuntimeInfo> runtimes = [];

        // 借用入口可为空（HealRuntimePaths 在借用安装被删后清空 dshEntryPath；全新无借用环境同形态）：
        // 空路径下 GetDirectoryName 返回 null，Path.Combine 必抛 ArgumentNullException(path1)——跳过即可。
        if (!string.IsNullOrWhiteSpace(_borrowedEntryPath))
        {
            string borrowedVersion = ReadDshVersion(
                Path.GetFullPath(Path.Combine(Path.GetDirectoryName(_borrowedEntryPath)!, "..")));
            runtimes.Add(new DshRuntimeInfo(borrowedVersion, activeRuntime is null, IsBorrowed: true));
        }

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
    /// <remarks>
    /// ADR-0011：先问 GitHub Releases（tag 即真相），拿不到再回退 npm dist-tag。
    /// 通道字面量为 ADR-0011 前的 dist-tag 时由 <see cref="RuntimeChannel.Normalize"/> 归入默认档。
    /// </remarks>
    public async Task<string> GetLatestVersionAsync(string channel, CancellationToken cancellationToken)
    {
        RuntimeReleaseChannel resolved = RuntimeChannel.Normalize(channel);

        if (_versionSource is not null
            && await _versionSource.TryGetLatestAsync(resolved, cancellationToken).ConfigureAwait(false)
                is { } discovered)
        {
            return discovered;
        }

        // fail-open：GitHub 限流 / 断网 / 仓库改名都退回 dist-tag 查询，不让检查通道故障吞掉更新提示。
        return await _npmDistTagLookup(RuntimeChannel.ToDistTag(resolved), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>npm dist-tag 查询（<c>npm view @deepseek-ai/dsh@&lt;tag&gt; version</c>）：ADR-0011 的回退路径。</summary>
    private async Task<string> QueryNpmDistTagAsync(string distTag, CancellationToken cancellationToken)
    {
        string npm = RequireNpm();
        (int exitCode, string stdout) = await NodeJsToolRunner.RunCaptureAsync(
            _nodePath, npm, _runtimeRootDir,
            ["view", $"{DshPackageName}@{distTag}", "version"], cancellationToken).ConfigureAwait(false);
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
