using DshDesktop.Infrastructure.Updates;

namespace DshDesktop.Tests;

/// <summary>
/// GitHubDesktopUpdater 测试（Inno 安装形态的自更新适配器，internal 经 InternalsVisibleTo 直测）。
/// 流程：检查（GitHub Releases API）→ 下载（Setup exe + SHA256 校验）→ 应用（提权静默安装 + 退出）。
/// </summary>
public sealed class GitHubDesktopUpdaterTests
{
    [Test]
    public async Task IsNewerVersion_NewerTag_ReturnsTrue()
    {
        await Assert.That(GitHubDesktopUpdater.IsNewerVersion("0.1.2", "v0.1.3")).IsTrue();
    }

    [Test]
    public async Task IsNewerVersion_SameVersion_ReturnsFalse()
    {
        await Assert.That(GitHubDesktopUpdater.IsNewerVersion("0.1.2", "v0.1.2")).IsFalse();
        await Assert.That(GitHubDesktopUpdater.IsNewerVersion("0.1.2", "0.1.2")).IsFalse();
    }

    [Test]
    public async Task IsNewerVersion_OlderTag_ReturnsFalse()
    {
        await Assert.That(GitHubDesktopUpdater.IsNewerVersion("0.1.2", "v0.1.1")).IsFalse();
    }

    [Test]
    public async Task IsNewerVersion_UnparseableTag_ReturnsFalse()
    {
        await Assert.That(GitHubDesktopUpdater.IsNewerVersion("0.1.2", "latest")).IsFalse();
        await Assert.That(GitHubDesktopUpdater.IsNewerVersion("0.1.2", "")).IsFalse();
    }

    private const string ReleaseJson = """
        {
          "tag_name": "v0.1.3",
          "assets": [
            {
              "name": "DSH-Desktop-0.1.3-full.nupkg",
              "browser_download_url": "https://example.com/DSH-Desktop-0.1.3-full.nupkg",
              "size": 21671625,
              "digest": "sha256:aaaa"
            },
            {
              "name": "DSH-Desktop-Setup-0.1.3.exe",
              "browser_download_url": "https://example.com/DSH-Desktop-Setup-0.1.3.exe",
              "size": 26133193,
              "digest": "sha256:52d8ab6a6c5f5001f674739daf53f51c9f4ff7c9320c5575736c9ecdb7fb0a53"
            }
          ]
        }
        """;

    [Test]
    public async Task TryParseLatestRelease_ValidJson_PicksSetupAsset()
    {
        GitHubDesktopUpdater.PendingDesktopUpdate? update =
            GitHubDesktopUpdater.TryParseLatestRelease(ReleaseJson);

        await Assert.That(update).IsNotNull();
        await Assert.That(update!.Version).IsEqualTo("0.1.3");
        await Assert.That(update.AssetUrl).IsEqualTo("https://example.com/DSH-Desktop-Setup-0.1.3.exe");
        await Assert.That(update.AssetName).IsEqualTo("DSH-Desktop-Setup-0.1.3.exe");
        await Assert.That(update.Size).IsEqualTo(26133193L);
        await Assert.That(update.Sha256).IsEqualTo("52d8ab6a6c5f5001f674739daf53f51c9f4ff7c9320c5575736c9ecdb7fb0a53");
    }

    [Test]
    public async Task TryParseLatestRelease_NoSetupAsset_ReturnsNull()
    {
        const string json = """
            { "tag_name": "v0.1.3", "assets": [ { "name": "RELEASES", "browser_download_url": "https://example.com/RELEASES", "size": 81 } ] }
            """;

        await Assert.That(GitHubDesktopUpdater.TryParseLatestRelease(json)).IsNull();
    }

    [Test]
    public async Task TryParseLatestRelease_InvalidJson_ReturnsNull()
    {
        await Assert.That(GitHubDesktopUpdater.TryParseLatestRelease("not json")).IsNull();
        await Assert.That(GitHubDesktopUpdater.TryParseLatestRelease("{}")).IsNull();
    }

    [Test]
    public async Task CheckForUpdates_NotInstalled_ReturnsNullWithoutHttp()
    {
        int httpCalls = 0;
        GitHubDesktopUpdater updater = NewUpdater(
            isInstalled: false,
            handler: _ =>
            {
                httpCalls++;
                return JsonResponse(ReleaseJson);
            });

        DshDesktop.Application.Updates.DesktopUpdateInfo? result =
            await updater.CheckForUpdatesAsync(CancellationToken.None);

        await Assert.That(result).IsNull();
        await Assert.That(httpCalls).IsEqualTo(0);
        await Assert.That(updater.IsInstalled).IsFalse();
    }

