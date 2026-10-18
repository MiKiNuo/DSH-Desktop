using System.Net.Http.Headers;
using System.Text.Json;
using DshDesktop.Application.Updates;
using Serilog;

namespace DshDesktop.Infrastructure.Updates;

/// <summary>
/// 表示 DSH Runtime 的 GitHub Releases 版本发现源（ADR-0011）。
/// </summary>
/// <remarks>
/// 为什么必须是 releases **列表**而不是 releases/latest：上游 deepseek-ai/deepseek-harness
/// 实测六个 release 的 <c>prerelease</c> 全为 true，而 <c>releases/latest</c> 只返回非预发布的最新一个，
/// 对该仓库直接 404。Desktop 侧（<see cref="GitHubDesktopUpdater"/>）的 latest 写法在此不可复用。
/// 为什么不能用 Release 资产直接安装：实测这些 release 的 <c>assets</c> 为空，装不上东西；
/// 安装仍走 <see cref="IRuntimeRepository.InstallAsync"/> 的 npm side-by-side。
/// 本源只回答「通道内最高版本号是多少」，把 tag <c>dsh-v0.2.1-rc.2</c> 剥成 npm 裸 semver <c>0.2.1-rc.2</c>。
/// </remarks>
public sealed class GitHubRuntimeVersionSource : IRuntimeVersionSource
{
    /// <summary>
    /// releases 列表接口地址（含预发布）。per_page 取 20：上游发布节奏下足够覆盖通道内最高版本，
    /// 且未认证配额为 60 次/小时（检查频率为 UI Ready 一次 + 用户手动，绰绰有余）。
    /// </summary>
    internal const string ReleasesUrl =
        "https://api.github.com/repos/deepseek-ai/deepseek-harness/releases?per_page=20";

    /// <summary>
    /// DSH Runtime 的 tag 前缀。上游是多语言 SDK 同仓，tag 形如 <c>dsh-v0.2.1-rc.2</c>，
    /// 必须按此前缀过滤，否则会把无关 tag 当成 Runtime 版本。
    /// </summary>
    internal const string TagPrefix = "dsh-v";

    private readonly HttpClient _http;
    private readonly ILogger _logger;

    /// <summary>
    /// 初始化 GitHub Runtime 版本发现源（生产入口，真实 HTTP）。
    /// </summary>
    /// <param name="logger">结构化日志。</param>
    public GitHubRuntimeVersionSource(ILogger logger)
        : this(logger, CreateHttpClient())
    {
    }

    /// <summary>
    /// 初始化 GitHub Runtime 版本发现源（全缝注入）。internal 供 DshDesktop.Tests 直测（InternalsVisibleTo）。
    /// </summary>
    /// <param name="logger">结构化日志。</param>
    /// <param name="http">HTTP 客户端。</param>
    internal GitHubRuntimeVersionSource(ILogger logger, HttpClient http)
    {
        _logger = logger.ForContext("Source", "DshRuntimeVersionSource");
        _http = http;
    }

    /// <inheritdoc />
    public async Task<string?> TryGetLatestAsync(
        RuntimeReleaseChannel channel,
        CancellationToken cancellationToken)
    {
        try
        {
            string json = await _http.GetStringAsync(ReleasesUrl, cancellationToken).ConfigureAwait(false);
            string? latest = SelectLatest(json, channel);
            if (latest is null)
            {
                _logger.Information("Update.Dsh.GitHubNoCandidate {Channel}", channel);
                return null;
            }

            _logger.Information("Update.Dsh.GitHubResolved {Channel} {Version}", channel, latest);
            return latest;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException)
        {
            // 限流（403/429）、网络中断、仓库改名或转私有（404）、响应非预期形态一律降级为「源不可用」，
            // 由 RuntimeRepository 回退 npm dist-tag——不能因检查通道故障让用户看不到任何更新。
            _logger.Warning("Update.Dsh.GitHubUnavailable {Channel} {Error}", channel, exception.Message);
            return null;
        }
    }

    /// <summary>
    /// 从 releases 列表响应中挑出通道内最高版本。
    /// internal 供 DshDesktop.Tests 直测。
    /// </summary>
    /// <param name="json">GitHub API 响应体（数组）。</param>
    /// <param name="channel">发布通道。</param>
    /// <returns>npm 裸 semver 版本；响应不可解析或通道内无候选为 null。</returns>
    internal static string? SelectLatest(string json, RuntimeReleaseChannel channel)
    {
        GitHubReleaseResponse[]? releases;
        try
        {
            releases = JsonSerializer.Deserialize(json, GitHubReleaseJsonContext.Default.GitHubReleaseResponseArray);
        }
        catch (JsonException)
        {
            return null;
        }

        string? best = null;
        foreach (GitHubReleaseResponse release in releases ?? [])
        {
            if (release.Draft)
            {
                continue;
            }

            if (channel == RuntimeReleaseChannel.Stable && release.Prerelease)
            {
                continue;
            }

            if (release.TagName is not { Length: > 0 } tag
                || !tag.StartsWith(TagPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            string version = tag[TagPrefix.Length..];
            if (version.Length == 0)
            {
                continue;
            }

            if (best is null || RuntimeVersion.IsNewer(version, best))
            {
                best = version;
            }
        }

        return best;
    }

    private static HttpClient CreateHttpClient()
    {
        // GitHub API 要求 User-Agent，否则 403。
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DSH-Desktop", "1.0"));
        return client;
    }
}