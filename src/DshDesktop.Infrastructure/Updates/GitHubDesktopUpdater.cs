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
    /// 字面量单源：InnoSetupInstallProbe（数据根安装形态判定）复用本常量，
    /// 文本守卫 InstallerAppId_MatchesUpdaterRegistryProbe 锁定与 iss 一致。
    /// </summary>
    internal const string UninstallSubKey =
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
    /// 下载单飞锁：后台预下载与手动下载共用同一目标路径，必须串行化。
    /// </summary>
    private readonly SemaphoreSlim _downloadGate = new(1, 1);

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
        // 单飞：后台预下载与手动下载写同一路径，交叠时先到者已以 FileShare.None 持有文件，
        // 后到者用 FileMode.Create 打开必抛 sharing violation（2026-09-19 实机 "being used by
        // another process"）。串行化后再以"已下载即复用"短路，重复调用幂等。
        await _downloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // 无持有更新时抛错：让调用方走失败回流，避免 UI 的"下载中"悬挂。
            PendingDesktopUpdate pending = _pendingUpdate
                ?? throw new InvalidOperationException("没有已检查到的 Desktop 更新，请先检查更新。");

            if (await IsReusableAsync(pending, cancellationToken).ConfigureAwait(false))
            {
                _logger.Information("Update.Desktop.DownloadReused {Version}", pending.Version);
                progress?.Report(100);
                return;
            }

            await DownloadCoreAsync(pending, progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _downloadGate.Release();
        }
    }

    /// <summary>
    /// 判断此前下载的包是否仍可复用：资产名带版本号，同名即同版本；再校验文件在、字节数吻合、
    /// SHA256 一致（被外部删除、写了一半的残包、同尺寸篡改都判为不可复用，回落到重新下载）。
    /// 复用不跳过哈希：包落在同账户可任意改写的 temp 目录，且随后会被提权执行。
    /// </summary>
    private async Task<bool> IsReusableAsync(PendingDesktopUpdate pending, CancellationToken cancellationToken)
    {
        if (_downloadedSetupPath is not { } path
            || !string.Equals(Path.GetFileName(path), pending.AssetName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var file = new FileInfo(path);
        if (!file.Exists || file.Length != pending.Size)
        {
            return false;
        }

        return pending.Sha256 is not { Length: > 0 } expected
            || await Sha256MatchesAsync(path, expected, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 校验文件 SHA256 是否为期望值（hex，大小写不敏感）。
    /// </summary>
    private static async Task<bool> Sha256MatchesAsync(
        string path, string expected, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        byte[] hash = await System.Security.Cryptography.SHA256
            .HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return string.Equals(Convert.ToHexStringLower(hash), expected, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 实际下载并校验安装包（调用方须已持有 <see cref="_downloadGate"/>）。
    /// </summary>
    private async Task DownloadCoreAsync(
        PendingDesktopUpdate pending, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_downloadDirectory);
        string targetPath = Path.Combine(_downloadDirectory, pending.AssetName);

        using (HttpResponseMessage response = await _http
            .GetAsync(pending.AssetUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false))
        {
            _ = response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? pending.Size;
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

        if (pending.Sha256 is { Length: > 0 } expected
            && !await Sha256MatchesAsync(targetPath, expected, cancellationToken).ConfigureAwait(false))
        {
            File.Delete(targetPath);
            throw new InvalidDataException("下载的安装包校验失败（SHA256 不匹配），已丢弃。");
        }

        _downloadedSetupPath = targetPath;
        _logger.Information("Update.Desktop.Downloaded {Version}", pending.Version);
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