    [Test]
    public async Task CheckForUpdates_InstalledNewerRelease_ReturnsUpdate()
    {
        GitHubDesktopUpdater updater = NewUpdater(
            isInstalled: true,
            handler: _ => JsonResponse(ReleaseJson));

        DshDesktop.Application.Updates.DesktopUpdateInfo? result =
            await updater.CheckForUpdatesAsync(CancellationToken.None);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Version).IsEqualTo("0.1.3");
    }

    [Test]
    public async Task CheckForUpdates_InstalledSameVersion_ReturnsNull()
    {
        GitHubDesktopUpdater updater = NewUpdater(
            isInstalled: true,
            handler: _ => JsonResponse(ReleaseJson),
            currentVersion: "0.1.3");

        await Assert.That(await updater.CheckForUpdatesAsync(CancellationToken.None)).IsNull();
    }

    private static GitHubDesktopUpdater NewUpdater(
        bool isInstalled,
        Func<HttpRequestMessage, HttpResponseMessage> handler,
        string currentVersion = "0.1.2",
        string? downloadDirectory = null,
        Action<System.Diagnostics.ProcessStartInfo>? launcher = null,
        Action<int>? exitProcess = null)
    {
        return new GitHubDesktopUpdater(
            Serilog.Core.Logger.None,
            currentVersion,
            new HttpClient(new StubHttpMessageHandler(handler)),
            () => isInstalled,
            launcher ?? (_ => { }),
            exitProcess ?? (_ => { }),
            downloadDirectory
                ?? Path.Combine(Path.GetTempPath(), "dsh-updater-tests", Guid.NewGuid().ToString("N")));
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };
    }

    [Test]
    public async Task Download_WithoutPendingCheck_Throws()
    {
        GitHubDesktopUpdater updater = NewUpdater(isInstalled: true, handler: _ => JsonResponse(ReleaseJson));

        await Assert.That(async () => await updater.DownloadAsync(null, CancellationToken.None))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Download_ValidAsset_WritesFileAndReportsProgress()
    {
        byte[] payload = System.Text.Encoding.UTF8.GetBytes("fake-setup-payload");
        string downloadDir = NewDownloadDir();
        try
        {
            GitHubDesktopUpdater updater = NewUpdater(
                isInstalled: true,
                handler: request => request.RequestUri!.AbsoluteUri.Contains("api.github.com", StringComparison.Ordinal)
                    ? JsonResponse(ReleaseJsonWithDigest(payload))
                    : BytesResponse(payload),
                downloadDirectory: downloadDir);
            _ = await updater.CheckForUpdatesAsync(CancellationToken.None);
            var progress = new ProgressCapture();

            await updater.DownloadAsync(progress, CancellationToken.None);

            string expectedPath = Path.Combine(downloadDir, "DSH-Desktop-Setup-0.1.3.exe");
            await Assert.That(File.Exists(expectedPath)).IsTrue();
            await Assert.That(await File.ReadAllBytesAsync(expectedPath)).IsEquivalentTo(payload);
            await Assert.That(progress.Values.Count > 0).IsTrue();
            await Assert.That(progress.Values[^1]).IsEqualTo(100);
        }
        finally
        {
            Directory.Delete(downloadDir, recursive: true);
        }
    }

    [Test]
    public async Task Download_HashMismatch_ThrowsAndDeletesFile()
    {
        byte[] payload = System.Text.Encoding.UTF8.GetBytes("tampered-payload");
        string downloadDir = NewDownloadDir();
        try
        {
            // JSON 里声明的是另一份内容的哈希（用真实 sha256 生成见 ReleaseJsonWithDigest 入参差异）。
            GitHubDesktopUpdater updater = NewUpdater(
                isInstalled: true,
                handler: request => request.RequestUri!.AbsoluteUri.Contains("api.github.com", StringComparison.Ordinal)
                    ? JsonResponse(ReleaseJsonWithDigest(System.Text.Encoding.UTF8.GetBytes("other-content")))
                    : BytesResponse(payload),
                downloadDirectory: downloadDir);
            _ = await updater.CheckForUpdatesAsync(CancellationToken.None);

            await Assert.That(async () => await updater.DownloadAsync(null, CancellationToken.None))
                .Throws<InvalidDataException>();
            await Assert.That(File.Exists(Path.Combine(downloadDir, "DSH-Desktop-Setup-0.1.3.exe"))).IsFalse();
        }
        finally
        {
            Directory.Delete(downloadDir, recursive: true);
        }
    }

    [Test]
    public async Task ApplyAndRestart_WithoutDownload_Throws()
    {
        GitHubDesktopUpdater updater = NewUpdater(isInstalled: true, handler: _ => JsonResponse(ReleaseJson));

        await Assert.That(() => updater.ApplyAndRestart()).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ApplyAndRestart_AfterDownload_LaunchesElevatedSilentInstallerAndExits()
    {
        byte[] payload = System.Text.Encoding.UTF8.GetBytes("fake-setup-payload");
        string downloadDir = NewDownloadDir();
        try
        {
            System.Diagnostics.ProcessStartInfo? launched = null;
            int? exitCode = null;
            GitHubDesktopUpdater updater = NewUpdater(
                isInstalled: true,
                handler: request => request.RequestUri!.AbsoluteUri.Contains("api.github.com", StringComparison.Ordinal)
                    ? JsonResponse(ReleaseJsonWithDigest(payload))
                    : BytesResponse(payload),
                downloadDirectory: downloadDir,
                launcher: startInfo => launched = startInfo,
                exitProcess: code => exitCode = code);
            _ = await updater.CheckForUpdatesAsync(CancellationToken.None);
            await updater.DownloadAsync(null, CancellationToken.None);

            updater.ApplyAndRestart();

            await Assert.That(launched).IsNotNull();
            await Assert.That(launched!.FileName).IsEqualTo(Path.Combine(downloadDir, "DSH-Desktop-Setup-0.1.3.exe"));
            await Assert.That(launched.Verb).IsEqualTo("runas");
            await Assert.That(launched.UseShellExecute).IsTrue();
            await Assert.That(launched.Arguments).Contains("/VERYSILENT");
            await Assert.That(exitCode).IsEqualTo(0);
        }
        finally
        {
            Directory.Delete(downloadDir, recursive: true);
        }
    }

    [Test]
    public async Task InstallerAppId_MatchesUpdaterRegistryProbe()
    {
        // 安装形态判定的唯一锚点：Inno AppId 与更新器注册表探测键必须一致，否则安装后 IsInstalled
        // 永远为 false、自更新整体静默失灵（两处字面量一旦改一处即失守）。
        const string appId = "8F3A2C1E-7B4D-4E6F-9A1B-2C3D4E5F6A7B";
        string? root = XamlScan.FindRepositoryRoot();
        await Assert.That(root).IsNotNull();

        string issPath = Path.Combine(root!, "installer", "DshDesktop.iss");
        string updaterPath = Path.Combine(
            root!, "src", "DshDesktop.Infrastructure", "Updates", "GitHubDesktopUpdater.cs");
        string iss = (await File.ReadAllTextAsync(issPath)).Replace("\r\n", "\n");
        string updater = (await File.ReadAllTextAsync(updaterPath)).Replace("\r\n", "\n");

        await Assert.That(iss.Contains(appId, StringComparison.Ordinal)).IsTrue();
        await Assert.That(updater.Contains(appId, StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task InstallerLanguageFile_VendoredAndReferenced()
    {
        // v0.1.3 发布失败根因：CI 的 choco Inno 不含 ChineseSimplified.isl。
        // 语言文件必须随仓库自带且 iss 以相对路径引用（相对脚本目录解析）。
        string? root = XamlScan.FindRepositoryRoot();
        await Assert.That(root).IsNotNull();

        string issPath = Path.Combine(root!, "installer", "DshDesktop.iss");
        string islPath = Path.Combine(root!, "installer", "Languages", "ChineseSimplified.isl");
        string iss = (await File.ReadAllTextAsync(issPath)).Replace("\r\n", "\n");

        await Assert.That(iss.Contains("MessagesFile: \"Languages\\ChineseSimplified.isl\"", StringComparison.Ordinal)).IsTrue();
        await Assert.That(File.Exists(islPath)).IsTrue();
    }

    private static string NewDownloadDir()
    {
        return Path.Combine(Path.GetTempPath(), "dsh-updater-tests", Guid.NewGuid().ToString("N"));
    }

    private static string ReleaseJsonWithDigest(byte[] payload)
    {
        string sha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(payload));
        return $$"""
            {
              "tag_name": "v0.1.3",
              "assets": [
                {
                  "name": "DSH-Desktop-Setup-0.1.3.exe",
                  "browser_download_url": "https://example.com/DSH-Desktop-Setup-0.1.3.exe",
                  "size": {{payload.Length}},
                  "digest": "sha256:{{sha}}"
                }
              ]
            }
            """;
    }

    private static HttpResponseMessage BytesResponse(byte[] payload)
    {
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(payload),
        };
    }

    private sealed class ProgressCapture : IProgress<int>
    {
        public List<int> Values { get; } = [];

        public void Report(int value) => Values.Add(value);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(respond(request));
        }
    }
}
