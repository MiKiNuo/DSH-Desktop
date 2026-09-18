using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using DshDesktop.Application.Updates;
using Microsoft.Win32;
using Serilog;

namespace DshDesktop.Infrastructure.Updates;

/// <summary>
/// 表示 <see cref="IDesktopUpdater"/> 的 GitHub Releases 实现（Inno 安装形态）：
/// 检查走 releases/latest API，下载 Setup exe 并校验 SHA256，应用 = 提权静默安装 + 退出本进程。
/// 未安装形态（dotnet run / 便携解压）下检查短路返回 null。
/// </summary>
public sealed class GitHubDesktopUpdater : IDesktopUpdater
{
    /// <summary>
    /// Setup 资产名前缀（release.yml 上传 DSH-Desktop-Setup-&lt;版本&gt;.exe）。
    /// </summary>
    private const string SetupAssetPrefix = "DSH-Desktop-Setup-";

    /// <summary>
    /// GitHub releases/latest API 地址。
    /// </summary>
    private const string LatestReleaseUrl =
        "https://api.github.com/repos/MiKiNuo/DSH-Desktop/releases/latest";

    /// <summary>
    /// Inno 卸载注册表子键（AppId 与 installer/DshDesktop.iss 必须一致；per-machine 安装落 HKLM64）。
    /// </summary>
    private const string UninstallSubKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{8F3A2C1E-7B4D-4E6F-9A1B-2C3D4E5F6A7B}_is1";

    private readonly ILogger _logger;
    private readonly string _currentVersion;
    private readonly HttpClient _http;
    private readonly Func<bool> _isInstalledProbe;
    private readonly Action<ProcessStartInfo> _launcher;
    private readonly Action<int> _exitProcess;
    private readonly string _downloadDirectory;

    private PendingDesktopUpdate? _pendingUpdate;
    private string? _downloadedSetupPath;

    /// <summary>
    /// 初始化 GitHub Desktop 更新适配器（生产入口，真实探测/下载/提权）。
    /// </summary>
    /// <param name="logger">结构化日志。</param>
    /// <param name="currentVersion">当前版本（DesktopInfo.Version）。</param>
    public GitHubDesktopUpdater(ILogger logger, string currentVersion)
        : this(
            logger,
            currentVersion,
            CreateHttpClient(),
            ProbeInstalled,
            startInfo => Process.Start(startInfo),
            Environment.Exit,
            Path.Combine(Path.GetTempPath(), "DSH-Desktop-Update"))
    {
    }

    /// <summary>
    /// 初始化 GitHub Desktop 更新适配器（全缝注入）。internal 供 DshDesktop.Tests 直测（InternalsVisibleTo）。
    /// </summary>
    internal GitHubDesktopUpdater(
        ILogger logger,
        string currentVersion,
        HttpClient http,
        Func<bool> isInstalledProbe,
        Action<ProcessStartInfo> launcher,
        Action<int> exitProcess,
        string downloadDirectory)
    {
        _logger = logger.ForContext("Source", "DesktopUpdater");
        _currentVersion = currentVersion;
        _http = http;
        _isInstalledProbe = isInstalledProbe;
        _launcher = launcher;
        _exitProcess = exitProcess;
        _downloadDirectory = downloadDirectory;
    }

    /// <inheritdoc />
    public bool IsInstalled => _isInstalledProbe();

    /// <inheritdoc />
    public async Task<DesktopUpdateInfo?> CheckForUpdatesAsync(CancellationToken cancellationToken)
    {
        if (!IsInstalled)
        {
            return null;
        }

        string json = await _http.GetStringAsync(LatestReleaseUrl, cancellationToken).ConfigureAwait(false);
        PendingDesktopUpdate? parsed = TryParseLatestRelease(json);
        if (parsed is null || !IsNewerVersion(_currentVersion, parsed.Version))
        {
            _pendingUpdate = null;
            return null;
        }

        _pendingUpdate = parsed;
        _logger.Information("Update.Desktop.Available {Version}", parsed.Version);
        return new DesktopUpdateInfo(parsed.Version);
    }

    private static HttpClient CreateHttpClient()
    {
        // GitHub API 要求 User-Agent，否则 403。
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DSH-Desktop", "1.0"));
        return client;
    }

    private static bool ProbeInstalled()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using RegistryKey? key = RegistryKey
                .OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                .OpenSubKey(UninstallSubKey);
            return key is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }


    /// <summary>
    /// 表示一个待应用的 Desktop 更新（检查到后由适配器持有，一次性语义）。
    /// </summary>
    /// <param name="Version">目标版本号（去 v 前缀）。</param>
    /// <param name="AssetName">Setup 资产文件名。</param>
    /// <param name="AssetUrl">资产下载地址。</param>
    /// <param name="Size">资产字节数。</param>
    /// <param name="Sha256">资产 SHA256（hex，小写）；API 未上报为 null。</param>
    internal sealed record PendingDesktopUpdate(
        string Version, string AssetName, string AssetUrl, long Size, string? Sha256);

    /// <summary>
    /// 判断候选 tag 是否比当前版本新。tag 允许带 v 前缀；任一不可解析即不视为更新。
    /// internal 供 DshDesktop.Tests 直测（InternalsVisibleTo）。
    /// </summary>
    /// <param name="currentVersion">当前版本（编译期常量 DesktopInfo.Version）。</param>
    /// <param name="candidateTag">候选 Release tag。</param>
    /// <returns>候选严格更新为 true。</returns>
    internal static bool IsNewerVersion(string currentVersion, string candidateTag)
    {
        string candidateText = candidateTag.TrimStart('v', 'V');
        return Version.TryParse(currentVersion, out Version? current)
            && Version.TryParse(candidateText, out Version? candidate)
            && candidate > current;
    }

    /// <summary>
    /// 解析 releases/latest 响应并挑出 Setup 资产；响应无效或无 Setup 资产返回 null。
    /// internal 供 DshDesktop.Tests 直测。
    /// </summary>
    /// <param name="json">GitHub API 响应体。</param>
    /// <returns>待应用更新；不可用为 null。</returns>
    internal static PendingDesktopUpdate? TryParseLatestRelease(string json)
    {
        GitHubReleaseResponse? release;
        try
        {
            release = JsonSerializer.Deserialize(json, GitHubReleaseJsonContext.Default.GitHubReleaseResponse);
        }
        catch (JsonException)
        {
            return null;
        }

        if (release?.TagName is not { Length: > 0 } tag)
        {
            return null;
        }

        GitHubReleaseAsset? asset = release.Assets?.FirstOrDefault(a =>
            a.Name is not null
            && a.Name.StartsWith(SetupAssetPrefix, StringComparison.Ordinal)
            && a.Name.EndsWith(".exe", StringComparison.Ordinal)
            && a.BrowserDownloadUrl is not null);

        if (asset is null)
        {
            return null;
        }

        string? sha256 = asset.Digest is { Length: > 7 } digest
            && digest.StartsWith("sha256:", StringComparison.Ordinal)
                ? digest["sha256:".Length..]
                : null;

        return new PendingDesktopUpdate(
            tag.TrimStart('v', 'V'), asset.Name!, asset.BrowserDownloadUrl!, asset.Size, sha256);
    }

    /// <inheritdoc />
    public async Task DownloadAsync(IProgress<int>? progress, CancellationToken cancellationToken)
    {
        // 无持有更新时抛错：让调用方走失败回流，避免 UI 的"下载中"悬挂。
        if (_pendingUpdate is null)
        {
            throw new InvalidOperationException("没有已检查到的 Desktop 更新，请先检查更新。");
        }

        Directory.CreateDirectory(_downloadDirectory);
        string targetPath = Path.Combine(_downloadDirectory, _pendingUpdate.AssetName);

        using (HttpResponseMessage response = await _http
            .GetAsync(_pendingUpdate.AssetUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false))
        {
            _ = response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? _pendingUpdate.Size;
            await using Stream source = await response.Content
                .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using FileStream destination = new(
                targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
            byte[] buffer = new byte[81920];
            long received = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                received += read;
                if (total > 0)
                {
                    progress?.Report((int)(received * 100 / total));
                }
            }
        }

        if (_pendingUpdate.Sha256 is { Length: > 0 } expected)
        {
            byte[] hash;
            await using (FileStream stream = new(targetPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                hash = await System.Security.Cryptography.SHA256
                    .HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            }

            if (!string.Equals(Convert.ToHexStringLower(hash), expected, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(targetPath);
                throw new InvalidDataException("下载的安装包校验失败（SHA256 不匹配），已丢弃。");
            }
        }

        _downloadedSetupPath = targetPath;
        _logger.Information("Update.Desktop.Downloaded {Version}", _pendingUpdate.Version);
    }

    /// <inheritdoc />
    public void ApplyAndRestart()
    {
        if (_pendingUpdate is null || _downloadedSetupPath is null)
        {
            throw new InvalidOperationException("没有已下载的 Desktop 更新可应用。");
        }

        // 提权静默覆盖安装：UAC 一次确认（用户取消时 Process.Start 抛 Win32Exception 1223，
        // 包装为友好文案走失败回流）；安装器 [Run] 段负责以原用户身份重启新版。
        _logger.Information("Update.Desktop.ApplyRestart {Version}", _pendingUpdate.Version);
        var startInfo = new ProcessStartInfo
        {
            FileName = _downloadedSetupPath,
            Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART",
            UseShellExecute = true,
            Verb = "runas",
        };

        try
        {
            _launcher(startInfo);
        }
        catch (System.ComponentModel.Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            throw new InvalidOperationException("已取消管理员授权，更新未执行。", exception);
        }

        _exitProcess(0);
    }
}

/// <summary>
/// GitHub releases/latest 响应（仅取所需字段）。
/// </summary>
internal sealed class GitHubReleaseResponse
{
    /// <summary>Release tag（如 v0.1.3）。</summary>
    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }

    /// <summary>资产列表。</summary>
    [JsonPropertyName("assets")]
    public List<GitHubReleaseAsset>? Assets { get; set; }
}

/// <summary>
/// GitHub Release 资产。
/// </summary>
internal sealed class GitHubReleaseAsset
{
    /// <summary>文件名。</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>浏览器下载地址。</summary>
    [JsonPropertyName("browser_download_url")]
    public string? BrowserDownloadUrl { get; set; }

    /// <summary>字节数。</summary>
    [JsonPropertyName("size")]
    public long Size { get; set; }

    /// <summary>摘要（形如 sha256:&lt;hex&gt;）。</summary>
    [JsonPropertyName("digest")]
    public string? Digest { get; set; }
}

/// <summary>
/// GitHub Releases JSON 源生成上下文（AOT 兼容，与 DshConfigJsonContext 同一先例）。
/// </summary>
[JsonSerializable(typeof(GitHubReleaseResponse))]
internal sealed partial class GitHubReleaseJsonContext : JsonSerializerContext;

